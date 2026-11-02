# -*- coding: utf-8 -*-
"""宇宙矩阵 + 3级增产剂优化 v2（LP）——严格锁定选定路线。

规则：仅使用用户选定路径的配方（PICK 锁死，LP 不引入其它配方）。
- Mk.III 增产：产量 +25%；配方每次运行需为每个原料单位喷涂 1 次（1 个增产剂=60次）
- 可喷：T1熔炉/T2化工/T3精炼/T4制造/T15科研；不可喷：T5对撞机(反物质)/T8分馏(重氢)/采集/接收
- 增产剂配方 R106/107/108 可自身喷涂（自举）
- 铁/铜/石/钛 不计；其余资源用量 ≤ 未喷涂基线；目标=非4矿资源总投入最小（等权）
输出：最优喷涂决策 / 原料对比 / 增产剂用量；并给“全喷涂”对照方案。"""
import io, os, json
from collections import defaultdict

import pulp

BASE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "json"))

def load(n):
    with io.open(os.path.join(BASE, n + ".json"), encoding="utf-8") as f:
        return json.load(f)["rows"]

items = {r["ID"]: r for r in load("ItemProtoSet")}
recs  = {r["ID"]: r for r in load("RecipeProtoSet")}

def iname(i):
    it = items.get(i)
    return (it["Name"] if it else "#%d" % i)

# ---------- 锁定路径（用户确认） ----------
PICK = {
    1109: [(17, 1.0)],             # 高能石墨: 煤炼
    1112: [(61, 1.0)],             # 金刚石: 金伯利
    1123: [(32, 1.0)],             # 石墨烯: 可燃冰
    1124: [(35, 0.5), (33, 0.5)],  # 碳纳米管: 刺笋+石墨烯 1:1
    1113: [(62, 1.0)],             # 晶格硅: 分形硅石
    1126: [(28, 0.5), (29, 0.5)],  # 卡西米尔: 钛晶石+光栅石 1:1
    1206: [(99, 1.0)],             # 粒子容器: 电磁涡轮
    1114: [(16, 1.0)],             # 精炼油: 原油精炼
    6001: [(9, 1.0)], 6002: [(18, 1.0)], 6003: [(27, 1.0)], 6004: [(55, 1.0)],
    6005: [(102, 1.0)], 1122: [(74, 1.0)], 6006: [(75, 1.0)],
}
PROL = {1141: [(106, 1.0)], 1142: [(107, 1.0)], 1143: [(108, 1.0)]}

SPRAYABLE_T = {1, 2, 3, 4, 15}
COLLECT = {1000, 1003, 1004, 1005, 1006, 1007, 1011, 1012, 1013, 1014, 1015, 1016,
           1116, 1117, 1120, 1121, 1208}    # 矿/采集品/特殊源（1120氢信号产+采集、1121重氢、1208光子）

RF = defaultdict(list)
for r in recs.values():
    for rid in r["Results"]:
        RF[rid].append(r)

# ---------- 锁定配方集：PICK + 唯一配方自动补全（多配方且未选=报错） ----------
PICK_ALL = dict(PICK)
PICK_ALL.update(PROL)

# 先收集所有可达中间品（含未锁定的），再逐轮补全唯一配方
while True:
    needs = set()
    for item, rids in PICK_ALL.items():
        for rid, w in rids:
            for i in recs[rid]["Items"]:
                if i not in PICK_ALL and i not in COLLECT:
                    needs.add(i)
    if not needs:
        break
    changed = False
    for item in needs:
        if item in PICK_ALL or item in COLLECT:
            continue
        rs = RF.get(item, [])
        if len(rs) == 1:
            PICK_ALL[item] = [(rs[0]["ID"], 1.0)]
            changed = True
        elif len(rs) == 0:
            COLLECT.add(item)
            changed = True
        else:
            raise RuntimeError("多配方且未锁定: %s(id=%d) 配方=%s" % (
                iname(item), item, [r["ID"] for r in rs]))
    if not changed:
        raise RuntimeError("仍有未锁定物品: %s" % [iname(i) for i in needs if i not in PICK_ALL and i not in COLLECT])

used_items = set(PICK_ALL.keys())
locked = {}
for cur, rids in PICK_ALL.items():
    for rid, w in rids:
        locked[rid] = recs[rid]
print("锁定配方数:", len(locked), "| 物品数:", len(used_items))

def can_spray(r):
    return r["Type"] in SPRAYABLE_T

# ---------- 基线（未喷涂，纯锁定路线，与 matrix_balance 一致） ----------
def baseline():
    RES = defaultdict(float)
    def solve(item_id, amount):
        if item_id in COLLECT:
            RES[item_id] += amount
            return
        for rid, w in PICK_ALL.get(item_id, []):
            r = recs[rid]
            num = amount * w / r["ResultCounts"][r["Results"].index(item_id)]
            for i, c in zip(r["Items"], r["ItemCounts"]):
                solve(i, num * c)
    solve(6006, 1.0)
    bp = [0.0]
    def byprod(item_id, amount):
        for rid, w in PICK_ALL.get(item_id, []):
            r = recs[rid]
            num = amount * w / r["ResultCounts"][r["Results"].index(item_id)]
            for p, c in zip(r["Results"], r["ResultCounts"]):
                if p == 1120:
                    bp[0] += num * c
            for i, c in zip(r["Items"], r["ItemCounts"]):
                byprod(i, num * c)
    byprod(6006, 1.0)
    h2_net = RES.get(1120, 0.0) - bp[0] + RES.get(1121, 0.0)   # 净需求 + 分馏耗氢(1:1)
    RES[1120] = max(h2_net, 0.0)
    return RES

BASE_RES = baseline()
print("基线（未喷涂，每 1 宇宙矩阵）:")
for k in sorted(BASE_RES, key=lambda x: -BASE_RES[x]):
    if BASE_RES[k] > 0:
        print("   %-12s %.3f" % (iname(k), BASE_RES[k]))

# ---------- LP ----------
NON4 = [i for i in BASE_RES if i not in (1001, 1002, 1004, 1005) and BASE_RES.get(i, 0) > 0]
WEIGHTS = {i: 1.0 for i in NON4 if i not in (1120, 1121, 1208)}   # 默认等权；稀缺权重见 sensitivity 脚本
CONSTRAIN = [i for i in NON4 if i not in (1120, 1121)]   # 氢/重氢单独（采集不设目标但有上限）

def build_lp(spray_all=False, force=None, weights=None):
    prob = pulp.LpProblem("prolif", pulp.LpMinimize)
    v = {rid: pulp.LpVariable("v_%d" % rid, lowBound=0) for rid in locked}
    p = {rid: pulp.LpVariable("p_%d" % rid, lowBound=0) for rid in locked}
    for rid in locked:
        prob += p[rid] <= v[rid], "plev%d" % rid
        f = force.get(rid) if force else None
        if not can_spray(locked[rid]) or f is False:
            prob += p[rid] == 0, "nospray%d" % rid
        elif f is True or spray_all:
            prob += p[rid] == v[rid], "allspray%d" % rid
    # 多配方并行锁定（1:1 等产量）
    if 35 in v and 33 in v and v[35] != v[33]:
        prob += v[35] * locked[35]["ResultCounts"][locked[35]["Results"].index(1124)] == \
                v[33] * locked[33]["ResultCounts"][locked[33]["Results"].index(1124)], "cnratio"
    if 28 in v and 29 in v:
        prob += v[28] == v[29], "caratio"
    # 物品平衡（中间品 + 宇宙矩阵 + Mk.III 外部需求）
    bal_items = set()
    for rid, r in locked.items():
        for p2 in r["Results"]:
            if p2 not in (1120, 1121) and p2 not in COLLECT:
                bal_items.add(p2)
    M = pulp.LpVariable("Mk3", lowBound=0)
    for j in bal_items:
        expr = pulp.LpAffineExpression()
        for rid, r in locked.items():
            if j in r["Results"]:
                idx = r["Results"].index(j)
                y = r["ResultCounts"][idx]
                expr += y * v[rid] + 0.25 * y * p[rid]
            if j in r["Items"]:
                idx = r["Items"].index(j)
                expr -= r["ItemCounts"][idx] * v[rid]
        rhs = (pulp.LpAffineExpression() + 1.0) if j == 6006 else (M if j == 1143 else 0.0)
        prob += expr == rhs, "bal%d" % j
    # 喷涂 -> Mk.III
    spray = pulp.LpAffineExpression()
    for rid, r in locked.items():
        if can_spray(r):
            spray += sum(r["ItemCounts"]) * p[rid]
    prob += M == spray / 60.0, "mk3link"
    # 重氢（采集+分馏 1:1；分馏耗氢）
    deu_d = pulp.LpAffineExpression()
    for rid, r in locked.items():
        if 1121 in r["Items"]:
            idx = r["Items"].index(1121)
            deu_d += r["ItemCounts"][idx] * v[rid]
    deu_f = pulp.LpVariable("deu_f", lowBound=0)
    deu_c = pulp.LpVariable("deu_c", lowBound=0)
    prob += deu_f + deu_c == deu_d, "deubal"
    prob += deu_f <= BASE_RES.get(1121, 0.0), "deucap"
    # 氢平衡
    h2d = pulp.LpAffineExpression(); h2b = pulp.LpAffineExpression()
    for rid, r in locked.items():
        if 1120 in r["Items"]:
            idx = r["Items"].index(1120)
            h2d += r["ItemCounts"][idx] * v[rid]
        if 1120 in r["Results"]:
            idx = r["Results"].index(1120)
            h2b += r["ResultCounts"][idx] * (v[rid] + 0.25 * p[rid])
    h2c = pulp.LpVariable("h2c", lowBound=0)
    prob += h2d + deu_f == h2b + h2c, "h2bal"
    prob += h2c <= BASE_RES.get(1120, 0.0), "h2cap"
    # 资源上限（非4矿；采集资源也限制）
    for j in CONSTRAIN:
        expr = pulp.LpAffineExpression()
        for rid, r in locked.items():
            if j in r["Items"]:
                idx = r["Items"].index(j)
                expr += r["ItemCounts"][idx] * v[rid]
        prob += expr <= BASE_RES[j], "cap%d" % j
    # 目标：非4矿资源总投入（权重可调；氢/重氢/光子采集不计入目标，只受限）
    wts = weights if weights is not None else WEIGHTS
    obj = pulp.LpAffineExpression()
    for j in NON4:
        if j in (1120, 1121, 1208):
            continue
        expr = pulp.LpAffineExpression()
        for rid, r in locked.items():
            if j in r["Items"]:
                idx = r["Items"].index(j)
                expr += r["ItemCounts"][idx] * v[rid]
        obj += wts.get(j, 1.0) * expr
    prob += obj
    return prob, v, p, M, spray, deu_f, deu_c, h2c, obj

def solve_report(tag, spray_all=False):
    prob, v, p, M, spray, deu_f, deu_c, h2c, obj = build_lp(spray_all)
    st = prob.solve(pulp.PULP_CBC_CMD(msg=0))
    if pulp.LpStatus[st] != "Optimal":
        print("%s: %s" % (tag, pulp.LpStatus[st]))
        return None
    print("\n================ %s ================" % tag)
    print("目标值(非4矿资源总投入): %.3f" % pulp.value(obj))
    sprayed = sorted([rid for rid in locked if (pulp.value(p[rid]) or 0) > 0.001], key=lambda r: -pulp.value(p[r]))
    notsprayed = [rid for rid in locked if can_spray(locked[rid]) and (pulp.value(p[rid]) or 0) <= 0.001 and (pulp.value(v[rid]) or 0) > 0.01]
    print("喷涂配方 %d 条: %s" % (len(sprayed), ", ".join(locked[r]["Name"][:10] for r in sprayed[:40])))
    print("未喷涂可喷配方 %d 条: %s" % (len(notsprayed), ", ".join(locked[r]["Name"][:10] for r in notsprayed[:30])))
    print("喷涂次数 %.1f → 3级增产剂 %.3f 个/个宇宙矩阵" % (pulp.value(spray), pulp.value(M)))
    return dict(v=v, p=p, M=M, spray=spray, deu_f=deu_f, deu_c=deu_c, h2c=h2c, obj=obj)

if __name__ == "__main__":
    opt = solve_report("最优喷涂方案")
    all_ = solve_report("对照：全部可喷配方喷涂", spray_all=True)

    def usage(rid, j):
        return locked[rid]["ItemCounts"][locked[rid]["Items"].index(j)] if j in locked[rid]["Items"] else 0

    print("\n【原料对比】基线 vs 最优 vs 全喷（每 1 宇宙矩阵）")
    for j in sorted(BASE_RES, key=lambda x: -BASE_RES[x]):
        if BASE_RES[j] == 0:
            continue
        def total(res):
            s = 0.0
            for rid in locked:
                u = usage(rid, j)
                if u:
                    s += u * (pulp.value(res["v"][rid]) or 0)
            if j == 1120:
                s = pulp.value(res["h2c"]) or 0
            if j == 1121:
                s = (pulp.value(res["deu_f"]) or 0) + (pulp.value(res["deu_c"]) or 0)
            return s
        b = BASE_RES[j]
        o = total(opt)
        a = total(all_) if all_ else None
        row = "%-12s 基线 %8.3f -> 最优 %8.3f" % (iname(j), b, o)
        if a is not None:
            row += "（全喷 %8.3f）" % a
        row += " %s" % ("✓" if o <= b + 1e-6 else "▲超基线!")
        print(row)
    print("\n氢采集：基线 %.3f → 最优 %.3f" % (BASE_RES[1120], pulp.value(opt["h2c"])))
    print("重氢：需求 %.3f（采集 %.3f / 分馏 %.3f，分馏耗氢同量）" % (
        pulp.value(opt["deu_f"]) + pulp.value(opt["deu_c"]), pulp.value(opt["deu_c"]), pulp.value(opt["deu_f"])))
    # 临界光子 = 反物质配方 R74 投入
    photon = (pulp.value(opt["v"][74]) or 0) * 2
    print("临界光子（射线接收）：%.3f" % photon)
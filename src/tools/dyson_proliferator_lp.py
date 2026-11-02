# -*- coding: utf-8 -*-
"""戴森球建设 × 3级增产剂优化（LP）——太阳帆 20,900/min + 小型运载火箭 150/min。

沿用 matrix-proliferator 的机制与权重：
- Mk.III = 产量+25%；原料单位×1 次喷涂；1 个增产剂=60 次；增产剂生产可自举
- 可喷 T1/T2/T3/T4/T15；铁/铜/石/钛不限制；其余资源 ≤ 无增产基线
- 权重：煤=1 金伯利=1 刺笋=1.5 有机晶体=2 光栅石=3
- 光子合并器=棱镜路线 R68 | 石墨烯=可燃冰 | 碳管 1:1 | 卡西米尔 1:1 | 重氢采集优先
输出：最优喷涂分配 / 原料对比（基线 vs 优化）/ 增产剂用量。"""
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

PICK = {
    1501: [(70, 1.0)], 1502: [(81, 1.0)], 1503: [(83, 1.0)],
    1125: [(80, 1.0)], 1802: [(41, 1.0)], 1404: [(68, 1.0)], 1111: [(11, 1.0)],
    1107: [(66, 1.0)], 1205: [(103, 1.0)], 1103: [(63, 1.0)],
    1123: [(32, 1.0)], 1124: [(35, 0.5), (33, 0.5)], 1126: [(28, 0.5), (29, 0.5)],
    1118: [(26, 1.0)], 1109: [(17, 1.0)], 1112: [(61, 1.0)],  # 金刚石: 金伯利（增产剂Mk.II需要）
}
PROL = {1141: [(106, 1.0)], 1142: [(107, 1.0)], 1143: [(108, 1.0)]}
SPRAYABLE_T = {1, 2, 3, 4, 15}
COLLECT = {1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1011, 1012, 1013, 1014, 1015,
           1016, 1116, 1117, 1120, 1121, 1208}
TARGETS = {1501: 20900.0, 1503: 150.0}   # 太阳帆(含组件内部900) + 火箭/分钟
WEIGHTS = {1006: 1.0, 1012: 1.0, 1015: 1.5, 1117: 2.0, 1014: 3.0}

RF = defaultdict(list)
for r in recs.values():
    for rid in r["Results"]:
        RF[rid].append(r)

ALL = dict(PICK); ALL.update(PROL)
while True:
    needs = set()
    for item, rids in ALL.items():
        for rid, w in rids:
            for i in recs[rid]["Items"]:
                if i not in ALL and i not in COLLECT:
                    needs.add(i)
    if not needs:
        break
    changed = False
    for item in needs:
        if item in ALL or item in COLLECT:
            continue
        rs = RF.get(item, [])
        if len(rs) == 1:
            ALL[item] = [(rs[0]["ID"], 1.0)]; changed = True
        elif len(rs) == 0:
            COLLECT.add(item); changed = True
        else:
            raise RuntimeError("多配方未锁定: %s id=%d" % (iname(item), item))
    if not changed:
        raise RuntimeError("未锁定: %s" % [iname(i) for i in needs if i not in ALL and i not in COLLECT])

locked = {}
for cur, rids in ALL.items():
    for rid, w in rids:
        locked[rid] = recs[rid]
print("锁定配方数:", len(locked))

def can_spray(r):
    return r["Type"] in SPRAYABLE_T

# ---------- 基线（无增产） ----------
def baseline():
    RES = defaultdict(float)
    def solve(item_id, amount):
        if item_id in COLLECT:
            RES[item_id] += amount
            return
        for rid, w in ALL.get(item_id, []):
            r = recs[rid]
            num = amount * w / r["ResultCounts"][r["Results"].index(item_id)]
            for i, c in zip(r["Items"], r["ItemCounts"]):
                solve(i, num * c)
    for iid, amt in TARGETS.items():
        solve(iid, amt)
    bp = [0.0]
    def byprod(item_id, amount):
        for rid, w in ALL.get(item_id, []):
            r = recs[rid]
            num = amount * w / r["ResultCounts"][r["Results"].index(item_id)]
            for p, c in zip(r["Results"], r["ResultCounts"]):
                if p == 1120:
                    bp[0] += num * c
            for i, c in zip(r["Items"], r["ItemCounts"]):
                byprod(i, num * c)
    for iid, amt in TARGETS.items():
        byprod(iid, amt)
    h2_net = RES.get(1120, 0.0) - bp[0]
    RES[1120] = max(h2_net, 0.0)
    return RES

BASE_RES = baseline()
print("基线（无增产，每分钟）:")
for k in sorted(BASE_RES, key=lambda x: -BASE_RES[x]):
    if BASE_RES[k] > 0:
        print("   %-12s %10.1f" % (iname(k), BASE_RES[k]))

# ---------- LP ----------
NON4 = [i for i in BASE_RES if i not in (1001, 1002, 1004, 1005) and BASE_RES.get(i, 0) > 0]

def build_lp(spray_all=False, force=None, weights=None):
    prob = pulp.LpProblem("dyson_prolif", pulp.LpMinimize)
    v = {rid: pulp.LpVariable("v_%d" % rid, lowBound=0) for rid in locked}
    p = {rid: pulp.LpVariable("p_%d" % rid, lowBound=0) for rid in locked}
    for rid in locked:
        prob += p[rid] <= v[rid], "plev%d" % rid
        f = force.get(rid) if force else None
        if not can_spray(locked[rid]) or f is False:
            prob += p[rid] == 0, "nospray%d" % rid
        elif f is True or spray_all:
            prob += p[rid] == v[rid], "allspray%d" % rid
    if 35 in v and 33 in v:
        prob += v[35] * 2 == v[33] * 2, "cnratio"
    if 28 in v and 29 in v:
        prob += v[28] == v[29], "caratio"
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
        rhs = 0.0
        if j in TARGETS:
            rhs = TARGETS[j]
        elif j == 1143:
            rhs = M
        prob += expr == rhs, "bal%d" % j
    spray = pulp.LpAffineExpression()
    for rid, r in locked.items():
        if can_spray(r):
            spray += sum(r["ItemCounts"]) * p[rid]
    prob += M == spray / 60.0, "mk3link"
    deu_d = pulp.LpAffineExpression()
    for rid, r in locked.items():
        if 1121 in r["Items"]:
            idx = r["Items"].index(1121)
            deu_d += r["ItemCounts"][idx] * v[rid]
    deu_f = pulp.LpVariable("deu_f", lowBound=0); deu_c = pulp.LpVariable("deu_c", lowBound=0)
    prob += deu_f + deu_c == deu_d, "deubal"
    prob += deu_f <= BASE_RES.get(1121, 0.0), "deucap"
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
    for j in NON4:
        if j in (1120, 1121, 1006):   # 煤不做上限（增产剂引入的合理消耗）
            continue
        expr = pulp.LpAffineExpression()
        for rid, r in locked.items():
            if j in r["Items"]:
                idx = r["Items"].index(j)
                expr += r["ItemCounts"][idx] * v[rid]
        prob += expr <= BASE_RES[j], "cap%d" % j
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

def run(tag, spray_all=False, force=None):
    prob, v, p, M, spray, deu_f, deu_c, h2c, obj = build_lp(spray_all, force)
    st = prob.solve(pulp.PULP_CBC_CMD(msg=0))
    if pulp.LpStatus[st] != "Optimal":
        print("%s: %s" % (tag, pulp.LpStatus[st]))
        return None
    print("\n==== %s ====" % tag)
    print("目标值(加权): %.1f   Mk.III/min=%.3f  喷涂次数/min=%.0f" % (
        pulp.value(obj), pulp.value(M), pulp.value(spray)))
    on = sorted([rid for rid in locked if (pulp.value(p[rid]) or 0) > 0.001], key=lambda r: -(pulp.value(p[r]) or 0))
    off = [rid for rid in locked if can_spray(locked[rid]) and (pulp.value(p[rid]) or 0) <= 0.001 and (pulp.value(v[rid]) or 0) > 0.01]
    print("喷涂 %d 条: %s" % (len(on), ", ".join("%s" % locked[r]["Name"][:10] for r in on)))
    print("不喷 %d 条: %s" % (len(off), ", ".join("%s" % locked[r]["Name"][:10] for r in off)))
    return dict(v=v, p=p, M=M, spray=spray, deu_f=deu_f, deu_c=deu_c, h2c=h2c, obj=obj)

def usage(res, j):
    s = 0.0
    for rid, r in locked.items():
        if j in r["Items"]:
            s += r["ItemCounts"][r["Items"].index(j)] * (pulp.value(res["v"][rid]) or 0)
    if j == 1120:
        s = pulp.value(res["h2c"]) or 0
    if j == 1121:
        s = (pulp.value(res["deu_f"]) or 0) + (pulp.value(res["deu_c"]) or 0)
    return s

if __name__ == "__main__":
    opt = run("戴森球最优（稀缺权重）")
    all_ = run("戴森球全喷涂对照", spray_all=True)

    print("\n【原料对比】基线 vs 最优 vs 全喷（每分钟）")
    if opt:
        for j in sorted(BASE_RES, key=lambda x: -BASE_RES[x]):
            if BASE_RES[j] == 0:
                continue
            b = BASE_RES[j]; o = usage(opt, j)
            a = usage(all_, j) if all_ else None
            row = "%-12s 基线 %9.0f -> 最优 %9.0f" % (iname(j), b, o)
            if a is not None:
                row += "（全喷 %9.0f）" % a
            row += " %s" % ("✓" if o <= b + 1e-6 else "▲超基线!")
            print(row)
        print("\n氢采集：基线 %.0f → 最优 %.0f" % (BASE_RES.get(1120, 0), pulp.value(opt["h2c"])))
        print("重氢：需求 %.0f（采集 %.0f / 分馏 %.0f）" % (
            pulp.value(opt["deu_f"]) + pulp.value(opt["deu_c"]), pulp.value(opt["deu_c"]), pulp.value(opt["deu_f"])))

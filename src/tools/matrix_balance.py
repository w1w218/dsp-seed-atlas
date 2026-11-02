# -*- coding: utf-8 -*-
"""宇宙矩阵物料平衡：按用户选定的路径，计算每 1/min 宇宙矩阵的原料需求与副产结算。

路径选择（2026-10-16 用户确认）：
  高能石墨=煤炼R17 | 金刚石=金伯利R61 | 石墨烯=可燃冰R32 | 碳纳米管=刺笋R35:石墨烯R33=1:1
  晶格硅=分形硅石R62 | 卡西米尔=钛晶石R28:光栅石R29=1:1 | 粒子容器=电磁涡轮R99
  精炼油=原油R16 | 有机晶体=矿采 | 硫酸=采集 | 重氢=采集+分馏(氢:重氢=1:1) | 氢=副产优先+采集
输出：配方运行次数 / 原料消耗 / 氢·重氢·临界光子结算 / 归一化比例。"""
import io, os, json
from collections import defaultdict

BASE = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "json"))

def load(n):
    with io.open(os.path.join(BASE, n + ".json"), encoding="utf-8") as f:
        return json.load(f)["rows"]

items = {r["ID"]: r for r in load("ItemProtoSet")}
recs  = {r["ID"]: r for r in load("RecipeProtoSet")}

def iname(i):
    it = items.get(i)
    return (it["Name"] if it else "#%d" % i)

# ---- 用户选定的配方集（产出物品 -> [(recipe_id, 权重)]，权重和为 1）----
PICK = {
    1109: [(17, 1.0)],                      # 高能石墨: 煤炼
    1112: [(61, 1.0)],                      # 金刚石: 金伯利
    1123: [(32, 1.0)],                      # 石墨烯: 可燃冰
    1124: [(35, 0.5), (33, 0.5)],           # 碳纳米管: 刺笋:石墨烯 = 1:1
    1113: [(62, 1.0)],                      # 晶格硅: 分形硅石
    1126: [(28, 0.5), (29, 0.5)],           # 卡西米尔: 钛晶石:光栅石 = 1:1
    1206: [(99, 1.0)],                      # 粒子容器: 电磁涡轮
    1114: [(16, 1.0)],                      # 精炼油: 原油精炼(副产氢)
    6001: [(9, 1.0)], 6002: [(18, 1.0)], 6003: [(27, 1.0)], 6004: [(55, 1.0)],
    6005: [(102, 1.0)], 1122: [(74, 1.0)], 6006: [(75, 1.0)],
}
# 其余中间产物：若配方唯一则自动采用（从 recs 里按产物找唯一）
from collections import defaultdict as _dd
RF = _dd(list)
for r in recs.values():
    for rid in r["Results"]:
        RF[rid].append(r)

# 特殊原料（不递归）：矿采 / 采集 / 射线接收
def is_raw(i):
    it = items.get(i, {})
    return (bool(it.get("MiningFrom")) or i == 1000 or i in (1116, 1120, 1208)) and i != 1117
# 有机晶体 1117 虽可采矿但也算（用户选矿采）；氢 1120 / 硫酸 1116 / 水 1000 / 临界光子 1208 为采集品

RES_RAW = {}   # 原料需求
RES_REC  = _dd(float)  # 配方运行次数
RAW_KEYS = {}
def count_raw(i):
    it = items.get(i, {})
    src = it.get("MiningFrom") or ("水（海洋）" if i == 1000 else
          ("硫酸（海洋）" if i == 1116 else ("氢（气巨/副产）" if i == 1120 else
          ("临界光子（射线接收）" if i == 1208 else ""))))
    RAW_KEYS[i] = src or "矿"

def solve(item_id, amount):
    """递归：目标产出 amount 个 item_id，累积原料与配方次数。"""
    if item_id in (1000, 1116, 1120, 1121, 1117, 1208):   # 水/硫酸/氢/重氢(采集或分馏)/有机晶体/临界光子
        RES_RAW[item_id] = RES_RAW.get(item_id, 0.0) + amount
        return
    it = items.get(item_id)
    if it and it.get("MiningFrom"):
        RES_RAW[item_id] = RES_RAW.get(item_id, 0.0) + amount
        return
    picks = PICK.get(item_id)
    if picks is None:
        rs = RF.get(item_id, [])
        picks = [(r["ID"], 1.0) for r in rs] if rs else None
    if not picks:
        RES_RAW[item_id] = RES_RAW.get(item_id, 0.0) + amount  # 未知来源（含临界光子等）
        return
    for rid, w in picks:
        r = recs[rid]
        idx = r["Results"].index(item_id)          # 目标产物在其结果列表中的位置
        num = amount * w / r["ResultCounts"][idx]
        RES_REC[rid] += num
        for i, c in zip(r["Items"], r["ItemCounts"]):
            solve(i, num * c)

# 目标：1 个宇宙矩阵；另算 1/min（=每 60s ，宇宙矩阵配方 900 tick = 15s）
ONE = 1.0
solve(6006, ONE)

# ---- 副产结算 ----
# 副产：精炼油R16 产氢；可燃冰R32 产氢；反物质R74 产氢
H2_BYPROD = 0.0
for rid, num in RES_REC.items():
    r = recs[rid]
    outs = list(zip(r["Results"], r["ResultCounts"]))
    if 1120 in r["Results"]:
        idx = r["Results"].index(1120)
        H2_BYPROD += num * r["ResultCounts"][idx]
H2_DEMAND = RES_RAW.get(1120, 0.0)   # 需求侧把氢当作原料收集了，先取回
RES_RAW.pop(1120, None)
h2_net = H2_DEMAND - H2_BYPROD       # >0 需采集

# ---- 重氢：需求来自奇异物质R104；先采集（参数），不足分馏；分馏 1:1 需氢 ----
DEU_DEMAND = RES_RAW.pop(1121, 0.0)
DEU_COLLECT = 0.0      # 参数：采集量（设 0 = 全部分馏；可改）
DEU_FRACT = max(0.0, DEU_DEMAND - DEU_COLLECT)

# 氢的补充需求 = 净缺口 + 分馏用氢(1:1)
h2_final = h2_net + DEU_FRACT

print("=" * 78)
print("目标：1 个宇宙矩阵（含各矩阵、反物质；引力矩阵/反物质配方产 2 个已折算）")
print("=" * 78)
print("\n【配方运行次数】(每 1 宇宙矩阵)")
for rid, num in sorted(RES_REC.items(), key=lambda x: -x[1]):
    r = recs[rid]
    print("  R%-4d x%-8.3f  %-10s <- %s -> %s" % (
        rid, num, r["Name"],
        " + ".join("%sx%d" % (iname(i), c) for i, c in zip(r["Items"], r["ItemCounts"])),
        " + ".join("%sx%d" % (iname(i), c) for i, c in zip(r["Results"], r["ResultCounts"]))))

print("\n【原料需求】(每 1 宇宙矩阵；负=副产)")
total = sum(v for k, v in RES_RAW.items() if v > 0)
for k in sorted(RES_RAW, key=lambda x: -RES_RAW[x]):
    print("  %-12s x%-10.3f" % (iname(k) + ("(%s)" % RAW_KEYS.get(k, "") if RAW_KEYS.get(k) else ""), RES_RAW[k]))
print("  氢        需求 %-8.3f 副产 %-8.3f 净缺口(采集) %.3f" % (H2_DEMAND, H2_BYPROD, h2_net))
print("  重氢      需求 %.3f（采集 %.3f + 分馏 %.3f，分馏另耗氢 %.3f）" % (DEU_DEMAND, DEU_COLLECT, DEU_FRACT, DEU_FRACT))
print("  氢最终需求(含分馏) = %.3f" % h2_final)
print("  临界光子 需求 = %.3f" % RES_RAW.get(1208, 0.0))

# 归一化比例（以铁矿为 1）
print("\n【归一化比例】（每 1/min 宇宙矩阵；×60/900=0.0667）")
per_min = 60.0 / 900.0
iron = RES_RAW.get(1001, 0.0) or 1
ordered = sorted(RES_RAW, key=lambda x: -RES_RAW[x])
for k in ordered:
    if RES_RAW[k] <= 0:
        continue
    print("  %-12s %10.3f /min   (铁=%.3f)" % (iname(k), RES_RAW[k] * per_min, RES_RAW[k] / iron))
print("  氢(净采集) %10.3f /min" % (h2_final * per_min))
print("  重氢        净需求 %10.3f /min" % (DEU_DEMAND * per_min))
print("  临界光子    %10.3f /min" % (RES_RAW.get(1208, 0.0) * per_min))
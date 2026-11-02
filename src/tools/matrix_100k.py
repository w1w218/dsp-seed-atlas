# -*- coding: utf-8 -*-
"""按“每分钟 10 万宇宙矩阵”基准输出：原料需求 / 配方每分钟次数（增产方案：稀缺权重）。"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import proliferator_lp as m
import pulp
import io

K = 100000.0         # 目标：每分钟 100,000 个宇宙矩阵（原 25000 误作产线数；系数应乘产量/min）
PER_MIN = 100000.0
W = {1006: 1.0, 1012: 1.0, 1015: 1.5, 1117: 2.0, 1014: 3.0}

prob, v, p, M, spray, deu_f, deu_c, h2c, obj = m.build_lp(False, None, W)
prob.solve(pulp.PULP_CBC_CMD(msg=0))

BASE = m.BASE_RES
lines = []
lines.append("# 宇宙矩阵 10万/分钟 基准表（增产剂稀缺权重方案）")
lines.append("")
lines.append("- 换算系数：目标产量 %d 个/min（每 1 宇宙矩阵需求量 × %d = 每分钟需求）" % (PER_MIN, int(K)))
lines.append("- 配方/原料为**每分钟**需求；原料单位=个/分钟，配方=次数/分钟")
lines.append("- 路径选择与 matrix-balance/matrix-proliferator 一致；铁/铜/石/钛不限制（仍列出参考）")
lines.append("- 生成：dsp\\tools\\matrix_100k.py")
lines.append("")

# 原料需求表
lines.append("## 原料需求（每分钟）")
lines.append("")
lines.append("| 资源 | 每1宇宙矩阵 | ×25000/分钟 | 每 小时 | 备注 |")
lines.append("|---|---|---|---|---|")
items_map = {1006:"煤矿", 1015:"刺笋", 1012:"金伯利", 1117:"有机晶体", 1014:"光栅石",
             1003:"硅石", 1011:"可燃冰", 1000:"水", 1007:"原油", 1013:"分形硅石",
             1001:"铁矿", 1002:"铜矿", 1004:"钛石", 1005:"石矿", 1208:"临界光子",
             1120:"氢(采集)", 1121:"重氢(需求)"}
def usage(j):
    s = 0.0
    for rid, r in m.locked.items():
        if j in r["Items"]:
            s += r["ItemCounts"][r["Items"].index(j)] * (pulp.value(v[rid]) or 0)
    if j == 1120:
        s = pulp.value(h2c) or 0
    if j == 1121:
        s = (pulp.value(deu_f) or 0) + (pulp.value(deu_c) or 0)
    return s
base_items = sorted(BASE, key=lambda x: -BASE[x])
for j in base_items:
    if BASE[j] <= 0:
        continue
    u = usage(j)
    note = "不限制(丰富)" if j in (1001, 1002, 1004, 1005) else ""
    if j == 1120:
        note = "净采集（副产已扣）"
    if j == 1121:
        note = "气巨采集（分馏耗氢另计）"
    if j == 1208:
        note = "射线接收"
    lines.append("| %s | %.3f | **%.0f** | %.0f | %s |" % (
        items_map.get(j, m.iname(j)), u, u * K, u * K * 60, note))
lines.append("")

# 配方次数（每分钟）
lines.append("## 配方运行次数（每分钟；增产方案实际运行 v，喷涂 p）")
lines.append("")
lines.append("| 配方 | 类型 | 每分钟次数 | 其中喷涂 | 单次喷数 | 每秒? |")
lines.append("|---|---|---|---|---|---|")
for rid in sorted(m.locked, key=lambda r: -(pulp.value(v[r]) or 0)):
    r = m.locked[rid]
    vv = pulp.value(v[rid]) or 0
    pv = pulp.value(p[rid]) or 0
    t = {1: "熔炉", 2: "化工", 3: "精炼", 4: "制造台", 5: "对撞机", 8: "分馏", 15: "科研"}.get(r["Type"], str(r["Type"]))
    lines.append("| R%d %s | %s | **%.0f** | %.0f (%.0f%%) | %d | %ds |" % (
        rid, r["Name"], t, vv * K, pv * K, 100 * pv / vv if vv else 0,
        sum(r["ItemCounts"]), r["TimeSpend"]))
lines.append("")
lines.append("## 增产剂与设施概要")
lines.append("")
lines.append("- 3级增产剂（Mk.III）需求：每 1 宇宙矩阵 %.3f → **每分钟 %.0f 个**" % (pulp.value(M), (pulp.value(M) or 0) * K))
lines.append("- 喷涂总次数：每分钟 %.0f 次" % ((pulp.value(spray) or 0) * K))
lines.append("- 设备数估算（单设施产能 × 增产系数）：科研站 10万/min 需 25,000 条宇宙矩阵配方线（900tick=15s/次，1.25×），即 **20,000 个科研站等效**（未计 Mk.II 与归一）")

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "matrix-100k.md")
io.open(out, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
print("written", out)
print("\n".join(lines[:8]))
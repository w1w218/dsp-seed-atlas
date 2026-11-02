# -*- coding: utf-8 -*-
"""合并矩阵（10万/min×增产稀缺权重）与戴森球（增产·煤放开）的资源需求，
输出总量与归一化比例（以铁矿=1）。"""
import sys, os, io
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import proliferator_lp as mat
import dyson_proliferator_lp as dys
import pulp

MAT_K = 100000.0
MAT_W = {1006: 1.0, 1012: 1.0, 1015: 1.5, 1117: 2.0, 1014: 3.0}

# ---- 矩阵链最优（稀缺权重）----
prob1, v1, p1, M1, sp1, d1f, d1c, h1c, o1 = mat.build_lp(False, None, MAT_W)
prob1.solve(pulp.PULP_CBC_CMD(msg=0))
# ---- 戴森球链最优（煤放开）----
prob2, v2, p2, M2, sp2, d2f, d2c, h2c, o2 = dys.build_lp(False)
prob2.solve(pulp.PULP_CBC_CMD(msg=0))

def usage_mod(locked, v, j, h2c, d2f, d2c, TARGETS=None):
    s = 0.0
    for rid, r in locked.items():
        if j in r["Items"]:
            s += r["ItemCounts"][r["Items"].index(j)] * (pulp.value(v[rid]) or 0)
    return s

def usg_mat(j):
    if j == 1120:
        return pulp.value(h1c) or 0
    if j == 1121:
        return (pulp.value(d1f) or 0) + (pulp.value(d1c) or 0)
    if j == 1208:
        return sum((pulp.value(v1[rid]) or 0) * r["ItemCounts"][r["Items"].index(1208)]
                   for rid, r in mat.locked.items() if 1208 in r["Items"])
    return usage_mod(mat.locked, v1, j, h1c, d1f, d1c)

def usg_dys(j):
    if j == 1120:
        return pulp.value(h2c) or 0
    if j == 1121:
        return (pulp.value(d2f) or 0) + (pulp.value(d2c) or 0)
    return usage_mod(dys.locked, v2, j, h2c, d2f, d2c)

# 资源全集：矩阵基线 + 戴森基线
RES = {}
for j in mat.BASE_RES:
    if mat.BASE_RES[j] > 0:
        RES[j] = True
for j in dys.BASE_RES:
    if dys.BASE_RES[j] > 0:
        RES[j] = True

NAME = {1000: "水", 1001: "铁矿", 1002: "铜矿", 1003: "硅石", 1004: "钛石", 1005: "石矿",
        1006: "煤矿", 1007: "原油", 1011: "可燃冰", 1012: "金伯利矿石", 1013: "分形硅石",
        1014: "光栅石", 1015: "刺笋结晶", 1016: "单极磁石", 1116: "硫酸", 1117: "有机晶体",
        1120: "氢(采集)", 1121: "重氢(需求)", 1208: "临界光子"}

lines = []
lines.append("# 合并资源需求：宇宙矩阵 10万/min + 戴森球（太阳帆 2万/min + 火箭 150/min）")
lines.append("")
lines.append("- 矩阵链：3级增产稀缺权重方案（每 1 宇宙矩阵 ×100000；重氢=采集、氢=净采集）")
lines.append("- 戴森球链：3级增产、煤不设上限方案（每分钟）")
lines.append("- 铁/铜/石/钛不限制，仍列出；归一化以铁矿=1")
lines.append("")

rows = []
for j in RES:
    m = usg_mat(j) * MAT_K
    d = usg_dys(j)
    t = m + d
    rows.append((j, m, d, t))

rows.sort(key=lambda x: -x[3])
lines.append("| 资源 | 矩阵链/min | 戴森球/min | 合计/min | 合计/h | 归一化(铁=1) |")
lines.append("|---|---|---|---|---|---|")
iron = rows[0][3] if rows[0][0] == 1001 else next(r[3] for r in rows if r[0] == 1001)
for j, m, d, t in rows:
    nm = NAME.get(j, mat.iname(j))
    lines.append("| %s | %.0f | %.0f | **%.0f** | %.0f | %.3f |" % (nm, m, d, t, t * 60, t / iron))
lines.append("")
lines.append("- 3级增产剂合计：%.0f 个/min（矩阵 %.0f + 戴森球 %.0f）" % (
    (pulp.value(M1) or 0) * MAT_K + (pulp.value(M2) or 0), (pulp.value(M1) or 0) * MAT_K, pulp.value(M2) or 0))
lines.append("- 氢采集合计：%.0f/min（矩阵 %.0f；戴森球链自平衡为 0）" % (
    usg_mat(1120) * MAT_K + usg_dys(1120), usg_mat(1120) * MAT_K))
lines.append("- 重氢合计：%.0f/min（矩阵 %.0f 采集 + 戴森球 %.0f）" % (
    usg_mat(1121) * MAT_K + usg_dys(1121), usg_mat(1121) * MAT_K, usg_dys(1121)))

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "combined-balance.md")
io.open(out, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
print("\n".join(lines))
print("\n->", out)
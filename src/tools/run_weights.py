# -*- coding: utf-8 -*-
"""珍贵度权重对比：等权 vs 稀缺权重（煤=1 金伯利=1 刺笋=1.5 有机晶体=2 光栅石=3）。
输出两组方案的喷涂决策差异与关键资源用量。"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import proliferator_lp as m
import pulp

SCARCE_W = {1006: 1.0, 1012: 1.0, 1015: 1.5, 1117: 2.0, 1014: 3.0}   # 稀缺权重（其余默认 1）

WATCH = {1006: "煤矿", 1015: "刺笋", 1012: "金伯利", 1117: "有机晶体",
         1014: "光栅石", 1003: "硅石", 1011: "可燃冰", 1000: "水"}

def usage(res, j):
    s = 0.0
    for rid, r in m.locked.items():
        if j in r["Items"]:
            s += r["ItemCounts"][r["Items"].index(j)] * (pulp.value(res["v"][rid]) or 0)
    if j == 1120:
        s = pulp.value(res["h2c"]) or 0
    if j == 1121:
        s = (pulp.value(res["deu_f"]) or 0) + (pulp.value(res["deu_c"]) or 0)
    return s

def run(tag, weights):
    prob, v, p, M, spray, deu_f, deu_c, h2c, obj = m.build_lp(False, None, weights)
    st = prob.solve(pulp.PULP_CBC_CMD(msg=0))
    res = dict(v=v, p=p, M=M, spray=spray, deu_f=deu_f, deu_c=deu_c, h2c=h2c, obj=obj)
    print("==== %s ====" % tag)
    print("状态: %s  目标值(按该权重口径): %.3f  Mk.III=%.3f  喷涂次数=%.1f" % (
        pulp.LpStatus[st], pulp.value(obj), pulp.value(M), pulp.value(spray)))
    print("资源: " + " | ".join("%s=%.3f" % (nm, usage(res, j)) for j, nm in WATCH.items()))
    # 喷涂决策
    on = sorted([rid for rid in m.locked if (pulp.value(p[rid]) or 0) > 0.001],
                key=lambda r: -(pulp.value(p[r]) or 0))
    off = [rid for rid in m.locked if m.can_spray(m.locked[rid]) and (pulp.value(p[rid]) or 0) <= 0.001
           and (pulp.value(v[rid]) or 0) > 0.01]
    sp_n = {r: m.locked[r]["Name"][:10] for r in (26, 29, 28, 30, 35, 33, 32, 99)}
    print("喷涂关键位: " + " | ".join("R%d(%s)=%s" % (r, sp_n.get(r, m.locked[r]["Name"][:6]),
          "喷" if (pulp.value(p[r]) or 0) > 0.001 else "不喷") for r in (26, 29, 28, 30, 35, 33)))
    print("喷涂配方 %d 条 / 未喷 %d 条" % (len(on), len(off)))
    return res

print("稀缺权重: 煤=1 金伯利=1 刺笋=1.5 有机晶体=2 光栅石=3（其余=1）\n")
w0 = run("W0 等权（原最优）", None)
w1 = run("W1 稀缺权重", SCARCE_W)

print("\n-- W1 vs W0 差异 --")
diffs = []
for j in WATCH:
    d = usage(w1, j) - usage(w0, j)
    if abs(d) > 0.001:
        diffs.append("%s %+.3f" % (WATCH[j], d))
print("资源变化: " + " | ".join(diffs) if diffs else "无显著变化")
print("Mk.III: %+.3f" % (pulp.value(w1["M"]) - pulp.value(w0["M"])))
# 按 W1 口径比较目标
o0 = pulp.value(w0["obj"])  # 等权口径的目标
o1 = pulp.value(w1["obj"])  # 稀缺口径的目标
print("等权口径目标: W0=%.3f  W1=%.3f (W0 更优)" % (o0, o1))
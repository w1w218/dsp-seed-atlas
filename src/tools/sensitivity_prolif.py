# -*- coding: utf-8 -*-
"""敏感性分析：钛化玻璃(R30)/钛晶石(R26)/卡西米尔光栅版(R29) 喷 vs 不喷 的资源影响对比。
变体：S0=LP最优(自由) / S1=R30不喷 / S2=R26强制喷 / S3=R29强制喷 /
      S4=R30不喷+R26喷 / S5=R30不喷+R26喷+R29喷
输出：目标值、增产剂用量、关键资源（煤/刺笋/金伯利/有机晶体/光栅石/硅石/可燃冰/水）。"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import proliferator_lp as m
import pulp

WATCH = {1006: "煤矿", 1015: "刺笋结晶", 1012: "金伯利矿石", 1117: "有机晶体",
         1014: "光栅石", 1003: "硅石", 1011: "可燃冰", 1000: "水"}

VARIANTS = [
    ("S0  LP最优(自由)", None),
    ("S1  钛化玻璃不喷", {30: False}),
    ("S2  钛晶石强制喷", {26: True}),
    ("S3  卡西米尔光栅版强制喷", {29: True}),
    ("S4  钛化玻璃不喷+钛晶石喷", {30: False, 26: True}),
    ("S5  钛化玻璃不喷+钛晶石喷+光栅版喷", {30: False, 26: True, 29: True}),
    ("S6  三者全不喷(30/26/29)", {30: False, 26: False, 29: False}),
]

def usage_of(res, j):
    s = 0.0
    for rid, r in m.locked.items():
        if j in r["Items"]:
            s += r["ItemCounts"][r["Items"].index(j)] * (pulp.value(res["v"][rid]) or 0)
    if j == 1120:
        s = pulp.value(res["h2c"]) or 0
    if j == 1121:
        s = (pulp.value(res["deu_f"]) or 0) + (pulp.value(res["deu_c"]) or 0)
    return s

def show(tag, force):
    prob, v, p, M, spray, deu_f, deu_c, h2c, obj = m.build_lp(False, force)
    st = prob.solve(pulp.PULP_CBC_CMD(msg=0))
    ok = pulp.LpStatus[st] == "Optimal"
    res = dict(v=v, p=p, M=M, spray=spray, deu_f=deu_f, deu_c=deu_c, h2c=h2c, obj=obj)
    if not ok:
        print("%-38s  %s" % (tag, pulp.LpStatus[st]))
        return None
    parts = ["%-38s obj=%7.3f  MkIII=%.3f" % (tag, pulp.value(obj), pulp.value(M))]
    for j, nm in WATCH.items():
        parts.append("%s=%6.3f" % (nm, usage_of(res, j)))
    print("  ".join(parts))
    return res

print("基线：", ", ".join("%s=%.2f" % (nm, m.BASE_RES[j]) for j, nm in WATCH.items()))
print("=" * 150)
results = {}
for tag, force in VARIANTS:
    results[tag] = show(tag, force)

if results.get("S0  LP最优(自由)"):
    s0 = results["S0  LP最优(自由)"]
    print("\n-- 与 S0 的差异 --")
    for tag, res in results.items():
        if tag.startswith("S0"):
            continue
        if not res:
            continue
        diffs = []
        for j, nm in WATCH.items():
            d = usage_of(res, j) - usage_of(s0, j)
            if abs(d) > 0.001:
                diffs.append("%s%+.3f" % (nm, d))
        print("%-38s obj%+8.3f  MkIII%+7.3f  %s" % (
            tag, pulp.value(res["obj"]) - pulp.value(s0["obj"]),
            pulp.value(res["M"]) - pulp.value(s0["M"]), " | ".join(diffs)))
    # 喷涂状态也对比
    print("\n-- 关键配方喷涂状态 (R30钛化玻璃/R26钛晶石/R29卡西米尔光栅版/R28卡西米尔钛晶版) --")
    for tag, res in results.items():
        if not res:
            continue
        st_ = []
        for rid in (30, 26, 29, 28):
            vv = pulp.value(res["v"][rid]) or 0
            if vv <= 0.001:
                st_.append("R%d=—" % rid)
            else:
                ff = (pulp.value(res["p"][rid]) or 0) / vv
                st_.append("R%d=%s" % (rid, "喷" if ff > 0.99 else ("不喷" if ff < 0.01 else "%.0f%%" % (ff * 100))))
        print("%-38s %s" % (tag, "  ".join(st_)))
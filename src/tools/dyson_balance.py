# -*- coding: utf-8 -*-
"""戴森球建设配比：太阳帆 20,000/min + 小型运载火箭 150/min（合计太阳帆需求 20,900/min）。

路径锁定（沿用既有选择 + 新链默认）：
  光子合并器=R68 棱镜路线（省光栅石）| 石墨烯=R32 可燃冰 | 碳纳米管 R35:R33=1:1
  卡西米尔 R28:R29=1:1 | 处理器/量子芯片/位面过滤器=矩阵链同款 | 钛化玻璃=R30
  重氢：先采集余分馏(1:1) | 氢：副产优先余采集
输出：原料需求/分钟 + 配方次数/分钟 + 氢/重氢结算。"""
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

# 锁定路径：(item -> [(rid, w)])
PICK = {
    # 戴森球链
    1501: [(70, 1.0)],    # 太阳帆
    1502: [(81, 1.0)],    # 戴森球组件
    1503: [(83, 1.0)],    # 小型运载火箭
    1125: [(80, 1.0)],    # 框架材料
    1802: [(41, 1.0)],    # 氘核燃料棒
    1404: [(68, 1.0)],    # 光子合并器: 棱镜路线（省光栅石）[备选 R69 光栅石x1]
    1111: [(11, 1.0)],    # 棱镜
    1107: [(66, 1.0)],    # 钛合金
    1205: [(103, 1.0)],   # 超级磁场环
    1103: [(63, 1.0)],    # 钢材
    # 复用矩阵链选择
    1123: [(32, 1.0)],    # 石墨烯: 可燃冰
    1124: [(35, 0.5), (33, 0.5)],  # 碳纳米管 1:1
    1126: [(28, 0.5), (29, 0.5)],  # 卡西米尔 1:1
    1118: [(26, 1.0)], 1206: [(99, 1.0)], 1114: [(16, 1.0)],
    1109: [(17, 1.0)], 1112: [(61, 1.0)], 1113: [(62, 1.0)],
}
# 唯一配方自动补全
RF = defaultdict(list)
for r in recs.values():
    for rid in r["Results"]:
        RF[rid].append(r)

COLLECT = {1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1011, 1012, 1013, 1014, 1015,
           1016, 1116, 1117, 1120, 1121, 1208}

ALL = dict(PICK)
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
            ALL[item] = [(rs[0]["ID"], 1.0)]
            changed = True
        elif len(rs) == 0:
            COLLECT.add(item)
            changed = True
        else:
            raise RuntimeError("多配方未锁定: %s id=%d %s" % (iname(item), item, [r["ID"] for r in rs]))
    if not changed:
        raise RuntimeError("未锁定: %s" % [iname(i) for i in needs if i not in ALL and i not in COLLECT])

# 目标：太阳帆 20000 + 150 火箭隐含 900 帆 → 20900；火箭 150
TARGET = {1501: 20900.0, 1503: 150.0}

def solve(item_id, amount, RES):
    if item_id in COLLECT:
        RES[item_id] += amount
        return
    for rid, w in ALL.get(item_id, []):
        r = recs[rid]
        num = amount * w / r["ResultCounts"][r["Results"].index(item_id)]
        RES_REC[rid] += num
        for i, c in zip(r["Items"], r["ItemCounts"]):
            solve(i, num * c, RES)

RES = defaultdict(float)
RES_REC = defaultdict(float)
for iid, amt in TARGET.items():
    solve(iid, amt, RES)

# 氢副产
H2_BP = 0.0
for rid, num in RES_REC.items():
    r = recs[rid]
    if 1120 in r["Results"]:
        H2_BP += num * r["ResultCounts"][r["Results"].index(1120)]
H2_DEM = RES.get(1120, 0.0)
if 1120 in RES:
    del RES[1120]
h2_net = H2_DEM - H2_BP

DEU = RES.pop(1121, 0.0)
deu_f = 0.0   # 采集优先（参数）
deu_f = DEU   # 假设全采集；可改分馏（分馏耗氢 = 分馏量）

print("=" * 76)
print("戴森球建设配比：太阳帆 %d/min + 小型运载火箭 %d/min（含组件内部太阳帆）" % (int(TARGET[1501]), int(TARGET[1503])))
print("=" * 76)
print("\n【配方次数/分钟】(按实际运行)")
for rid, num in sorted(RES_REC.items(), key=lambda x: -x[1]):
    r = recs[rid]
    print("  R%-4d x%-10.2f %-14s <- %s" % (rid, num, r["Name"][:12],
        " + ".join("%sx%d" % (iname(i), c) for i, c in zip(r["Items"], r["ItemCounts"]))))

print("\n【原料需求/分钟】")
for k in sorted(RES, key=lambda x: -RES[x]):
    print("  %-12s %10.1f" % (iname(k), RES[k]))
print("  氢          需求 %8.1f  副产 %8.1f  净(采集) %8.1f" % (H2_DEM, H2_BP, h2_net))
print("  重氢        需求 %8.1f（采集优先；全分馏则氢 +%.1f）" % (DEU, DEU))

print("\n【每小时】")
for k in sorted(RES, key=lambda x: -RES[x]):
    print("  %-12s %10.0f" % (iname(k), RES[k] * 60))
print("  氢(净采集) %.0f/h   重氢 %.0f/h" % (h2_net * 60, DEU * 60))
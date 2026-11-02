# -*- coding: utf-8 -*-
"""由 6 张 PrototypeSet JSON 生成摘要 CSV（供人工查阅/地图搜索器导入）。"""
import io, os, json, csv

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.normpath(os.path.join(HERE, "..", "data"))
J = os.path.join(BASE, "json")
OUT = BASE

def load(name):
    with io.open(os.path.join(J, name + ".json"), encoding="utf-8") as f:
        return json.load(f)["rows"]

def write_csv(name, header, rows):
    with io.open(os.path.join(OUT, name), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        for r in rows:
            w.writerow([r.get(h, "") for h in header])
    print("  %-14s %3d 条" % (name, len(rows)))

themes = load("ThemeProtoSet")
veins  = load("VeinProtoSet")
items  = load("ItemProtoSet")
recs   = load("RecipeProtoSet")
techs  = load("TechProtoSet")
veges  = load("VegeProtoSet")

# 主题速查（VeinSpot 顺序: 铁铜硅钛石煤油; RareVeins: 稀有矿索引 8-14）
write_csv("themes.csv",
    ["id", "Name", "DisplayName", "PlanetType", "Temperature", "Distribute", "WaterItemId",
     "Iron", "Copper", "Silicium", "Titanium", "Stone", "Coal", "Oil",
     "RareVeins", "VeinOpacity", "Algos", "ModX", "ModY", "Wind", "IonHeight"],
    [{"id": t["ID"], "Name": t["Name"], "DisplayName": t["DisplayName"],
      "PlanetType": t["PlanetType"], "Temperature": t["Temperature"], "Distribute": t["Distribute"],
      "WaterItemId": t["WaterItemId"]} | (lambda vs: dict(zip(["Iron","Copper","Silicium","Titanium","Stone","Coal","Oil"], (vs + [0]*7)[:7])))(t["VeinSpot"])
      | {"RareVeins": "|".join(map(str, t["RareVeins"])), "VeinOpacity": "|".join("%g" % v for v in t["VeinOpacity"]),
         "Algos": "|".join(map(str, t["Algos"])), "ModX": "%g~%g" % tuple(t["ModX"]), "ModY": "%g~%g" % tuple(t["ModY"]),
         "Wind": t["Wind"], "IonHeight": t["IonHeight"]} for t in themes])

# 矿脉
write_csv("veins.csv",
    ["id", "Name", "MiningItem", "MiningTime", "ModelIndex", "CircleRadius"],
    [{"id": v["ID"], "Name": v["Name"], "MiningItem": v["MiningItem"], "MiningTime": v["MiningTime"],
      "ModelIndex": v["ModelIndex"], "CircleRadius": v["CircleRadius"]} for v in veins])

# 物品
ET = {0:"None",1:"Mining",2:"Smelt",3:"Chemical",4:"Assemble",5:"Ingredient",6:"Bar",7:"Component",
      8:"Logistic",9:"Material",10:"Product",11:"Append",12:"Gun",13:"Ammo",14:"Turret",15:"Tool",
      16:"Terraform",17:"Reactor",18:"Algorithm",19:"Weapon",20:"Bomb",21:"Drone",22:"Ship"}
write_csv("items.csv",
    ["id", "Name", "Type", "StackSize", "MiningFrom", "ProduceFrom", "CanBuild", "IsFluid",
     "HeatValue", "FuelType", "GridIndex", "BuildIndex", "Description"],
    [{"id": it["ID"], "Name": it["Name"], "Type": "%d:%s" % (it["Type"], ET.get(it["Type"], "?")),
      "StackSize": it["StackSize"], "MiningFrom": it["MiningFrom"], "ProduceFrom": it["ProduceFrom"],
      "CanBuild": it["CanBuild"], "IsFluid": it["IsFluid"], "HeatValue": it["HeatValue"],
      "FuelType": it["FuelType"], "GridIndex": it["GridIndex"], "BuildIndex": it["BuildIndex"],
      "Description": it["Description"][:40]} for it in items])

# 配方
RT = {1:"Smelt冶炼",2:"Chemical化工",3:"Refine精炼",4:"Assemble组装",5:"Particle粒子",8:"Fractionate分馏",15:"Research科研"}
def fmt_list(ids, cnts):
    return "+".join("%d:%d" % (i, c) for i, c in zip(ids, cnts))
write_csv("recipes.csv",
    ["id", "Name", "Type", "TimeSpend", "Handcraft", "Items", "Results", "NonProductive"],
    [{"id": r["ID"], "Name": r["Name"], "Type": "%d:%s" % (r["Type"], RT.get(r["Type"], "?")),
      "TimeSpend": r["TimeSpend"], "Handcraft": r["Handcraft"],
      "Items": fmt_list(r["Items"], r["ItemCounts"]), "Results": fmt_list(r["Results"], r["ResultCounts"]),
      "NonProductive": r["NonProductive"]} for r in recs])

# 科技
write_csv("techs.csv",
    ["id", "Name", "Level", "MaxLevel", "PreTechs", "Items", "ItemPoints",
     "HashNeeded", "UnlockRecipes", "Position", "Desc"],
    [{"id": t["ID"], "Name": t["Name"], "Level": t["Level"], "MaxLevel": t["MaxLevel"],
      "PreTechs": "|".join(map(str, t["PreTechs"])),
      "Items": fmt_list(t["Items"], t["ItemPoints"]),
      "ItemPoints": "|".join(map(str, t["ItemPoints"])),
      "HashNeeded": t["HashNeeded"],
      "UnlockRecipes": "|".join(map(str, t["UnlockRecipes"])),
      "Position": "%g,%g" % tuple(t["Position"]),
      "Desc": t["Desc"][:40]} for t in techs])

# 植被
write_csv("veges.csv",
    ["id", "Name", "Type", "Versatility", "CircleRadius", "MiningItem", "MiningCount", "MiningChance", "MiningTime", "HpMax"],
    [{"id": v["ID"], "Name": v["Name"], "Type": v["Type"], "Versatility": v["Versatility"],
      "CircleRadius": v["CircleRadius"], "MiningItem": "|".join(map(str, v["MiningItem"])),
      "MiningCount": "|".join(map(str, v["MiningCount"])),
      "MiningChance": "|".join("%g" % x for x in v["MiningChance"]),
      "MiningTime": v["MiningTime"], "HpMax": v["HpMax"]} for v in veges])

print("done ->", OUT)
# DSP 游戏数据手册（src\data\）

从游戏 `DSPGAME_Data\resources.assets`（Unity 2022.3 序列化资产，427MB）直接解码的现行版本原型数据。
提取脚本：`decode_protos.py`（UnityPy 读原始字节 + 按字段布局解析二进制，**未随仓库发布**，本地参考）；
`src\tools\build_summary.py`（生成 CSV 摘要，随仓库发布）。游戏版本 2026-10 现行（含黑雾 DLC 内容）。

## 数据文件

- **json\ThemeProtoSet.json / VeinProtoSet.json / ItemProtoSet.json / RecipeProtoSet.json / TechProtoSet.json / VegeProtoSet.json** — 完整原始字段（解码验证：全部 `coverage` 完整、`remaining=0`）
- **themes.csv**（25 主题）、**veins.csv**（14 矿脉）、**items.csv**（175 物品）、**recipes.csv**（162 配方）、**techs.csv**（315 科技）、**veges.csv**（286 植被）

对应 asset path_id（resources.assets 内）：Theme 136398 / Vein 136402 / Item 136388 / Recipe 136395 / Tech 136397 / Vege 136400。

## 表结构与关键字段

### themes.csv（星球主题 = 行星"皮肤"，决定资源/植被/地表）

- 列 `Iron..Oil` = VeinSpot 前 7 项：**每颗星这些普通矿的"组数"**（铁/铜/硅/钛/石/煤/油；0 = 该主题无此矿）
- `RareVeins` = 稀有矿索引（EVeinType 值 **8-14**，对应 veins.csv 的 id：8 可燃冰 / 9 金伯利 / 10 分形硅 / 11 有机晶体 / 12 光栅石 / 13 刺笋 / 14 单极磁）；出现概率由 `RareSettings`（json）与恒星类型系数共同决定（见 mechanics.md §6）
- `Temperature` = 主题温度（生成时匹配行星 `temperatureBias`：`theme.Temperature*temperatureBias >= -0.1` 才候选）；`Distribute`：0 Default / 1 Birth(出生星才有) / 2 Interstellar / 3 Rare
- `WaterItemId`：0 无水 / 1000 水 / 1116 硫酸海（Volcanic 1）/ 4294967294(-2 全陆) / 4294967295(-1 全水? Lava 1)
- `PlanetType`：0 None /1 Vocano火山 /2 Ocean海洋 /3 Desert荒漠 /4 Ice冰原 /5 Gas气巨
- `Algos` = 地形算法编号候选；`ModX/ModY` = 地形扰动范围（每颗行星在该范围内随机取 mod 值）

### veins.csv（矿脉原型）

- `MiningItem` = 对应物品 id（1001 铁矿…1016 单极磁石、1117 有机晶体）；`MiningTime` = 每单位采集 ticks（60=1s）

### items.csv（物品）

- `Type` 含枚举名（Mining/Smelt/Ingredient/Component/…）；`StackSize` 堆叠；`HeatValue` 燃料热值；`MiningFrom`/`ProduceFrom` 来源说明（中文）

### recipes.csv（合成配方 —— "产物合成规则"核心）

- `Type`：1 Smelt熔炉 / 2 Chemical化工厂 / 3 Refine精炼 / 4 Assemble制造台 / 5 Particle粒子对撞 / 8 Fractionate分馏塔 / 15 Research科研站
- `TimeSpend` = ticks（60/s）；`Handcraft` = 可否手搓；`Items` 格式 `物品id:数量+...`，`Results` 同理
- 例：铁块 = 铁矿1→铁块1 / 60tick / 手搓 ✓；电磁矩阵 = 磁线圈1+电路板1→电磁矩阵1 / 180tick

### techs.csv（科技树）

- `PreTechs` 前置科技；`Items:ItemPoints` 研究材料（3600 点/物品，见 TechProto.kPointPerItem）；`UnlockRecipes` 解锁配方；`Level/MaxLevel` 科技等级（可升级科技的 MaxLevel>1）；`Position` 科技树坐标
- 注：研究矩阵配方在 recipes.csv（Type=15）

### veges.csv（植被）

- `MiningItem/MiningCount/MiningChance` = 砍伐产出（多物品概率）；`Type` = 植被类别（树/矿点外观）；`Versatility` = 适配多个主题的通用植被集

## 主题 ↔ 资源速查（地图搜索器核心）

> **主题名说明（2026-10-19）**：`ThemeProtoSet.json` 的 `DisplayName` 字段在游戏资源中实为**本地化 key**
> （开发内部用中文 key），运行时 `Translate()` 显示真名。已按游戏实测显示名修正 7 个主题
> （红土→黑石盐滩、白色海洋→樱林海、三色→飓风石林、红冰→猩红冰湖、草原→热带草原、
> 极寒→极寒冻土、潘多拉→潘多拉沼泽）；重新 decode 时需重做该映射。


| 主题 | 类型 | 普通矿(铁铜硅钛石煤油) | 稀有矿 |
|---|---|---|---|
| Ocean 1 地中海 (Birth) | 海洋 | 7/5/0/0/8/11/18 | 有机晶体 |
| Ocean 2~6 | 海洋 | 各异（Ocean 5 极匮乏：无铁铜硅钛石） | 有机晶体/金伯利/刺笋等 |
| Desert 1~11 | 荒漠 | 各异 | 可燃冰/金伯利/分形硅/光栅石等 |
| Lava 1 / Volcanic 1 | 火山 | 铁铜量大（Lava 15/15） | Lava: 金伯利/分形硅/光栅石；Volcanic1: 无 |
| Ice 1 | 冰原 | 钛多(10) | 可燃冰/分形硅/光栅石 |
| Gas 1~5 | 气巨 | 无 | 气态采集 GasItems（氢/氘/可燃冰） |

完整清单见 themes.csv；`RareSettings`（json）含出生星/非出生星概率与级联参数。

## 数据来源与可信度

- 解码依据：游戏内 `LDB.LoadTable<T>` 用 `Resources.Load("Prototypes/xxxProtoSet")` → 资产为 ScriptableObject（MonoBehaviour 序列化）。Unity 序列化规则：string=int32 字节长+UTF-8+4B 对齐；int/float/enum/bool=4B；long=8B；数组=int32 count+元素。
- 字段顺序来自 `ref\decomp-full\` 反编译（本地参考，未随仓库发布；Proto 基类：Name/ID/SID → 各 Proto 派生字段；[NonSerialized] 字段不序列化）。
- 与 DspFindSeed（旧版 0.8.21）的 XML 导出对照：Theme 25/Vein 14 数量一致；**当前版本新增字段**（如 VeinProto.IconTag/iconTagString、ItemProto 战斗字段）；Item 122→175（黑雾新增战斗物品）。
- 离线可复算：用解码脚本（未随仓库发布，需 UnityPy + 游戏资源）；本仓库已提供解码结果。

## 关键对照（mechanics.md §9 数据缺口 → 已解决）

- ✅ 主题/矿脉/物品/配方/科技/植被数值：本目录
- ✅ 行星生成算法复刻（DotNet35Random + UniverseGen→PlanetGen + 地形/矿脉）：`src\`（P0-P2 完成，存档对拍 194/198）
- ✅ 气巨行星产物（GasItems/GasSpeeds）：在 themes.json（Gas 1~5），`--gas` 模式可导出全部气巨气体速率
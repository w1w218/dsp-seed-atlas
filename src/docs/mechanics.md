# 戴森球计划（DSP）内部机制逆向笔记

来源：`ref\ref\decomp-full\`（ilspycmd 全量反编译，Assembly-CSharp.dll 8MB/2277 类）。
游戏版本：Unity 2022.3.62f3c1、Mono 托管，Assembly SHA-256 `ef3144...b1d8c`，MVID `4da04a9a-...-c1a2256c5347`。
2026-10-18 整理（机制公式为 10-16 摸底，进度已随 P0-P2 完成更新）。核心类：`UniverseGen/StarGen/PlanetGen/PlanetAlgorithm/LDB/GameDesc`。

## 0. 数据流总览（种子 → 银河 → 恒星 → 行星 → 资源）

```
GameDesc(galaxySeed, starCount, galaxyAlgo, isRareResource, isInfiniteResource, resourceMultiplier, ...)
   │
   ├─ UniverseGen.CreateGalaxy(gameDesc)          ← 银河
   │    DotNet35Random(galaxySeed)                ← .NET35 System.Random 算法，可 1:1 复刻
   │    RandomPoses（随机游走撒恒星位置）
   │    恒星配额：白矮/中子/黑洞/巨星 数量 → 逐颗 StarGen.CreateStar
   │    逐颗 CreateStarPlanets（行星系统）→ 出生行星查找（Distribute==Birth）→ 星图
   │
   ├─ StarGen.CreateStar / CreateStarPlanets      ← 恒星 + 行星系统
   │    质量 ← 正态(level) → 温度 → 光谱 → 宜居带/轨道缩放
   │    行星数量表（光谱决定）+ 气巨概率 pGas[] + 轨道分配 + 小行星带
   │
   ├─ PlanetGen.CreatePlanet                      ← 单颗行星
   │    轨道参数（半径/倾角/周期/自转/奇观）→ 类型判定（宜居带归一化）
   │    SetPlanetTheme（主题过滤/温度匹配/随机选一）
   │
   └─ PlanetAlgorithm*.GenerateVeins/Terrain/Vegetables   ← 行星表面
        主题的 VeinSpot/VeinCount/VeinOpacity/RareVeins 配置
        恒星光谱系数 p 增强稀有矿 → 撒中心点 → 点内散布 → 储量公式
```

## 1. 随机数：DotNet35Random（可复现，任意语言可移植）

- 类实现 ≈ .NET Framework `System.Random`（Knuth 减法生成器，MSEED=161803398，SeedArray[56]），
  **但有两处 DSP 魔改（2026-10-20 与游戏 DLL 对拍确认）**：
  1. 构造器 `inextp = 31`（标准 .NET Framework 为 21）→ 首次取样索引即不同
  2. `Sample()` 返回 `raw × 4.656612875245797E-10`，`Next() = (int)(Sample() × int.MaxValue)`
     （标准版 Next 直接返回 raw 本身）→ 等价于 `Next() = raw - 1`（raw ≥ 1）
- `NextDouble()` = Sample()；`Next(a,b)` = `(int)((uint)(Sample() × (uint)(b-a)) + a)`（uint 截断，范围 ≤1 直接返回 a）。
- **坑（2026-10-19 实战教训）**：按标准 System.Random 实现会整链路分叉——
  恒星位置 63/64 对不上、行星数 253 vs 235、主题 157/180 错位；
  改为上述魔改版后：恒星层 60/62（2 颗为 CSV 舍入差）、主题 198/198、组数 197/198。详见 `docs\README.md`。
- 种子流惯用法：一个 `DotNet35Random(seed)` 依次 `Next()` 派生子种子，子种子再 new 新 Random —— 复刻时必须逐字节模仿调用顺序。

## 2. 银河生成（UniverseGen.CreateGalaxy）

- 输入：`gameDesc.galaxySeed / starCount / galaxyAlgo`（须 20200101~20591231）；`isRareResource` 时 `PlanetGen.gasCoef = 0.8`（气巨概率降 20%）—— 游戏"稀有无资源"选项。
- **恒星位置**：`GenerateTempPoses(seed, starCount, 4, minDist=2.0, minStep=2.3, maxStep=3.5, flatten=0.18)`：球面随机游走 + 碰撞检测（间距≥2.0），每 4 个取 1 个；实际 starCount 可能略少。
- **恒星类型配额**（星级 i 分配）：
  - `num5 = ceil(0.01*n + rnd*0.3)` → 白矮星；`num6` → 中子星；`num7 = ceil(0.016*n + rnd*0.4)` → 黑洞；顺序：黑洞最靠后（最远），其次中子星，其次白矮星，近内为主序/巨星。
  - 巨星：`i % num12 == num13`（等间隔抽签，`num12=(num11-1)/num8`，`num8=ceil(0.013*n+rnd*1.4)`）。
  - **i==0 出生星**（银河中心，position=0，level=0，resourceCoef=0.6）；`i==3` 强制 M 型红矮；`i==num11-1` 强制 O 型蓝巨星。
- **资源系数**：`resourceCoef = 7^logchain(dist/32) * 0.6`，dist 为到银河中心距离 —— **越靠外资源越丰富（指数级）**。
- **出生行星**：遍历 stars[0] 的行星，第一个 `ThemeProto.Distribute == Birth` 的 → `birthPlanetId`。
- **星图**：距离平方 <64（≈8 单位）的恒星互连成 `StarGraphNode`，线段整理避免交叉。

## 3. 恒星生成（StarGen.CreateStar）

- 每颗星独立 `seed`（银河 Random 流派生）。
- **质量/光谱（主序）**：`num12 = RandNormal(level映射, 0.33) + 微扰 + 1`，`mass = 2^num12`，clamp 后 ~0.19~25 M☉；level≈index/(n-1)，越靠后恒星越大越热。`RandNormal` = Box-Muller。
- **特殊恒星质量**：黑洞 `18+30*rand1*rand2`（18~48）；中子星 `7+11*rand`（7~18）；白矮 `1+5*rand`。
- **温度**：`temp = mass_adj^(0.56+0.14/log5(mass_adj+4)) * 4450 + 1300`（`mass_adj=(1-age^20*0.5)*mass`）。
- **光谱**：`classFactor = log2.6((temp-1300)/4500) - 0.5`，负值×4，clamp [-4,2] → `spectr = round(classFactor+4)` → M=0,K=1,G=2,F=3,A=4,B=5,O=6（X=7 特殊）。
- **亮度/半径**：`luminosity = mass_adj^0.7`；`radius = mass^0.4 * rnd(2^±0.2)`。
- **宜居带 & 轨道缩放**：`p = classFactor + 2`；`lightBalanceRadius = 1.7^p`；`habitableRadius = 1.7^p + 0.25*min(1,orbitScaler)`；`orbitScaler = 1.35^p`（<1 时向 1 插值 0.6）。`dysonRadius = orbitScaler*0.28`。
- **年龄演化（SetStarAge）**：`age<0.96` 主序；`age 0.96~1` → 红巨星（radius×巨大化，orbitScaler×3.3，habitable×9）；`age≥1` 大质量衰亡 → 白矮/中子/黑洞（具体看质量阈值）。CreateStar 时 needtype 显式指定 + age 双保险。
- **行星系统（CreateStarPlanets）**：
  - 黑洞/中子星：**固定 1 行星**；白矮星：70% 1 颗、30% 2 颗；巨星：1~3 颗（50% 概率 2 颗，30% 3 颗）。
  - 主序星数量表（num 随机判段）：M:1-4、K:1-5、G/F/A:3-5、B:4-6、O:5-6；**出生星固定 4 颗**。
  - **气巨概率 pGas[i]**（轨道 i）：M 型 0.2~0.3、K 0.18~0.28、G 0.18~0.3、F 0.2~0.31、A 0.1~0.35、B 0.1~0.35、O 0.1~0.35；出生星 pGas[0..2]=0（前三轨道无气巨）。
  - **轨道分配**：轨道半径表 `orbitRadius[1..16] = 0.4,0.7,1.0,1.4,1.9,2.5,3.3,4.3,5.5,6.9,8.4,10,11.7,13.5,15.4,17.5`（×orbitScaler）；相邻间跳号保间隔；出生星第 3 轨道必放一颗行星。
  - 气巨行星的后继行星成为其卫星（orbitAround=气巨 number，独立卫星轨道公式）。
  - **小行星带**：带1 在首个气巨前一轨道（`num4<0.2+轨道*0.2`）；带2 在最末主行星轨道 +1/+2/+3 处（num5 分段）；半径 `orbitRadius[n]*rnd*orbitScaler`。
- **黑雾（蜂巢）**：`hivePatternLevel/safetyFactor` 由恒星位置/亮度随机；`maxHiveCount` 由 `combatSettings.maxDensity`；黑洞/中子星为 epicHive（密度×2）；8 条 `hiveAstroOrbits`。**战斗系统，地图搜索器一般不需要**。

## 4. 行星生成（PlanetGen.CreatePlanet）

- **轨道参数**：`orbitRadius = orbitRadius[orbitIndex]*orbitScaler*(1+(1.2^ε-1)/max(1,r))/…`；倾角 ±8°（卫星×2.2，中子/黑洞再加 ±3）；周期 = 开普勒（G=1.353855e-6）；自转 ~400~1400s×(半径^0.25)×星类型系数（白矮×0.5/中子×0.2/黑洞×0.15），再与轨道周期倒数合成。
- **奇观（EPlanetSingularity）**：`TidalLocked`（轨道≤4 且 num14>0.96，自转=公转）、`TidalLocked2`（>0.93，自转=1/2 公转）、`TidalLocked4`（>0.90，1/4）、`LaySide`（<0.04 躺倒，倾角±70°）、`ClockwiseRotate`（0.85~0.90 逆转）。
- **类型判定公式**（关键）：
  - `num23 = sunDistance / habitableRadius`（归一化日距；1 = 宜居带正中）
  - `temperatureBias = 1.2/(num23+0.2) - 1`（近星+、远星−）
  - `habitableBias = |ln(num23)| * clamp(sqrt(habitableRadius),1,2) - 0.04`
  - **宜居判定**：`a = 全局宜居配额插值`（`a = clamp(lerp(缺失宜居数/剩余星数, 0.35, 0.5), 0.08, 0.8)`；`f = (habitableBias/a)^(10a)`；`num12 < f` → **Ocean（海洋/类地）**，`galaxy.habitableCount++`。目标 ≈ `ceil(starCount*0.29)`（≥11）颗宜居行星。出生星第 3 轨道卫星无条件 Ocean。
  - 非宜居：`num23<0.833` → 内区：`num13 < max(0.15, 2.5*num23-0.85)` 为 **Desert（荒漠）** 否则 **Vocano（火山）**；`0.833≤num23<1.2` → **Desert**；`num23≥1.2` → 外区：`num13 < 0.9/num23-0.1` 为 **Desert** 否则 **Ice（冰原）**。
- **气巨行星**：type=Gas，radius=80、scale=10，`habitableBias=100`（永远不判宜居）。
- **亮度**：`luminosity = (lightBalanceRadius/(sunDistance+0.01))^0.6` 多级 log 折叠。
- **主题选择（SetPlanetTheme）**：
  - 候选范围：`themeIds`（= gameDesc.savedThemeIds，游戏中"星球分布"选项可裁剪）；null 时用 `ThemeProto.themeIds`（全部）。
  - 过滤：出生星（index==0）Ocean + `Distribute==Birth` → 直接候选；否则 `theme.PlanetType == 行星类型 && theme.Temperature*temperatureBias >= -0.1`；荒漠特判（|Temperature|<0.5 的荒漠要求温度偏差匹配）；出生星只收 `Default`，其它星收 `Default|Interstellar`；**同星系内主题不重复**（同星前面行星已用的排除）。
  - 兜底：无候选 → 全部 Desert 主题；再空 → 任意 Desert。
  - 随机取一 → `planet.theme`；`algoId = theme.Algos[rand]`（地形算法编号）；`mod_x/mod_y` 在 `ModX/ModY` 区间均匀采样（地形变形）；`style = theme_seed % 60`。
- **气巨气体**：`GasItems/GasSpeeds`（主题配置），每气体速度 × `0.909~1.1 随机` × `gasCoef`(稀有无 0.8) × `resourceCoef^0.3`；`gasTotalHeat = Σ 热值×速度`。

## 5. 主题体系（ThemeProto / EThemeDistribute / 地形算法）

主题 = 行星"皮肤"：决定表面材质、温度、植被、矿脉配置、气体、海洋、风、电离层高度等。

- `EPlanetType`：None / Vocano / Ocean / Desert / Ice / Gas（行星大类）。
- `EThemeDistribute`：Default / **Birth**（出生点）/ Interstellar（仅非出生星）/ Rare。
- `ThemeProto` 关键字段：
  - `PlanetType`、`Temperature`（主题温度）、`Distribute`
  - `Algos[]`（可选地形算法 id 列表，`planet.algoId` 从中随机）、`ModX/ModY`（地形扰动范围）
  - `Vegetables0~5`（植被原型集）、`VeinSpot[]/VeinCount[]/VeinOpacity[]`（矿脉配置，按 EVeinType 索引 1..14）
  - `RareVeins[]/RareSettings[]`（稀有矿四元组：出生星概率/非出生星概率/级联概率/（第四位））、`GasItems[]/GasSpeeds[]`
  - `WaterHeight/WaterItemId`（海洋：0=无海、-2=无海且陆地 100%、>0 流体物品 id）、`IceFlag`、`Wind/IonHeight`
- 地形算法 `PlanetAlgorithm0~14`：每个主题可多选，`GenerateTerrain(modX,modY)/GenerateVegetables/CalcWaterPercent/GenerateVeins`；基类 `PlanetAlgorithm` 有公共矿脉生成。**具体地形形状在 PlanetAlgorithm11（默认多数主题）等子类**。

## 6. 资源（矿脉）生成（PlanetAlgorithm.GenerateVeins）

- **矿种映射（EVeinType，数组索引 = 类型值）**：1 Iron 铁、2 Copper 铜、3 Silicium 硅石、4 Titanium 钛、5 Stone 石、6 Coal 煤、7 Oil 原油、8 Fireice 可燃冰、9 Diamond 金伯利矿石、10 Fractal 分形硅石、11 Crysrub 有机晶体、12 Grat 光栅石、13 Bamboo 刺笋结晶、14 Mag 单极磁石。1-7 普通矿（主题配置决定有无），8-14 稀有（9-14 主题级/星系级）。
- **恒星系数 p**（稀有矿概率增强 `1-(1-r)^p`，逻辑等价）：主序 M 2.5 / K 1.0 / G 0.7 / F 0.6 / A 1.0 / B 0.4 / O 1.6；巨星 2.5；白矮 3.5；中子 4.5；黑洞 5.0。
- **特殊恒星固定矿**：白矮星 Diamond 金伯利矿石 +2（45% 级联再加）、Fractal 分形硅石 +2、Grat 光栅石 +1（各 45~50% 级联，VeinCount=0.7）；中子星/黑洞 Mag 单极磁石 +1（65% 级联，VeinCount=0.7）→ **单极磁石只有中子星/黑洞系**。
- **主题稀有矿（RareVeins/RareSettings）**：每稀有矿 4 个浮点：`[出生星概率, 非出生星概率, 级联概率, 备用]`；命中后 spot 数 +1 并按级联概率循环加组。`RareSettings.Length == RareVeins.Length*4`（代码自检）。
- **spot 散布**：
  - 每种矿 spot 数 = `VeinSpot[type]`（±1 抖动）；石油额外 15% 转水下（`kUnderwaterOilPercentage`）。
  - 中心点：球面随机点 + 出生点偏置 `birthPoint`（出生星用 `birthResourcePoint0/1` 固定铁/铜各 1 个特殊点；非出生星随机偏置向量）；与已有中心点距离 ≥ `num*sqrt(num16)`（num=2.1/radius；铁铜 196、油 100）。
  - 每组：`min(20~25 × VeinCount[type], …)` 个矿点，围绕中心点二维随机游走散布（间距≥0.85²），投影到球面。
- **储量公式**：
  - 单脉基准量 `base = VeinOpacity[type] * 100000 * resourceCoef`（下限 20；上限 15000×1.066）；石油用 `sqrt(resourceCoef)`。
  - 每脉 `amount = round(base ±6.25% 抖动)`，再 × `resourceMultiplier`（非油）/`oilAmountMultiplier`（油）；下限 1。
  - **出生星特例**：resourceCoef ×2/3；前两组矿（铁/铜）固定 6 点、VeinOpacity 0.2。
  - **"稀有无"选项**：resourceCoef >1 时开 0.8 次幂，再 ×0.7（全局降储量）。
  - **"无限资源"选项**：非油全部 amount=10 亿。
- **落点**：矿点贴地表高度，油贴海面（RawSnap）；有海洋（waterItemId>0）时水下非油矿丢弃；`data.AddVeinData`。运行期 `VeinGroup/runtimeVeinGroups` 汇总行星矿脉（`StarData.CalcVeinAmounts` 遍历）。

## 7. 陆地百分比 / 海洋

- `CalcLandPercent`：气巨 0；`waterItemId == -2` → 1（无海全陆）；否则扫描高度数据 `heightData ≥ radius*100-20` 或 modlevel==3 的采样点占比。`landPercent` 是"观测值"（由地形+水面算），不是输入。
- 海洋流体由 `WaterItemId`（>0 时物品 id，如硫酸 → 硫酸海洋主题）。

## 8. 名称与语言

- 恒星名：`NameGen.RandomStarName(seed, LCID, star, galaxy)`；中文（LCID 2052）行星名 = `{星名}{数字}号星`（数字用罗马数字）；其它语言同。
- 语种影响命名种子流（LCID 参与），但**不影响星系/行星结构**。

## 9. 已知数据状态（2026-10-18 更新）

- ✅ **主题/矿脉/物品/配方/科技/植被数值已提取**：从 `resources.assets`（Unity 2022 序列化，427MB）用 UnityPy + 反编译字段布局解码，见 `src\data\`（json\ + CSV 摘要 + data\README.md 数据手册）。共 25 主题 / 14 矿 / 175 物品 / 162 配方 / 315 科技 / 286 植被。
- ✅ 与 DspFindSeed 旧版 XML 导出对照验证：主题/矿脉数量一致；当前版本新增字段（VeinProto.IconTag/iconTagString）与黑雾物品（122→175）。
- `gameDesc.savedThemeIds`（"星球分布"选项可裁剪的主题集）默认 = `ThemeProto.themeIds`（25 个全部主题）。
- ✅ **地形/矿脉算法已全部复刻**（原"未展开/搜索器不需要"条目已过时）：PlanetAlgorithm 全变体（0~14，
  DspFindSeed-3.1 验证基座；DspSeedFinder 纯复刻为基类+PA11）+ GenerateVeins，P2 存档对拍 **197/198**（my-save）+ **199/200**（6807826）。
- 旧版参考：`ref\DspFindSeed-3.1\`（C# 种子查找器，复刻了部分生成逻辑 + 旧版 XML 导出，算法可参考、数据过时）。

## 10. 地图搜索器实现状态（2026-10-18 更新）

目标：给 seed → 输出每颗恒星每颗行星的类型/主题/矿脉预测（资源分布）。

- ✅ **P0 复刻算法**：`DotNet35Random` 1:1 移植（System.Random 算法，30 行）；`UniverseGen` → `StarGen` →
  `PlanetGen` 全链路（星表/行星表：类型、主题 id、orbit）。
- ✅ **P1 地形**：SimplexNoise + 球面网格 + PlanetAlgorithm 全变体（0~14，DspFindSeed-3.1 验证基座）+
  CalcLandPercent（陆海比存档逐颗验证）。
- ✅ **P2 资源量**：DLL 版（DspFindSeed-3.1，引用游戏 DLL）完整建模，存档对拍 my-save **197/198**、6807826 重存 **199/200** 精确一致；
  仅剩出生星（104/103 出生点特殊矿，约定忽略）。关键修复：Slerp 相对向量法（2403）、QueryHeight 用 DLL 原版（3301）、
  **位置候选点 zero 浮点运算顺序（1502/1801——基类 double vs 游戏 float，1 ulp 触发高度带边界翻转）**，见 `docs\README.md`。
  ⚠️ **该对拍结论属于 DLL 版；纯复刻 DspSeedFinder 的完整对拍是 2026-10-20 才做的**（见下）。
- ✅ **2026-10-20 纯复刻修复（DspSeedFinder 完整对拍 seed=1）**：
  - 根因一：`DotNet35Random` 照标准 System.Random 实现（缺 DSP 魔改 inextp=31 / Next 截断）→ 全链路分叉（§1）
  - 根因二：地形算法 0~14 未移植（仅 PA1/PA11 占位）、GenerateVeins 简化（无落点/无组数抖动）
  - 已修：完整移植 PlanetAlgorithm 0~14 + GenerateVeins（源：DspFindSeed-3.1 验证版）+ 数学库补齐（Vector2/Vector4/RandomTable/SimplexNoise 原版）
  - 结果 vs DLL：主题 **198/198**、行星数 235 一致、组数 **2770/2772**（仅 1502 草原油 18/17、有机晶 2/3 差 1）；
    储量中位一致（P50 -0.02%）但长尾 ±80%（复刻 Math 与 Unity 浮点 1 ulp 边界翻转，属已知差距）
  - 存档解析（DspFindSeed-3.1 --save）与存档仍 197/198（2026-10-20 复现），不受复刻影响
- ✅ **P3 搜索器 v1**：`src\DspSearcher`（.NET 8 WPF + `--headless` 无窗口入口）——两阶段：
  快筛（全局队列 + worker 本地批处理，32 线程实测 ~1 万 seeds/s、全空间 ≈2.7h）+ 精算 top 池 500（全量地形+矿脉）；
  过滤 4.1 O星≥3 / 4.2 黑洞中子星远近（展示列）/ 4.3 母星系冰巨星 / 4.4 6ly 全覆盖含火山灰硫酸；
  评分=合并配比瓶颈法（气巨站数固定 40）；断点续跑；产出 results.csv（易读单位+分析列）/ refined-detail.csv / search-report.txt。
  注意：Win11 异构调度失焦时把线程赶 E 核（CPU 掉 ~50%），headless 可避免。方案 `docs\SEARCHER-PLAN.md`。
- **校验**：同一 seed 存档解析逐颗对照（my-save.dsv，198 颗已扫描）；宜居比例 ~34%（锚点 29% 为旧版近似，实测以存档为准）。
- **性能**：快筛 32 线程 ~1 万 seeds/s（i9-13980HX 实测 10258/s）；精算 27s/种子。
- **2026-10-20 A2 架构调整**：`DspSearcher` 迁 **net48 + 引游戏 DLL**（唯一操作栈，后续 S1~S5 都在这里）；
  `DspCore48` 为新权威生成引擎（net48 类库）；`DspFindSeed-3.1` 退为验证基座；`DspSeedFinder`（net8）已退役删除（2026-10-20，git 历史可恢复）。
  浮点精度边界已修复（2026-10-20）：根因 = DspSearcher 进程位宽（AnyCPU 默认 x64/SSE2 vs 验证基座 x86/x87）
  → CalcVerts 顶点 1 ulp → QueryHeight 临界翻转（3301/2403/1502）；`DspSearcher.csproj` 加 `PlatformTarget=x86`
  后对齐存档（3301 石 1,143,086、198 颗对拍仅剩母星 104 约定忽略），排查记录 `docs\FLOAT-NOTES.md`。
- **后续进度（2026-10-21~25）**：存档解析扩展（戴森球/工厂/黑雾 189/189/relay/采集站/未开采）、快筛提速（DspFastScan 5.5×）、S2 galaxy.json（`--seed --json`）完成 —— 详见 `docs\README.md` 与 `docs\VIEWER-PLAN.md`（S1✅ S2✅，下一步 S3 规划）。

## 类文件索引（ref\ref\decomp-full\）

- `UniverseGen.cs` / `StarGen.cs` / `PlanetGen.cs` / `PlanetAlgorithm.cs`（+`PlanetAlgorithm0~14.cs`）
- `StarData.cs` / `PlanetData.cs` / `GalaxyData.cs` / `ThemeProto.cs` / `VeinProto.cs` / `VeinData.cs` / `VeinGroup.cs`
- `EStarType.cs` / `ESpectrType.cs` / `EPlanetType.cs` / `EThemeDistribute.cs` / `EVeinType.cs` / `EPlanetSingularity.cs` / `EAstroType.cs`
- `DotNet35Random.cs` / `NameGen.cs` / `GameDesc.cs`（GameDesc 在 GameData 或 DSPGame 相关文件中）
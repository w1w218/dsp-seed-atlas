# 地图搜索器（DSP Seed Finder）技术可行性方案

> ⚠️ 历史文档（2026-10-20 A2 后）：文中基于 net8 架构的 DspSeedFinder 已退役删除，
> 现权威栈为 `DspSearcher`（net48 + 游戏 DLL）+ `DspCore48`；本文保留作方案背景参考。

目标：给定种子 → 输出每颗恒星/行星的完整信息，**含各星球真实资源数量（矿脉总储量）**——
这是旧版 DspFindSeed 未实现的部分（作者在 readme 中说明"矿物数量涉及地形生成算法，未开放"）。

## 一、完整计算链（已全部从反编译确认，ref\ref\decomp-full\）

```
GameDesc(galaxySeed, starCount, ...)
  │ DotNet35Random（=System.Random，1:1 可移植）
  ├─ UniverseGen.CreateGalaxy      —— 星系：恒星位置/类型配额/出生星/星图        (已读, mechanics §2)
  ├─ StarGen.CreateStar/…Planets   —— 恒星参数 + 行星系统（数量/轨道/气巨/小行星带）(已读, §3)
  ├─ PlanetGen.CreatePlanet        —— 行星轨道/类型/奇观/主题选择/气巨气体         (已读, §4)
  └─ PlanetModelingManager.ModelingPlanetMain（管线, 行 837-851）
       ├─ PlanetAlgorithm*.GenerateTerrain(mod_x, mod_y)   ← 地形高度场（必须复刻）
       │    PlanetAlgorithm11：SimplexNoise×3 + FBM + Remap + Levelize2/3
       │    （特殊主题用 Algorithm 0~14 变体；algoId 由主题 Algos 随机）
       ├─ CalcWaterPercent()       —— landPercent（陆地比，影响矿脉落点）
       ├─ GenerateVegetables()     —— 植被（搜索器可跳过，影响极小）
       └─ GenerateVeins()          —— 矿脉（资源量核心，已完整读懂 §6）
            → PlanetRawData.AddVeinData → VeinGroup 汇总（每行星按类型统计储量）
```

**资源量定义**：某行星某种矿的总储量 = Σ(每个 VeinData.amount)，其中
- spot 组数：`VeinSpot[type]`（±1 抖动）+ 稀有矿按 `RareSettings` 概率（受恒星系数 p 增强）加组
- 每组矿点数：`min(20~25×VeinCount[type], …)` 个，二维随机游走散布到球面
- 单点储量：`VeinOpacity[type]×100000×resourceCoef`（±6.25% 抖动；石油用 √resourceCoef）
- **落点过滤**：中心点须在地表以上（`QueryHeight ≥ radius`）；海洋下的非油矿丢弃
  → **矿脉数量/储量依赖地形高度场与海洋分布**，必须复刻 GenerateTerrain 才能精确

## 二、可移植性评估（关键依赖全部存在于反编译，纯 CPU 可复刻）

| 组件 | 反编译文件 | 复刻难度 | 说明 |
|---|---|---|---|
| DotNet35Random | DotNet35Random.cs | ★ 低 | System.Random 算法，30 行 |
| SimplexNoise | SimplexNoise.cs | ★★ 中 | 标准 Simplex 噪声（含 3D FBM），~200 行 |
| Maths.Levelize2/3 | Maths.cs | ★ 低 | 纯数学函数 |
| PlanetRawData 顶点网格 | PlanetRawData.cs | ★★ 中 | verts200 静态球面顶点缓存（precision 200：201×201×4=161,604 顶点）；需复刻一次生成（SphereGenerator） |
| QueryHeight | PlanetRawData.cs | ★ 低 | 顶点索引查表+插值 |
| PlanetAlgorithm11（通用地形） | PlanetAlgorithm11.cs | ★★★ 中高 | 逐顶点 FBM 噪声 + Levelize（每行星 16 万顶点 × 3 噪声，单行星预计 <50ms） |
| PlanetAlgorithm 0~14 变体 | PlanetAlgorithm*.cs | ★★★ 中高 | 气巨/冰/火山等特殊算法；已全部实现（DspFindSeed-3.1 验证基座） |
| GenerateVeins（矿脉） | PlanetAlgorithm.cs（基类） | ★★ 中 | 已完整解读（中心点/组点/储量/过滤），~150 行 |
| 行星/恒星结构 | UniverseGen/StarGen/PlanetGen | ★★ 中 | 已完整解读（mechanics.md §2-4） |

**结论：技术完全可行**。无黑盒（不依赖 Unity 运行时）；所有随机均源于 DotNet35Random 种子流，
同种子必同结果（确定性）。

## 三、性能预算（对照 DspFindSeed 实测）

- DspFindSeed（C#，无地形/矿脉）：约 1 万种子 30~60 秒（单进程）
- 增加地形+矿脉（作者估算）：约 100 秒/万（多进程可线性扩展）
- **本项目实测（P3 搜索器，2026-10-19）**：快筛 32 线程 ≈ 1 万 seeds/s（全局队列 + worker 本地批处理 + ServerGC），
  全空间 1 亿种子 ≈ 2.7 小时；精算（top 500 全量地形+矿脉）≈ 14 分钟（16 核）
- 本项目语言选择：**C#（.NET 8，与 DspFindSeed 一致）或 Python**？
  - C#：与 DspFindSeed 结构/算法直接对照移植（它已有 UniverseGen/StarGen/PlanetGen 实现），性能好
  - Python：开发快但性能差（16 万顶点×3 FBM×行星数 会慢 10-50 倍）
  - **建议 C#**（dotnet 8 已装；DspFindSeed 源码可参考其 UI/条件逻辑）

## 四、与 DspFindSeed 的分工

- **可直接参考（C# 版）**：UI（WPF 条件面板/CSV 导入导出/多开）、UniverseGen/StarGen/PlanetGen 结构、
  GameDesc、搜索循环（多进程切分种子区间）
- **必须重写/新增**：PlanetAlgorithm11 地形 + SimplexNoise + PlanetRawData 网格 + GenerateVeins 矿脉 +
  VeinGroup 汇总 → 真正的每星球资源数量
- 数据：直接用 src\data\（主题/矿脉/物品表 json）——比 DspFindSeed 的旧 XML 新

## 五、实施阶段（进度标注 2026-10-18）

1. **P0 星表/行星表**（✅ 完成）：DotNet35Random + UniverseGen→StarGen→PlanetGen → 恒星/行星结构
   （类型/主题/轨道/奇观/资源系数）——DspSeedFinder（纯复刻）与 DspFindSeed-3.1（游戏 DLL 基座）均有
2. **P1 地形**（✅ 完成）：PlanetRawData 网格 + SimplexNoise + Algorithm 0~14 全变体 → landPercent 可验证
   （存档解析逐颗对照：地中海 60.05%、黑洞星 98.87% 等与游戏面板一致）
3. **P2 资源量**（✅ 完成）：GenerateVeins + VeinGroup 汇总 → 每行星每矿种总储量
   （存档对拍 **197/198 精确一致**，剩余仅母星 104（约定忽略），见 docs\README.md）
4. **P3 搜索器**（✅ 完成 v1，2026-10-19）：`DspSearcher` 独立 WPF 工程（引用 DspSeedFinder），
   两阶段（星表过滤快筛 → top 池精算）+ 断点续跑 + 全局种子队列多线程；配比 = 合并（矩阵+戴森球），
   过滤 4.1 O星≥3 / 4.2 黑洞中子星远近（优先）/ 4.3 母星系冰巨星 / 4.4 6ly 全覆盖；
   方案见 `SEARCHER-PLAN.md`，产出 top20.csv / top20-galaxies.csv / search-report.txt
5. **P4 优化**（部分已在 v1 内建：快筛先星表后行星；待做）：气巨采集站数精确化、
   精算相关性监控（理论 vs 精确 top 池验证）、过滤条件参数扩展

## 六、验证锚点（2026-10-18 状态）

- 出生星：固定 4 行星、主题 Ocean 1（地中海 Birth）、resourceCoef=0.6 ✅（存档解析陆海比 60.05% 一致）
- 恒星类型配额、光谱分布、宜居行星比例 ~34% ✅
- 矿脉：主题 VeinSpot 与 src\data\themes.csv 一致 ✅；存档对拍 198 颗中 **197 颗组数+储量全对** ✅
- 关键修复（2026-10-18）：顶点 `Slerp` 改用相对向量法（修 2403）、`QueryHeight` 改用游戏 DLL 原版（修 3301）、**位置候选点 `zero` 运算顺序（修 1502/1801）**
- 剩余差异：仅母星 104（出生点特殊矿，约定忽略），见 docs\README.md

## 代码位置（已实现，2026-10-19）

- ~~`DspSeedFinder\`~~：纯复刻（.NET 8），已于 2026-10-20 并入 `DspCore48` 并删除
- `src\DspSearcher\`：P3 搜索器（现为 net48 x86，引用 `DspCore48`），方案 `SEARCHER-PLAN.md`
- `ref\DspFindSeed-3.1\`：游戏 DLL 基座验证项目（存档解析 `--save` + 完整建模 `--real-veins/--veins-csv` + 对拍主工具）
- 参考：`ref\DspFindSeed-3.1\`（旧版 C#，算法可参考、数据过时）；本文档与 mechanics.md 为算法依据
- 数据：`src\data\`（json/CSV）
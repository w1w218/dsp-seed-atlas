# 文档索引（docs）

戴森球计划（DSP）星系搜索 / 规划 / 浏览器工具链的文档中心。

## 快速导航

- **`COMMANDS.md`** — 所有 CLI 用法、双击菜单、端到端流程（**先看这个**）
- `mechanics.md` — 机制逆向笔记（种子 → 星系 → 恒星 → 行星 → 矿脉）
- `SAVE-FORMAT.md` / `SAVE-EXTRACT.md` — 存档二进制格式（权威）与提取规格
- `SEARCHER-PLAN.md` / `FEASIBILITY.md` / `FLOAT-NOTES.md` — 搜索器方案 / 可行性 / 浮点精度
- `PLANNER-DESIGN.md` / `PLANNER-IMPL.md` — 规划器设计 / 实现
- `VIEWER-PLAN.md` / `VIEWER-IMPL.md` — 浏览器方案 / 实现
- `PARSE-DSV.md` — 早期 python 存档解析（历史文档）

## 仓库结构

- **`src\`** — 公开代码：六个工程 + `data\` + `tools\` + `scripts\`
- **`ref\`** — 本地参考资料，**不随仓库发布**：反编译源码（`decomp-full`/`decomp-selected`）、
  REA 结构证据、第三方参考项目（`DspFindSeed-3.1`）、游戏原始资源块

> 文档中出现的 `ref\...` 路径均为本地参考，公开仓库中不存在。

## 项目目标

给定种子 → 星系/行星全信息 + **真实资源数量**；再叠加生产规划与存档融合，用网页浏览。

## 工程一览（2026-10-29）

- **`DspCore48\`**（net48 + net8.0 双目标，类库）— 权威生成引擎：UniverseGen / StarGen / PlanetGen /
  PlanetManager 0~14 / GenerateVeins + 存档解析（SaveParser / FactoryExtractor / HiveExtractor /
  DysonExtractor / SpaceSectorExtractor / SpaceAnalyzer）
- **`DspSearcher\`**（net48 **x86**，WPF + `--headless`）— 唯一精算栈：搜索 / 单种子 `--json` / 存档 / 融合。
  **必须 x86**（x87 浮点与游戏一致，否则矿脉错位，见 `FLOAT-NOTES.md`）
- **`DspFastScan\`**（net8 x64）— 快筛引擎（~5.5×），结果供 net48 精算接力
- **`DspPlan\`**（net8）— 规划器：推流 + 多链退火 + LNS，写回 `galaxy.json` 的 `plan` 区块
- **`DspSave\`**（net8 x64）— 存档解析/融合专用入口（大档在 x86 下会 OOM）
- **`DspView\`**（net8）— 渲染器：27 列表格 + 3D 双视图（星系/恒星系）+ `--serve`
- **`DspFindSeed-3.1\`** — 旧版验证基座（引用游戏 DLL），仅作对拍参照；**本地参考项目，位于** `ref\DspFindSeed-3.1`（不随仓库发布）
- 已删除：`DspSeedFinder`（net8 纯复刻，2026-10-20 并入 `DspCore48`，git 可恢复）

命令用法统一见 **`COMMANDS.md`**。

## 进度

- ✅ **P0 星表/行星表**（2026-10-17）：DotNet35Random + UniverseGen → StarGen → PlanetGen 结构层
  - 验证锚点通过：出生星 G 型/资源×0.6/4 行星/地中海出生点（气巨卫星）、宜居比例 ~34%、恒星光谱/质量分布合理
- ✅ **P1 地形**（2026-10-17）：SimplexNoise（含 DSP 3D 缩放 32.696434）+ 球面网格（六面体 Slerp）+
  PlanetAlgorithm 全变体（0~14，DspFindSeed-3.1 验证基座；DspSeedFinder 纯复刻 2026-10-20 已全量移植 0~14）
  + CalcLandPercent 陆海比
  - 陆海比验证（存档解析逐颗对照，与游戏面板一致）：地中海出生星 60.05%、黑洞星 98.87% 等
- ✅ **P2 资源量**（2026-10-18）：GenerateVeins + VeinGroup 汇总
  - **存档对拍：198 颗已扫描行星，197 颗精确一致**（20 颗仅油浮点粒度差），参照 `my-save.dsv`（seed=1、64 星、资源倍率 1）
  - 命令：`DspFindSeed-3.1\DspFindSeed\bin\Debug\DspFindSeed.exe --seed 1 --stars 64 --veins-csv ...`（**DLL 版**，引用游戏 DLL）；存档解析 `--save`；对拍脚本 `src\tools\compare_save_model.py`（油按 流速×25000=amount 口径；CSV 中间产物在 `_scratch\`）
  - ⚠️ 该 197/198 结论属于 **DLL 版**；纯复刻 DspSeedFinder 的完整对拍见下方“2026-10-20 复刻对拍修复”
  - **2026-10-18 晚修复**：顶点 `Slerp` 改为 Unity 相对向量法（→ 2403 全对）、`QueryHeight` 改用游戏 DLL 原版（→ 3301 全对）、`birthPoint` 浮点写法对齐原版；`GenerateVeins` 已与反编译原版逐行 diff 无实质差异
  - **2026-10-19 多线程修复**：`PlanetGen.tmp_theme`/`StarGen.pGas` 静态可变 → 局部（搜索器 16 线程并发生成的前提）；`UniverseGen` 拆 `CreateGalaxyLight`（星表层，随机流一致）+ `PopulatePlanets`（补行星，已验证 4 种子全量一致）
- ✅ **P3 搜索器 v1**（2026-10-19）：`DspSearcher`（独立 .NET 8 WPF 工程，引用 DspSeedFinder）
  - 方案：`SEARCHER-PLAN.md`；配比 = 合并（矩阵 10 万/min + 戴森球，3 级增产），瓶颈评分，过滤 4.1 O星≥3（强制）/4.2 黑洞中子星近≤8远≥15（优先加分）/4.3 母星系冰巨星（强制）/4.4 6ly 全覆盖含火山灰硫酸（强制）
  - 两阶段：快筛（星表层先淘汰 → 理论储量含距离加成）→ 精算（top 池全量地形+矿脉）
  - 实测：快筛 **32 线程 ~1 万 seeds/s**（i9-13980HX 实测 10258/s，CPU 96%）→ 全空间 1 亿种子 ≈ **2.7 小时**；理论 vs 精确分差 <0.5%（瓶颈矿 <2%，稀有矿 ±4~6%）；命中率 ~8~11%（含火山灰硫酸条件）
  - 关键技术约束：行星生成不可“分部”（habitableCount 宜居配额为跨行星全局累计状态，跳星生成会破坏判定上下文）；4.4 覆盖检查已并入评分循环（组数只算一遍）
  - 产出：`search-out/<时间戳>/` 下 checkpoint（断点续跑，10 万种子/次）、search.log、**results.csv**（全部候选：易读单位亿/小时 + 6ly丰富度/均衡度 + 冰巨卫星/最近O星分析列）、refined-detail.csv（行星级，程序读）、search-report.txt
  - 使用：GUI 直跑，或 `DspSearcher.exe --headless --from 0 --to 99999999 --threads 32`（日志写任务目录 search.log）；
  单种子精算 `--headless --seed N [--resource-multiplier 0.1] --out <dir>`（倍率默认 1，对拍存档时传入同口径）、
  存档解析 `--headless --save <dsv> [--out <csv 文件>] [--space 0/1]`（空间档默认算，`--space 0` 跳过）；
  命名默认中文；模型 refined-detail.csv 与存档 saveX-planets.csv 字段对齐，见 VIEWER-PLAN §S1；
  存档解析已支持 888MB 大档（流式+多线程，~1s），空间档见 SAVE-EXTRACT.md §6；
  随 `--save` 自动产出 `saveX-dyson.csv`（戴森球）+ `saveX-dyson-layers.csv`（逐层）；
  搜索/精算线程独立：`--refine-threads N`（默认 8，x86 内存限制），精算支持断点续算（Checkpoint.RefineDone）。
  注意：Win11 异构调度会在进程失焦时把线程赶到 E 核（CPU 掉到 ~50%）；headless 无窗口可避免；双进程 GUI+worker 方案实测 GUI 关闭后仍掉速，已回退

## P2 剩余差异（1/198，仅母星 104，约定忽略）

| 行星 | 主题 | 差异 |
|---|---|---|
| 104 | 地中海（母星） | 铁 -22.4%、铜 +49.5%（出生点特殊矿点，约定忽略）、石 +6.3%、煤 +16.1%、油 -4.2% |

### 2101 复测补充（2026-10-20，6807826-沙盒）

- 提交前复测（DspSearcher x86 修复后 vs `6807826-沙盒.dsv`）：**198/200 精确一致**
- 剩余：母星 103（出生点特殊矿，约定忽略）+ **2101（巨蟹座γ，草原）**：
  有机晶 存档 4 组/13,941,204 vs 模型 2 组/6,702,653（-51.9%）、铁 -0.4%、铜 -5.3%、
  硅 -1.1%、钛 -7.0%、石 +1.7%、煤 -5.7%
- 性质：**DspFindSeed 模型侧同样存在**（模型两个 x86 实现完全一致，但与游戏/存档差）→
  **非浮点修复引入**，属模型 vs 游戏生成边界差异（1502 类，待排查）

### 1502 已修复（2026-10-18 深夜）

- 根因：位置候选点 `zero` 的浮点运算顺序（基类 double 写法 vs 游戏 float；差 1 ulp 触发高度带边界判定翻转 → rng2 分叉）
- 修复后全部矿种逐位等于存档（油仅剩浮点粒度差）；6807826 种子的 1801 橙晶荒漠同步修复（候选点 float 写法）
- 详见 `data\p2-verification.md`

### 1502 有机晶排查结论（2026-10-18 早，已被上节取代）

- 判定正确：`destinationArray1[11]=3`（与存档 3 组一致）；生成失败：抖动后只剩 2 组
- 随机流证据：铁 7 组 + 铜 5 组 + 煤 7 组方向与存档夹角 <2°；有机晶/刺笋位置分叉（12°~84°）
- 油组数 18 vs 17（水下油配额实测未触发：uw 恒 3、n8 恒 18）

## 2026-10-20 复刻对拍修复（DspSeedFinder vs DLL/存档）

> ⚠️ 本节为历史记录：`DspSeedFinder`（net8 复刻）已于 A2 后退役删除；现在的权威栈为 DspSearcher + DspCore48。

背景：P2 存档对拍（197/198）一直是 **DLL 版**（DspFindSeed-3.1）做的；纯复刻 DspSeedFinder 从未整星系对拍。
首次用 seed=1 对拍即爆出大面积错位（主题 157/180 错、行星数 253 vs 235、恒星位置 63/64 不对）。

两个根因（均已修复）：

1. **DotNet35Random 按标准 System.Random 实现**（缺 DSP 魔改）：`inextp=31`（非 21）、`Next()=(int)(Sample()×2147483647)`（非 raw 本身）→ 随机流全链路分叉。
   mechanics.md §1 已更新记录。
2. **地形算法 0~14 + 完整 GenerateVeins 未移植**：DspSeedFinder 原只有 PA1/PA11，GenerateVeins 无落点/撒点/组数抖动。
   已从 DspFindSeed-3.1 验证版（历史 197/198 对拍通过的实现）整体移植：`Gen\PlanetAlgorithm.cs`（基类+0~14）+
   数学库补齐（Vector2/Vector4/RandomTable/SimplexNoise 原版/Num7 储量系数 1.1 等）。

对拍结果（seed=1，复刻 vs DLL 权威）：

- 恒星层：60/62 精确匹配（2 颗为 CSV 显示舍入差）
- 主题：**198/198**（修复前 23/180）
- 行星总数：235（修复前 253）
- 组数：**2770/2772**（仅 1502 草原：油 18/17、有机晶 2/3 差 1）
- 储量：中位一致（P50 -0.02%），但长尾 ±80%（复刻 Math 与 Unity 浮点 1 ulp 边界翻转，属已知差距）

存档解析（DspFindSeed-3.1 --save）与存档对拍 197/198 于 2026-10-20 复现，不受复刻侧影响。

## 2026-10-20 A2：DspSearcher 迁 net48 + 游戏 DLL（唯一操作栈）

- **`DspCore48`**（新增，net48 类库）：从 DspFindSeed-3.1 验证版提取的权威生成引擎
  （UniverseGen/StarGen/PlanetGen/PlanetManager 0~14/GenerateVeins + SaveParser/SaveExport），
  经 `GenApi`（CreateGalaxy/CreateGalaxyLight/PopulatePlanets/ModelPlanet/VeinsCsv/Save/Init）封装；
  与 DspFindSeed-3.1 --veins-csv 输出逐字节一致（12:23 验证）
- **`DspSearcher`** 迁移 net48 WPF + 引游戏 DLL + DspCore48；`--headless --seed N` 单种子精算可用；
  System.Text.Json → Newtonsoft；类型冲突（游戏 DLL global 同名类）全限定 DspCore.*
- **浮点精度边界已修复（2026-10-20）**：根因是 `DspSearcher` 进程位宽（AnyCPU 未设
  `Prefer32Bit` → 64 位系统默认 **x64/SSE2**，DspFindSeed 为 **x86/x87**），同 IL 的
  `CalcVerts`/`Slerp` 顶点差 1~4 ulp → QueryHeight 临界判定翻转 → 3301/2403/1502 储量偏差；
  `DspSearcher.csproj` 加 `<PlatformTarget>x86</PlatformTarget>` 后全部对齐存档
  （3301 石 = 1,143,086、vertices 0 差异、198 颗对拍仅剩母星 104 约定忽略）。
  排查记录见 `FLOAT-NOTES.md`

## 2026-10-21 存档解析扩展（戴森球 / 快筛并发 / 工厂段方案）

- ✅ **戴森球提取**（`DspCore48\DysonExtractor.cs`）：1212 魔法标记特征扫描 → 流式解析
  DysonSphere.Import 后半（层数 + 每层节点/框架/壳三池 + 在途火箭）；`--save` 自动产出
  `saveX-dyson.csv`（每球 层数/总·已建/恒星对应）+ `saveX-dyson-layers.csv`（逐层明细）。
  恒星对应 starMap v3：dysonSpheres 0/1 链 + swarm 头强校验 + 层轨道区间兜底；
  验证（两档与游戏内一致）：599 → KiffaBoraelis(0)+Pishpai(59)、w1wold3 → 母星 1 层球 + Agena 6 层等 4 球。
  待办：swarm ~4KB 闭环、太阳帆数、假球剔除、旧档兼容、「设计总量」字段（见 SAVE-FORMAT §7.5/§9）
- ✅ **快筛并发 OOM + 精算断点续算**（2026-10-21）：`UniverseGen` 静态共享数组
  （tmp_poses/tmp_drunk/tmp_state）改线程内局部（修复多线程 CreateGalaxyLight 并发“集合已修改”大面积异常）；
  精算独立线程 `--refine-threads`（默认 8，x86 内存）；行星建模数据提取后释放（精算 OOM）；
  `Checkpoint.RefineDone` + `TopPool.Update` 支持强杀后继续未完成候选。
- ✅ **工厂段全量提取**（2026-10-23，`DspCore48\FactoryExtractor.cs`）：`--save` 自动产出
  `saveX-factory.csv`（行星/实体数/回收/**地面基地数**/单位数/bucketCount）。
  定位策略：`factoryCount + ver=10` 定位 → **顺序解析**到段尾（链长==fc 校验）→ `ver==10` 盲搜兜底；
  全部 13 个 Skip* 逐字段对照 decomp-full 重写（Belt/Monitor/Gen/Consumer/Acc/Fractionator/GDBuilder 等），
  修复清单与验证见 `SAVE-FORMAT.md §7.6`。验证：201 档 **11/11 玩家行星全提取**（404 Taygeta IV
  bases=6）、057b bases=5 与游戏一致、my-save 1 实体、**882MB 大档全流程 1.7s**。
  ⚠️ 边界：黑洞星等未登陆行星无存档数据（未扫描不落盘）→ 其黑雾基地不可提取
- ✅ **黑雾巢穴 189/189 全提取**（2026-10-23 修复，`HiveExtractor`）：旧版 201 档仅提 82 个
  （黑洞/中子星/Kiffa 全丢）——根因 ①`RFlex` 误用 LEB128（游戏 FlexibleInt 是 flag 前缀编码），
  大巢穴 pbuilders 逐条错位；②`SkipCore` 少跳 12B。修复后 201 档 189/189（旧 82 回归无损，
  新增 107：黑洞星 64=6/中子星 63=6/Kiffa 1=3），详见 `SAVE-FORMAT.md §9`
- ✅ **行星地面基地数 = hive relay 活跃投放数**（2026-10-23，`HiveExtractor` 新增 relay 提取）：
  每个地面基地由太空巢穴的 relay（`DFRelayComponent`，`baseState==1 && stage==2` 为活跃投放）
  投放创建；relay 组件池在 `dfHives` 段**全球落盘，与玩家是否造访行星无关**——未造访行星的
  基地数也能读出。`--save` 产出 `saveX-relays.csv`（hiveAstroId/relayId/targetAstroId/baseState/stage）。
  验证：黑洞行星 6401 relay 活跃=**36**（与游戏内显示一致）、中子星 6301=36、全星河活跃基地 1782；
  260 档（清理后）6401 relay=0 印证基地被清即移除；404 差异（factory bases=6 vs relay 活跃 0）
  待查 bases 池 recycle 语义
- ✅ **采集站数 / 戴森球层状态 / 未开采矿组**（2026-10-23）：factory.csv 加采集站数列（protoId 2105，
  实测 102 气巨=6）；dyson.csv 加 空层/在建层/完成层（空=无设计，完成=节点+框架全建）；
  planets.csv 加 未开采组/已开采组（模型参照排序对比，--mine 0 跳过；my-save验证母星外全未开采 ✓）
- ✅ **007/201 档资源倍率存疑已解决**（2026-10-23）：根因 = 复刻把 `isRareResource` 写死 false，
  漏了游戏原版「资源极难（≤0.1x）时 resourceCoef 额外 ×0.7（且 >1 时 ^0.8）」；修复后 0.1x 档
  模型与存档逐位一致（201 档 401 铁/铜/硅全对上），007 存档/模型 1x = 0.07 = 0.1×0.7（详 SAVE-FORMAT §9）

## 结构（2026-10-20 A2 后）

```
DspSearcher\                    仅此一个操作程序（net48 WPF + 游戏 DLL，A2 迁移）
├── Core\      SearchConfig / Filters（4 条件）/ TheoryScorer（理论储量）/ Balance（配比）/
│              Candidate / TopPool / Checkpoint（断点续跑）/ Searcher（全局队列+多线程+两阶段）
├── MainWindow.xaml（条件面板 + 进度 + 实时 top 表格 + 日志 + 开始/暂停/恢复/停止）
├── 引用 DspCore48 + 游戏 DLL（Assembly-CSharp/UnityEngine*/Newtonsoft）
└── data\balance.json（合并配比固化）

DspCore48\                      权威生成引擎（net48 + net8 双目标，源 = DspFindSeed-3.1 验证版）
├── GenApi.cs      CreateGalaxy/CreateGalaxyLight/PopulatePlanets/ModelPlanet/VeinsCsv/Save/Init
├── UniverseGen/StarGen/PlanetGen/PlanetManager(0~14 全变体)/VeinGen
├── SaveParser/SaveExport（存档解析）
├── LDB\prototypes\  主题/矿脉原型 XML（游戏 2026-10 现行版，构建复制到输出）
└── 已修复：PlatformTarget=x86（进程位宽，见 FLOAT-NOTES.md）

DspFastScan\                    快筛专用（net8 x64，自含引擎 ~1 万/s；精算/存档仍用 net48 DspSearcher）
├── Engine\   纯托管引擎（源 = DspSeedFinder 47af515：Data/Gen/Math/Random，魔改 DotNet35Random；
│              VeinGen.GenerateGroupCounts 为查表版——快筛不跑地形管线，见下方提速记录）
├── Core\     同 Searcher Core（快筛/过滤/理论评分），Balance/Checkpoint 用 System.Text.Json
├── 自包含：不引 DspCore48/游戏 DLL；原型数据 = src\data\json（构建复制）
└── CLI：--from/--to/--threads/--refine-threads/--out/--seed/--resource-multiplier/--skip-refine/
    --min-o/--near/--far/--radius/--ice-giant/--pool-size（GUI 面板条件全量传递）
    → 产出 checkpoint → DspSearcher --headless --resume <checkpoint> 续精算（net48 x86 精度）

## 2026-10-24 快筛提速（✅ 已解决）

- **根因**：DspFastScan（DspCore48 引擎）快筛 3600/s、CPU 60%；对照组 v4 快照（DspSeedFinder
  net8 引擎）实测 11764/s，且命中集合与 DspCore48 不一致 → 定位为 47af515 把
  `VeinGen.GenerateGroupCounts` 从**查表版**改成**完整建模管线**（GenerateVeins →
  PlanetModelingManager.ModelingPlanet，每行星 16 万顶点地形+矿脉）——快筛 31% 种子全量建模，
  x64 下内存可达 30GB、速度暴跌。（README 曾记录的“4f347cf 引擎 8300/s”是把 791a311b
  误记为 4f347cf；实际 4f347cf 与 791a311b 的差异在快筛分发模型，引擎侧无此差距。）
- **修复**：DspSeedFinder 引擎并入 `DspFastScan\Engine\`（纯托管，魔改 DotNet35Random 与
  DspCore48 随机流同源）；`GenerateGroupCounts` 恢复查表版（4f347cf 实现）；`DspFastScan.csproj`
  去 DspCore48/游戏 DLL 引用，自包含（数据 = src\data\json）。
- **验证**（3 区间 × 3 倍率 1/0.5/0.1 × net48 对照，32 线程只快筛）：命中 9/9 一致、
  top500 逐位一致（3 区间全同）——倍率不影响一致性（覆盖判定看组数）；
  net48 1,800/s vs 新引擎 ~10,000/s（约 5.5 倍提速）。
- **GUI 集成**：`MainWindow.OnFastScan` 全参数传递（2026-10-24 核对补齐 --refine-threads/
  --min-o/--near/--far/--radius/--ice-giant/--pool-size），面板条件不再丢失；
  FastScan 完成后 resume 用 checkpoint 内 Config 精算，条件链路闭环。

### 2026-10-24 晚：连续分发积压回归 + checkpoint 锁冲突 + 进度显示（已修）

- **连续分发积压回归（已修）**：DspFastScan 创建时误用 791a311b 连续分发（Sleep(1) 限速后生产
  仍 ~66 万/s >> 消费 ~1 万/s → 队列积压千万级、checkpoint 按分发水位每秒存 10 次 + GUI 轮询读
  同文件 → 文件锁冲突/UI 卡死，即当年 791a311b 积压 bug 复发）。修复 = FastPhase 回退块模型
  （等本块消费完再发下一块，生产=消费，实测 1.1 万/s 无损）。
- **checkpoint 锁冲突根治**：读方 `Checkpoint.Load` 必须 `FileShare.ReadWrite | FileShare.Delete`
  （`FileShare.ReadWrite` 不含 Delete 位——读句柄未共享删除时写方 `File.Delete` 必 IOException，
  曾两次中断：12:10/12:54 各跑 1800 万时撞上）；写方 `Save` = 写 tmp → Delete → Move 原子替换 +
  IOException 重试 5 次（实测 `File.Move(overwrite:true)`/`File.Replace` 在目标被读句柄打开时抛
  UnauthorizedAccessException，不可用）。并发验证：GUI 模拟轮询 105 连读 0 错。
- **进度显示**：快筛/精算速率+ETA（快筛=已消费水位 Stats.Scanned，精算=独立秒表 _refineSw）；
  进度条用分发水位 _dispatched（Stats.Scanned 每 8192 合并有滞后，纯用它进度条不动）；
  FastScan 轮询 `RefreshFromCheckpoint` 接进度条 + mtime/长度守卫（checkpoint 未变跳过
  反序列化，~27ms→~9µs）；FastScan 中断退出时 Exited 按 checkpoint 如实提示并自动接力。
- 附带清理：`Engine\Gen\PlanetAlgorithm.cs` 删 47af515 遗留 [TIGRP] 调试打印。

DspFindSeed-3.1\DspFindSeed\      验证基座（A2 后退为验证用，不参与日常）
├── PlanetManager\   PlanetAlgorithm.cs（基类+0~14 全变体）/ PlanetModelingManager.cs
├── SaveExport.cs    --save 存档解析导出（恒星/行星级 CSV）
├── Program.cs       CLI：--seed/--stars/--out/--save/--real-veins/--veins-csv/--gas/--zh
└── bin\Debug\prototypes\  主题/矿脉原型 XML
```

> `DspSeedFinder`（net8 纯复刻）已于 2026-10-20 退役并删除（git 历史可恢复）；
> 本文“2026-10-20 复刻对拍修复”一节为该阶段的历史记录。

数据 `src\data\json\` 随构建复制到输出。移植说明：恒星命名与蜂巢(hive)未移植（独立随机流，不影响行星结构）。

## 2026-10-25：S2 galaxy.json（VIEWER-PLAN 主线）

- ✅ **初始生成 → galaxy.json**（嵌套真相源）：`DspSearcher --headless --seed N --json <path>`，
  结构 `meta + stars[]（含 planets[]，行星内嵌 veins[]）+ plan[] + save`；`DspSearcher\GalaxyJson.cs` 序列化。
- ✅ **SingleSeed 重构**：抽 `ModelGalaxy`（建模+矿脉汇总，RefineOne/SingleSeed 共用），单种子一次生成不再二次；
  galaxy.json 从建模 galaxy 输出（landPercent 真实陆海比）；支撑/矿脉与精算逐位一致（14629840/39645113 双种子验证）。
- 待补：`themeNameEn`（引擎无英文名源）

## 2026-10-25~27：S3 规划器（✅ 已完成，PLANNER-DESIGN/PLANNER-IMPL）

- ✅ **Phase A 收口**（A1-A5）：49 制造品（母星 6 + 共星 19 + 附属 5 + 独立 19）+ 18 采集品；
  **40 槽**（独立 27 + 共星 13）+ 母星固定；可建规则（气巨/冰巨/O 星不可建、6 低开阔度主题只能分担、母星保护区）
- ✅ **Phase B 定稿**（v2）：趟当量距离口径（同行星 0 / 同系 1 趟 / 跨系 d_ly×1 趟，不加固定项）；
  产量 = 总需求 ÷ 槽数均分；槽内自用由 `recipes.csv` 自动推导；附属品固定槽 0；
  **净额模型**（产量−本槽消耗）；多源→多汇精确运输问题；氢 782,016/min 纯采集，仅作偏好项
- ✅ **C1 推流 + 槽位净额**：铁矿石 1,534,942/min（与 combined 差 0.28%）；40 槽与 A4 表一致
- ✅ **C2/C3 候选池 + 距离 + 运输**：84272982 → 池 50 颗（可用 49，低开阔度 6）；同系 1 / 跨系 ly；
  运输贪心+2-opt 与精确解差 **0.002%**（B 验证）
- ✅ **C4 退火**：多链并行 + 早停（`--stagnation`）+ checkpoint 续跑（`--resume`）+ 精英池共识
- ✅ **C4b 锁定布局**：`plan_locked.json`（按行星 id）；锁定 6 槽仍收敛同解
- ✅ **C5 LNS 局部精修**：全规模 CP-SAT 实测不可行（600s 仅 19.4M、下界极松、无断点）；
  改为固定 28 槽精修 12 槽（300s → 12,674,742）；首轮 4 窗口无改进 → window=12 局部最优
- ✅ **C6 写回**：`galaxy.json` plan 区块（40 槽 + meta，自动备份）+ `plan-report.txt`（槽位/物流明细/氢物流）

**结果（seed 84272982）**：最优 **12,672,286**（中间品 12,432,389 / 母星 129,060 / 氢 110,838），
1-opt/2-opt 无改进 + 24 链收敛 + window=12 LNS 无改进。

**命令**：`DspPlan demand|eval|search|verify|verifyflow|cpsat|lns|apply`
（`--seed/--threads/--stagnation/--locked/--pool-limit/--resume/--window/--per-solve`）

**下一步**：S5 渲染（HTML 表格 + 3D）

## 2026-10-27：S4 存档融合完成（`--merge`）

- **模型修复**（核对时发现）：`DspCore48\PlanetManager\PlanetAlgorithm.cs` 的 PA7/11/12/13 四个变体
  `isRareResource`/`infiniteResource` 写死 false（10-23 只修了基类）→ ≤0.1x 档这些主题储量偏高 30~45%
  （实测比例 = `0.7 × coef^-0.2`，正是「漏 ×0.7 与 coef^0.8」）。已修为与基类同口径；
  重新生成 seed 84272982（0.1x）后存档对拍 **84/85 逐位一致**（仅母星 103 出生点特殊矿）。
  ✅ 已同步修 `DspFastScan` 引擎（5 处 isRareResource/infiniteResource + amount 倍率 + TheoryScorer 极难修正 + MakeDesc 自动推导）：
  0.1x 档单种子复测 vs net48 中位比值 1.005（P5/P95 = 0.87/1.20，属跨引擎浮点长尾）；修复前 302 类行星系统性 +29%
- **S4 merge**（`DspSearcher\Merge.cs`，`--merge <dsv> --galaxy <json>`，直接读 .dsv 一步到位）：
  按行星 id 写入 `stars[].name` / `planets[].name`（存档中文名）、`scanned`、`landPercent`、
  `veins[].groups/currentAmount`、`meta.save`；不动 plan 与 initialAmount；备份 `.bak-merge` + `merge-report.txt`
- **实测（my-save，seed 84272982，0.1x，tick 8076）**：已扫描 **85/243**；名字 64 恒星 + 243 行星全部落位；
  84/85 储量与初值一致；组数冲突 2（母星 103 石/油）；未扫描 158 颗保持初值；plan 40 槽 12,672,286 不受影响；幂等（除 mergedAt）

## 2026-10-26：配比口径修正（10 万宇宙矩阵/min 的 ×4）

- **根因**：`tools\matrix_100k.py` / `tools\merge_balance.py` 把“25,000 台产线”误当成“25,000 个/min”，所有矩阵链输出为目标值的 1/4
- **修复**：两脚本系数 25,000 → 100,000，重新生成 `data\matrix-100k.md`、`data\combined-balance.md`；`DspSearcher\data\balance.json` 同步（铁矿 397,808 → **1,539,248/min**）
- **对搜索器**：排序 / 过滤 / 命中率不变；`支撑小时` 与 `6ly均衡(h)` 两列仍按旧口径（报告值 ÷ 4 = 10 万口径），历史 search-out 不重跑
- **对规划器**：氢需求修正 **782,016/min**（纯采集；旧 102,000 漏了卡西米尔 393,216），副产口径净采集 258,538；重氢 260,800/min；全星系气巨氢产能约 48,863/min（缺口大，Phase C 需口径决策）

## 2026-10-28~29：S4b 字段扩展 + S5 渲染（✅ 已完成，见 `VIEWER-IMPL.md`）

- **S4b**（`GalaxyJson.cs` + `Merge.cs` 扩展）：行星加 `radius`/`waterItemId`/`spaceLevel`（开阔度=空间档）/`collectors`/`hasFactory`/`blackHoleBases`(本地)；
  恒星加 `hiveCount`/`dyson{layers,empty,building,done,rocketsInFlight}`；`meta.save.tinders[]`（含 ETA）。`blackHoleBasesRelay` 为 relay 口径
- **S5**（新工程 `DspView`，net8 CLI + `--serve`）：27 列平铺表格（恒星 rowspan 合并 / 开阔度 / 规划可编辑 / 备注 / 黑雾色阶 / 14 矿 / 3 气体）
  + 3D 星系视图（光谱色/光度） + 恒星系视图（轨道/相位/行星半径/卫星/规划标签/详情卡/ESC 回退）
  （10-31 重做后 6ly 壳/规划环/巢穴环已移除，见 `VIEWER-IMPL.md` §9）
  + 表格↔3D 联动 + 火种预警条 + 深链 `#star=`
- **`--serve`（HttpListener）**：`GET /` 静态、`GET /api/galaxy`、`POST /api/notes`（规划文本写回 `planNote` + `.bak-note`）
- **2026-10-29 UI 调整**：3D 显隐开关（关闭时表格占满全宽，`localStorage` 记忆）；恒星 3 列合并为 rowspan 单元格
  （1 行星只显名 / ≥2 行星显示 名+类型+光度）；黑雾列改 6 级实色（按巢穴数 1~6）；恒星列+行星名列横向滚动冻结
- **修复（10-29）**：`init3D` 双重初始化 → 两个 canvas 叠加（吞掉旋转/缩放/悬停/双击、撑高侧栏挤掉详情面板）
  → 幂等守卫 + 清残留 canvas + canvas 绝对定位 + 单次初始化（`apply3DLayout` 统一入口）；双击表格时 3D 关闭会自动打开
- 实测（my-save）：`--serve` 200、`/api/notes` 写入成功、Edge headless 截图确认 243 行表格 + 两套 3D + 详情/图例面板
- **2026-10-29 工厂段修复（已解决）**：982 刚开局档 `FactoryExtractor` 池错位 → 四个根因（bases 池 ObjectPool 槽位标记 /
  `SkipGDBuilder` 真结构 / prebuild 非空元素后的 16 个连接标志 / `TryHashProbe` 的 hashPool 上限 10 万→500 万）。
  my-save（931MB）现在 **100 个工厂**：母星 103（42,822 实体）、Pishpai I 号星 6001（73,425 实体，2104 星际物流站=7，
  与游戏内逐项一致）、6002（57,039 实体）；回归 982 / my-save / 057 均不变
- **大档存档入口 `DspSave`（net8 x64）**：`--save` / `--merge`（复用 `Merge.cs` 源码）；x86 的 DspSearcher 读 931MB 会 OOM，
  精算仍必须 x86。已加入 `DspSearcher.sln`（现 5 工程）
- **2026-10-30 显示修复（四项）**：
  1. **巢穴存活**：`HiveExtractor` 提取 cores/builders/nodes/replicators/turrets/units 池计数 + rootEnemyId + pbuilders 活跃数，
     按 `EnemyDFHiveSystem.isAlive` 判活；merge 时 `hiveCount` 显式归零 → my-save 巢穴已清显示 0（201 档 162 个活巢穴正常）
  2. **戴森球层状态**：只区分空层/有建造层（旧实现把「节点+框架全建」判为完成，壳可能未打完）；`dyson={layers,empty,built,rocketsInFlight}`
  3. **玩家重命名**：解析 `GamePrefsData.astroNameOverride`（恒星 key=starId×100）→ my-save 38 条（太阳/地球/石墨烯/白糖…），`nameSource="override"`
  4. **资源显示**：矿量改用工厂 `veinPool`（**当前值**，已开采会减少；旧用的是已扫描段的生成初始值）；
     按 `minerCount` 拆 `plannedAmount`（蓝）/`unplannedAmount`（绿），全 0 不显示，`<初始×10%` 已规划标红；
     气体列（氢/重氢/可燃冰气）= 每站速率×40站×60s，冰巨 item = **1011 可燃冰**（不是 1123 石墨烯）
- **2026-10-31 3D 重做**：恒星外观（菲涅尔白热核心 + 日冕；黑洞紫红光球 / 中子星紫蓝 / 红巨星大球湍流 / O 星青蓝）、
  行星按 realRadius sqrt 压缩（气巨 ×10）、轨道 sqrt 压缩 + 环带径向渐隐 + 110° 轨迹线、
  恒星名标签（出生星 + 有已建行星）；参数见 `VIEWER-IMPL.md` §9
- **剩余**：仅视觉人工核对（轨道倾角/经度方向与游戏一致、恒星系内标签重叠），无已知代码缺陷

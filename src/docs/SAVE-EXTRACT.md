# 存档数据提取规格（SAVE-EXTRACT）

> 存档二进制结构细节（头部/GameData/黑雾/组件池）见 **`SAVE-FORMAT.md`**（权威参考，知识沉淀）；
> 本文档是**需求视角**：7 项数据需求 → 来源结构/成本/状态。

目的：行星维度全局规划工具的数据需求 → 每项数据的提取来源（游戏反编译权威结构）、成本、状态。
参照实现：`DspCore48\SaveParser.cs`（v2：流式 + 多线程 + 权威头部序列）。

## 数据来源两条路

1. **存档提取**（当前剩余状态）：按游戏 `GameSave.Load → GameData.Import → 子结构.Import` 权威序列
2. **seed 生成**（初始/理论状态）：`DspSearcher --seed N`（星表/行星/地形/初始矿脉）——与存档同 seed 可对齐

规划工具两类都用：生成给"初始/理论"，存档给"当前/剩余"。

## 存档权威读取路径（SaveParser v2 已实现）

```
GameSave.Load:
  VFSAVE(6) + fileSize(8) + headerVersion(4) + [>=7: sandbox/peace(2)]
  + 游戏版本(3~4 int) + tick(8) + 保存时间(8) + 缩略图(4+len)
  + [>=5: AccountData(4+4+8+s7) + clusterGeneration(8)]
GameData.Import:
  gdVersion(4) + [>=4: patch] + [>=3: account(4+4+8+s7)]
  + gameName(s7) + gameDesc.Import + gameTick(8) + [>=7: achievement]
  + preferences(>=1) + history + [>=13: galaxy.ImportRuntimeData(已扫描行星)]
  + statistics + player + factoryCount(4) + galacticTransport + galacticDigital
  + trashSystem(>=5) + factory×factoryCount + dysonSpheres + [>=10: spaceSector(黑雾)]
```

SaveParser v2 已覆盖：头部全字段 + gameDesc（seed/starCount/资源倍率/themeIds/lcid）+ 黑雾战斗设置 +
`ImportRuntimeData` 前的定位（gameTick 后 searchFrom）+ **多线程分块扫描** ImportScannedData 段。

## 逐项数据规格

### 1. 坐标 / 距离（✅ 已有）

- 恒星坐标：`StarData.position`（seed 生成，不需存档）；存档 runtime 有 `uPos`（SpaceSector astros）
- 行星位置：轨道参数（`orbitRadius/orbitInclination/orbitLongitude/orbitPhase`，seed 生成）
- 距离：恒星间 = position 差；行星间 = 恒星距离 + 轨道半径近似（工具内计算）
- **来源**：seed 生成即可（无需存档）；存档侧可在 `spaceSector.astros`（含 uPos double）核对

### 2. 黑雾（行星基地 / 太空巢穴 / 火种）

**来源**：`spaceSector.Import(r)`（GameData.Import 尾部，gdVersion>=10）

- ✅ **hive 摘要（已实现，`DspCore48\HiveExtractor.cs`）**：
  - 按 SpaceSector.Import 权威结构定位 dfHives 段（**19884 魔法标记**）+ 完整 EnemyDFHiveSystem.Import
    （头部 + 9 组件池（DataPool/ObjectPool）+ idleRelay/idleTinder + formation/evolve/hatred 软跳过）
  - 输出 `saveX-hive.csv`（hiveAstroId/类型/恒星id/行星id/seed/realized/isEmpty/ticks/在途火种/本地火种）
    + `saveX-tinders.csv`（火种明细：origin/target hive、direction、stage、uSpeed）
    + `saveX-relays.csv`（relay 投放明细：hiveAstroId/relayId/targetAstroId/baseState/stage）
  - **验证（2026-10-23 后）**：201 档 **189/189 全提取**（旧版仅 82；黑洞/中子星/Kiffa 全部入 csv，
  回归无损，见 SAVE-FORMAT.md §9）；113-Z01-007（第一波防御）146 个、my-save（和平）0 个
  - **地面基地数（2026-10-23 新增）**：每行星基地数 = dfHives 段 relay 记录数
    （`targetAstroId` 聚合，排除未投放的 target=0/bs=0/st=254 待命项），**全球落盘、与玩家造访无关**——
    未造访行星（如黑洞 6401）也能读出。**语义已验证 = 游戏 UI「当前共有 N 个行星基地」**：
    Taygeta IV 存档 relay 4 条（bs=2）→ UI 显示 4（用户实测，实际星球 6 基地含 2 个刚建的
    新基地——新基地的 relay 尚未注册/不计入）；黑洞 6401=36、中子星 6301=36、全星河 1785；
    基地被清 → relay 移除（260 档 6401=0）；404 的 factory bases=6（含建造中、未减 recycle）
    与 relay 4 的差异即新建基地；黑雾基地被摧毁后留下地核钻井（可建地热电站/地基平整）
  - **远程/本地双口径（用户实测确认）**：星图查看 = relay 池计数（轻量、全球落盘）；
    登陆该星球后 = 本地 `enemySystem.bases` 全量（含正在建造的新基地）——两套提取各对一口径：
    `relays.csv` = 远程视角（6401=36、404=4），`factory.csv` 地面基地数 = 本地视角（404=6）
  - 调试要点：num2（pbuilders）上限 512→10000（大型巢穴 1347）；EnemyBuilderComponent 详情
    13×int+…（勿用 20×int）；EvolveData 10×int；formation port 可至 UInt16 上限（软失败不致命）
- ✅ **火种明细（DFTinderComponent）已实现**（tinders 池，Origin/Target/Stage/USpeed）
- ✅ **地面基地（已实现 2026-10-23，`FactoryExtractor.cs`）**：`PlanetFactory.enemySystem` 前段
  顺序解析已经打通，产出 `saveX-factory.csv` 的**地面基地数**（bases.cursor-1）——「母星 5 基地」等
  可直接从该列读；bases 明细（seed/等级/仇恨）尚未展开，后续扩展

### 3. 行星是否有建筑（✅ 已实现 2026-10-23）

- **来源**：`PlanetFactory.Import`（GameData.Import 的 factory 循环，`factoryCount` 前有行星 id）
- **判断**：`entityCount = entityCursor - entityRecycleCursor - 1 > 0`（PlanetFactory.cs:235）
  - `entityCursor > 1` 即该行星建过建筑（拆光则 entityRecycleCursor 抵消）
  - 更细：`factoryCount` 总工厂数 + 每 factory 归属行星（Import 参数 i/planet）
- **实现**：`DspCore48\FactoryExtractor.cs`（2026-10-23），输出 `saveX-factory.csv` 的**实体数**列
- **成本**：中低（只需读到 entityCursor/entityRecycleCursor，跳过实体细节）

### 4. 气巨/冰巨采集站数量（✅ 已实现 2026-10-23）

- 采集站 = **OrbitalCollector**（轨道采集器）实体，**protoId = 2105**（`ACH_OrbitalCollectorCount` 权威）
- 数量 = factory 实体池中 protoId==2105 的实体数（`FactoryExtractor.ProtoIdCount[2105]`）
- 上限：气巨可建站数 = `GasGiant 环绕轨道槽位数`（游戏上限 ~40，搜索器已按 40 估）
- "建满" = 采集站数 == 该气巨上限
- 实现：`factory.csv` 加**采集站数**列；实测 102 气巨=6、403=3
- 局限：仅玩家造访过（有 factory 段）的行星可读

### 5. 矿脉：当前总储量 / 未开采储量（✅ 已有 + ✅ 未开采组 2026-10-23）

- **来源**：`galaxy.ImportRuntimeData → planetData.ImportScannedData`（SaveParser v2 已实现）
  - `landPercent/veinBias/birthPoint/birthRP0/1 + veinGroups[]`
  - `VeinGroup{type, pos, count, amount}`：**count=组内矿点簇数，amount=组当前剩余总储量**（开采后减少）
- **当前总储量** = Σ amount（按矿种）——已有
- **未开采矿组（已实现 2026-10-23）**：对已扫描行星建模（同 seed 同倍率）→ 模型按 `(type, groupIndex)`
  聚合组总量 → 与存档组 amount 排序对比（不依赖组顺序，对拍过逐位一致故用 ==）→
  `planets.csv` 加 **未开采组/已开采组** 两列；`--mine 0` 跳过
  - 验证：my-save初始档除母星 104（玩家建矿）外全部未开采 ✓；后期档（_lastexit_ 6469 组全已开采）
    ——黑雾基地全球采矿所致，判定口径待用户游戏内数据校准
  - **用户实测对拍（2026-10-23，新存 my-save）**：宝瓶座δ - 2号星（102）硅 存档 9311813 vs 面板 9311790
    （差 23 = 进行中采集流失，用户确认）；其余矿 100% 未开采与面板一致；母星 104 除外（出生点特殊矿）
- **局限**：模型参照基于 seed 重生成；0.1x 资源极难档的 ×0.7 矿量修正已补（2026-10-23，
  isRareResource），存档倍率异常时判定仍可能失真（判定口径待用户数据校准）

### 6. 空间大小（蓝图放置：矿点外大片连续空间）（✅ 已实现 2026-10-20）

- **矿点位置用存档**：`VeinGroup.pos`（组中心，单位向量，**游戏权威真值**——不受模型 seed 误差影响）
- **实现**（`DspCore48\SpaceAnalyzer.cs`）：矿组聚类（3km 归簇）→ 簇圈禁建（半径+500m 边距）
  → 低分辨率等经纬网格（720×360 ≈ 2km/格）泛洪 → 可建比例
- **输出**：`空间档`（1~10，**10=无矿/最空旷**；每 10 矿组降 1 档：0-10→10、11-20→9、…91+→1）
  ——矿点占球面比例极小（<0.5%），按面积分档无区分度，故按矿组数分级，稳定可比
- **CLI**：`--save` 默认计算空间档；`--space 0` 跳过（大档加速）
- **成本**：低（220 颗行星含空间计算 <3s，无需多线程）

### 7. 戴森球完成比例（✅ 2026-10-21 扫描+解析；2026-10-23 层状态简化）

- **来源**：`dysonSpheres`（GameData.Import 后段）：每星 ReadInt32(0/1) → `DysonSphere.Import`
- **实现**（`DspCore48\DysonExtractor.cs`）：1212 魔法标记特征扫描 → 流式解析
  `DysonSphere.Import` 后半（layerCount + DysonSphereLayer 三池 + rocket + nrd + mask）
- **已产出**：`saveX-dyson.csv`（每球 层数/空·在建·完成层/节点·框架·壳 总与已建/在途火箭/恒星匹配说明）
  + `saveX-dyson-layers.csv`（逐层 轨道半径/净数/已建）
- **层状态定义（2026-10-30 修订）**：
  - `saveX-dyson.csv` 仍输出「空/在建/完成」（完成 = 有设计且节点+框架全部建成；在建 = 其余）
  - **`galaxy.json` 的 `dyson` 只区分空层/有建造层**：`{layers, empty, built, rocketsInFlight}` ——
    壳体填充度需读 `DysonShell.nodecps`（当前未提取）。现场：my-save Pishpai 5 层节点/框架全建，
    但游戏里只有 1 层真完成（壳体还在打太阳帆）→ 旧口径会误报「完成」
- **验证（113-Z01-599）**：唯一真球 = Pishpai（O 型，index 59），10 层（5 有节点 5 空），
  层轨道 65250~74400，节点 1210/框架 2320/壳 1120 —— 与游戏内面板一致（用户确认）
- **待办**：swarm 段 ~4KB 零填充未闭环；太阳帆数（不在当前需求内）；旧档兼容（用户明确不需要）
- **字段**：
  - `layerCount`（层数）；每层 `nodeCursor/nodeRecycle/frameCursor/frameRecycle/`
    `shellCursor/shellRecycle`（净数 = cursor - recycle - 1）
  - 已建：节点 sp>0、框架 spA+spB>0（池内逐条）
  - `rocketCursor/rocketRecycleCursor`（在途火箭）；`sailCursor`（太阳帆，在 swarm 内，未取）

## 建议实现顺序（用户定稿 2026-10-23）

1. ✅ S1 存档解析基础（头部 + gameDesc + scanned 矿脉 + 黑雾战斗设置）
2. ✅ A 黑雾提取（hive 摘要 + tinder + relay 地面基地数）
3. ✅ B 建筑检测（factory 实体数）——**只要"有建筑=已开发"，不解析工厂明细**（用户明确）
4. ✅ C 戴森球层状态（空/在建/完成 简化）
5. ✅ D 采集站计数（protoId 2105）
6. ✅ E 未开采矿组（模型参照排序对比）
7. ✅ F 空间大小算法
8. 🔲（不需要）玩家工厂/星际物流明细——仅"该星球有建筑"即可
9. 🔲（不需要）旧版存档兼容——只用于当前版本游戏


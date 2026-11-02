# DSP 存档二进制格式（SAVE-FORMAT）

权威参照：`ref\ref\decomp-full\` 反编译源码（GameSave / GameData / GameDesc / SpaceSector /
EnemyDFHiveSystem / PlanetFactory / 各组件类）。读写对称，读取顺序即写入顺序（BinaryReader/Writer）。

实现：`DspCore48\SaveParser.cs`（头部+已扫描行星）、`DspCore48\HiveExtractor.cs`（黑雾太空巢穴）、
`DspCore48\SaveExport.cs`（输出 CSV）。早期 python 解析见 `src\tools\parse_dsv.py`（历史）。

## 0. 总览

存档 = 顺序二进制流：`头部 + account + GameData（星系/行星/工厂/戴森球/黑雾）`。
已逆向覆盖：头部、GameData 前段（gameDesc/scanned 行星）、黑雾太空巢穴（完整）、
地面基地（结构已定位未实现）。玩家工厂/戴森球/星际物流等大段仅定位未解析。

## 1. 头部（GameSave.SaveCurrentGame / LoadCurrentGame）

```
VFSAVE（6 字节魔法，0x56 46 53 41 56 45）
fileSize（Int64，必须 == 文件长度）
headerVersion（Int32，<4 拒绝）
[ver>=7] isSandboxMode + isPeaceMode（2×bool）
游戏版本 Major/Minor/Release（3×Int32）；[ver>=6] Build（Int32）
gameTick（Int64）
saveTime（Int64，DateTime.Ticks）
缩略图 PNG：length（Int32）+ bytes
[ver>=5] AccountData（4+4+8+s7：0/platform/userId/userName）+ clusterGeneration（UInt64）
→ GameData.Import
```

## 2. GameData.Import 序列（`GameData.cs:784`）

```
gdVersion（Int32，当前 13）
[>=4] patch（Int32）
[>=3] AccountData（同头部）
gameName（s7 字符串）
gameDesc.Import（见 §3）
gameTick（Int64）
[>=7] gameAchievement.Import
[>=1] preferences.Import（相机/刷子等）
history.Import（生产统计，2537 行实现——最大段，跳过的替代=特征扫描）
[>=13] galaxy.ImportRuntimeData（已扫描行星，见 §4）
[>=2] hidePlayerModel/disableController（2×bool）
statistics.Import（游戏统计）
player（planetId Int32 + Player.Import）
factoryCount（Int32）+ factory×factoryCount（PlanetFactory.Import，见 §8）
galacticTransport.Import（星际物流）
[>=12] galacticDigital.Import
[>=5] trashSystem.Import（垃圾桶）
dysonSpheres：count（Int32，==starCount）+ 每星（Int32 0/1 → DysonSphere.Import）
[>=10] spaceSector.Import（黑雾，见 §5）
[>=6] history 补充段
（后续：更多兼容段）
```

## 3. gameDesc.Import（`GameDesc.cs:273`）

```
num（Int32，当前 9）
[>=3] creationTime（Int64）
[>=5] [>=6: Major/Minor/Release/Build 4×Int32 | 否则 Build 1×Int32]
galaxyAlgo、galaxySeed、starCount（3×Int32）
[>=10] starNameLCID（Int32；0=英文 2052=中文）
playerProto（Int32）
[>=2] resourceMultiplier（Single）
[>=1] savedThemeIds（count + Int32×N）
[>=4] achievementEnable（bool）
[>=7] isPeaceMode + isSandboxMode（2×bool）
[>=8] combatSettings（黑雾难度：ver Int32 + 9×Single）
  aggressiveness / initialLevel / initialGrowth / initialColonize / maxDensity /
  growthSpeedFactor / powerThreatFactor / battleThreatFactor / battleExpFactor
[>=9] goalLevel（Int32）
```

**倍率**：`resourceMultiplier` 落盘；`oilAmountMultiplier` 是**计算属性**不落盘
（`resourceMultiplier ≤ 0.1001 → 0.5，否则 1`）。生成矿脉时**普通矿 × resourceMultiplier、
原油 × oilAmountMultiplier**（`PlanetAlgorithm.cs:412` 等 5 处）。

## 4. 已扫描行星（galaxy.ImportRuntimeData → ImportScannedData）

`GalaxyData.cs:299`：

```
num（Int32，runtime 版本）
循环：astroId（Int32，<0 即 -1 结束）→ planetData.ImportScannedData(r)
[>=1] 循环：astroId（-1 结束）+ vegeCollectionActive（Int32 0/1）
（后续：温度/风力等行星 runtime）
```

`ImportScannedData`（`PlanetData.cs:1766`）：

```
0（Int32 占位）
landPercent（Single）
veinBias（3×Single）
birthPoint（3×Single）
birthResourcePoint0（3×Single）
birthResourcePoint1（3×Single）
veinGroups：count（Int32）+ VeinGroup×（count-1）[索引 0 占位不落盘]
```

`VeinGroup`（32 字节，`VeinGroup.cs`）：

```
0（Int32）+ type（Int32，EVeinType 1-14）+ pos（3×Single，单位方向向量）+ count（Int32，矿点簇数）+ amount（Int64，当前剩余总储量）
```

**注意**：存档只含**已扫描行星**（玩家登陆过的）；amount 是**当前剩余**（开采后减少）；
未扫描行星无此段（用 seed 生成模型补齐）。行星 id = 恒星 id×100 + 序号。

## 5. SpaceSector.Import（黑雾容器，`SpaceSector.cs:724`）

```
num（Int32）+ astroCapacity（Int32）+ astroCursor（Int32）
astros[]（i=1..astroCursor-1）：id（Int32，>0 时）+ type + parentId + uPos(3×Double) +
  uPosNext(3×Double) + uRot(4×Single) + uRotNext(4×Single) + uRadius
enemyCapacity + enemyCursor + enemyRecycleCursor + enemyPool[i].Import + enemyRecycle[]
enemyAnimPool[i]（5 字段×N）
craftCapacity + craftCursor + craftRecycleCursor + craftPool[i].Import + craftRecycle[] + craftAnimPool[]
[>=1] spaceRuins.Import
skillSystem.Import
dfHives 数（Int32，≤65535）
按恒星（0..dfHives 数-1）：
  while (ReadInt32() == 19884) { EnemyDFHiveSystem.Import(r) }   ← 19884 魔法标记，同恒星多 hive（sibling 链）
```

**太空巢穴 astroId**：≥1000000（`dynamicAstroIdBase = 1000000 + starCount*8 + 2`）；
所属星 = `(astroId - 1000000 - 1) / 8 + 1`；行星地面基地 = 行星 astroId（<1000000，**不在本段**，见 §8）。

## 6. EnemyDFHiveSystem.Import（太空巢穴，`EnemyDFHiveSystem.cs:1272`）

```
num（Int32，当前 3）+ hiveAstroId + seed + rtseed + pbuilders 数
pbuilders[]（GrowthPattern_DFSpace.Builder，见 §7）
realized + isEmpty（2×bool）+ ticks + turboTicks + turboRepress（3×Int32）
matterStatComplete（bool）+ matter 4×Int32 + rootEnemyId（Int32）+ isCarrierRealized（bool）
tindersArrivingInTransit（Int32，在途火种数）
[>=1] lancerAssaultCountBase（Single）；[==1] 额外 Int32
[>=3] relayNeutralizedCounter
组件池（9 个，见 §7）：builders / cores / nodes / connectors / replicators / gammas /
  turrets / relays（ObjectPool！）/ tinders / units
idleRelayIds：容量 Int32 + count Int32 + count×Int32
idleTinderIds：同
forms（3×EnemyFormation）+ evolve（EvolveData）+ hatred + hatredAstros（2×HatredList）
```

**提取字段（`HiveExtractor` → `saveX-hive.csv`）**：hiveAstroId / 类型 / 恒星id / 行星id / seed / realized / isEmpty /
ticks / 在途火种 / 本地火种 / **rootEnemyId** / **cores / builders / nodes / replicators / turrets / units**（池元素数）/
**pbuilders**（活跃建筑数：instId>0 且索引≥2）/ **存活**。

存活判定（`EnemyDFHiveSystem.isAlive` 精简）：`!isEmpty && (!realized || rootEnemyId>0 || pbuilders>0
|| cores>0 || builders>0 || nodes>0 || 活跃relay>0 || 本地火种>0)`。
**已摧毁的巢穴仍保留在存档（realized=True 也照样在）**，必须靠这些池信号判活，不能只看 realized/isEmpty。

## 7. 组件池与子结构

### DataPool<T>.Import（`DataPool.cs:259`，组件池通用）

```
capacity + cursor + recycleCursor（3×Int32）
for i=1..cursor-1：buffer[i].Import（组件自身读 id）
recycleCursor × Int32
```

### ObjectPool<T>.Import（`ObjectPool.cs:372`，relays 用）

```
capacity + cursor + recycleCursor
for i=1..cursor-1：id（Int32）→ id≠0 时 id==i 校验 + buffer[i].Import
recycleCursor × Int32
```

### 组件 Import 模式（`ReadByte flag + id + [id>0: 详情]`）

| 组件 | id 类型 | id>0 详情 |
|---|---|---|
| EnemyBuilderComponent | Int32 | 13×Int32 + Int16 + 2×Int32 + 4×UInt16 |
| DFSCoreComponent | UInt16 | 12×Int32 + Single + [flag>=1: Int32] |
| DFSNodeComponent | UInt16 | 2×Int32 |
| DFSConnectorComponent | UInt16 | 2×Int32 + 2×Single + Int32 + 2×UInt16 |
| DFSReplicatorComponent | UInt16 | 2×Int32 + Int16 + Byte + UInt16 + Int32 + 2×UInt16 + Byte + 3f + 4f + 3f + 2×UInt16 |
| DFSGammaComponent | UInt16 | 2×Int32 |
| DFSTurretComponent | UInt16 | 2×Int32 + Int32 + 3×Byte + Int32 + 3×Single + Single + Int32 + 4×Single + 3×Int32 |
| DFRelayComponent | UInt16 | 见下（含 EvolveData + 6×DFCarrierData） |
| DFTinderComponent | UInt16 | Int32 + Int32 + Int16 + 2×Int32 + 2×Int16 + 2×Single（origin/target/stage/uSpeed/param0） |
| EnemyUnitComponent | FlexibleInt | 3×Flex + 2×Int16 + 2×UInt16 + Byte + Flex + 3×Int16 + Int16 + 3f + TargetQueue + HatredList + [>=1: 2×Int32] + [>=2: Single] |

DFRelayComponent：flag + id + [id>0: 2×Int32 + Int16 + 2×Int32 + 3×Single + Single + SByte + Int16 + Int32 +
EvolveData + Int16 + SByte + SByte + Single + Single] + carrier 数（Byte，≤6） + 6×DFCarrierData
（default 也读）+ 3×Int32 + 3×Single + Int32 + [flag>=1: 2×Int32 + 3×Single + Single]

### 子结构

- **GrowthPattern_DFSpace.Builder**：flag + protoId(Int16) + instId(Flex) + [>0: Int16] +
  [!isConn(protoId 8109/8110): Flex savedWorkProgress] + pos(3f) + rot(4f) + [isConn: Byte+Single] +
  matterNeeded(Int32) + matterSpeed(Int16) + conns(Int16) + conns×Int16 + rank(Byte)
- **EvolveData**：flag + 10×Int32（level/expl/expf/expp/exppshr/threat/maxThreat/threatshr/waves/waveTicks）+
  [>=1: Int32] + rankBase(Int32)
- **TargetQueue**：flag + count(Byte) + [>0: 3f] + [>1: 3f] + 3×Int32
- **HatredList**：flag + 16×Int32（max/h1..h6/min × target+value）
- **DFCarrierData**：flag + stage + direction + matter(Int32) + energy(Int32) + upos(3×Double) +
  urot(4×Single) + speed(Single) + nearStart(bool) + param0/1(2×Single) + hp(Int32)
- **EnemyFormation**：flag + portCount(UInt16) + vacancyCursor(UInt16) + portCount×Byte +
  [port<=255: vacancyCursor×Byte | 否则×UInt16]

**FlexibleInt（ReadFlexibleInt，`IOHelper.cs:169`）**：7-bit 变长有符号？int（读作无符号累积，
移位 ≤28）。

## 7.5 戴森球（dysonSpheres 段，`DysonSphere.Import`）

**段位置**：GameData.Import 的 dysonSpheres 循环（`GameData.cs:965`）：

```
count（Int32，== galaxy.starCount）
for 每星：0/1（Int32，0=无球 / 1=DysonSphere.Import）——交替布局（0/1 与 Import 穿插）
```

**DysonSphere.Import**（`DysonSphere.cs:1852`）：

```
num（Int32 版本；0=ResetNew 空球） + randSeed
DysonSwarm.Import（见下） + 1212 魔法校验（Int32，特征扫描锚点！）
layerCount + num2（层上限+1）→ 逐位：layerId（0=空 / i）→ DysonSphereLayer.Import
rocket：capacity + cursor + recycle + rocketPool（92B/条定长）+ recycle×Int32
autoNodeCount + 循环（Int32 >0 → layerId+nodeId）
[>=2] nrd：capacity + cursor + recycle + nrdPool（44B/条定长）+ recycle
[>=3] 4×Int32（render mask）
```

**DysonSphereLayer.Import**（`DysonSphereLayer.cs:230`）：

```
num + id + orbitRadius(4B) + orbitRotation(4f) + [>=1: targetOrbitRotation 4f]
+ orbitAngularSpeed + currentAngle + currentRotation(4f) + nextRotation(4f)
+ drawingGridMode + [>=2: paintingGridMode + bool + [cnt×4B]]
node 段：capacity + nodeCursor + nodeRecycle + nodePool（num4=0 空槽 / DysonNode.Import）+ recycle
frame 段：同构（DysonFrame.Import）
shell 段：同构（DysonShell.Import 三分支，见下）
```

**净计数**：nodeCount = nodeCursor - nodeRecycle - 1（frame/shell 同理）；
已建 = 池内 sp>0（节点）/ spA+spB>0（框架）。

**DysonNode.Import**（版本分支，sp 在第 11 个值）：num + id/proto/layer(3×Int32) + use/reserved(2B) + pos(3f) + **sp** + spMax/spOrdered + [>=3 cpOrdered] + [>=2 rid] + frameTurn + [>=1 shellTurn] + _spReq + [>=4 _cpReq] + [>=5 color 4B]

**DysonFrame.Import**：num + id/proto/layer + reserved(1B) + nodeId×2 + euler(1B) + **spA + spB** + spMax + [>=1 color 4B]

**DysonShell.Import**（`DysonShell.cs:326`，三分支）：
- num==0：id/proto/layer/randSeed + polygon(count×3f) + nodes(count×4B) + 2×空int + 5×变长段 + vertsqOffset + nodecps
- num==1：+ polygon + nodes + vertexCount + 空int + [Int64 数组] + [Int16×2] + [vc>65500: Int32 | UInt16] + [UInt16] + [UInt32] + vertsqOffset + nodecps
- num>=2：+ [>=3 color] + nodes + radius(8B) + gridScale + polygon(3×double) + polyn + polynu + clockwise(1B) + 10×3×double 向量 + vertexCount + **48B/顶点**（verts 3f+uvs 2f+uv2s 2f+vkeys 4B+vAdjs 6×2B+vertAttr 4B）+ vertsq(Int16) + vertsqOffset(Int16) + nodecps(Int32)

**DysonSwarm.Import**（`DysonSwarm.cs:480`，sail 数据巨大，num>7 用 ReadMassive）：

```
num + randSeed + capacity + cursor + recycle
[<=7] 逐条 sail（st 4B + [条件 28B]）→ sailInfo 循环（orbit 等）
[>7] ReadMassive（cursor × 32B/条）+ for k<cursor: st>=0.001 → Read 24B（sailInfo）
recycle×Int32 → orbit 段（cap + cursor + SailOrbit.Import×45B/条：版本+id+radius+rot4f+up3f+count+enabled）
→ expiry（num4==cap + cursor + ending + cap×12B）→ [>=7 eternal bool]
→ [>=2 absorb（num5==cap + cursor + ending + [<=7: cap×16B | >7: ReadMassive cap×16B]）]
→ [>=6 orbitColors（num6 + num6×16B）] → bullet（cap + cursor + recycle + pool + recycle）
```

**提取实现（2026-10-21，`DspCore48\DysonExtractor.cs`）**：
- 特征扫描 1212 → 流式解析层/rocket/nrd/mask → 每层节点/框架/壳 总+已建；内存固定块流式（x86 进程安全，1.7GB 档不 OOM）
- **真球判定**：层数 > 1（恒星默认 1 空层，最多 10 层）或 有在建层（节点>0）——假球（1212 巧合）恒为 1 层空
- **层轨道区间公式（用户+反编译确认）**：层半径 ∈ [max(4000, physicsRadius×1.5), maxOrbit]；
  `maxOrbit = Round(dysonRadius×40000×2 / 100) × 100`（DysonSphere.cs:488-502）——用户验证：母星 1.12R→22600、Pishpai 4.7R→74400 ✓
- **母星定位**：地中海主题行星所在恒星（两档验证均为 index 0）
- **验证（2026-10-21）**：113-599 扫出 Pishpai（10 层/建 5）+ 1 层球（8001，疑母星，匹配 Taygeta 待核）；
  w1wold3 扫出 3×10 层球（建 6/1/4 层）+ 1 层球（10000，疑母星，匹配 Alrakis 待核）；建层数与用户预期（Agena 6/Tania 1/Kappa 4）吻合
- **已知限制 / 待办（2026-10-21 晚更新：恒星对应已解决）**：
  1. **恒星对应（starMap v3）✅**：dysonSpheres 段 0/1=1 = 打开过戴森球控制面板的星系（含空层球）→
     链上 1 数 = 全部解析球（含空球）→ 段头（count==starCount）+ 0/1 链 + importStart swarm 头强校验
     （num/randSeed/swarm num/cap/cur/rec，偏移 +8/+12/+16/+20/+24）+ 层轨道可建区间校验 → 唯一命中。
     两档验证：599（H=839228203：8001→KiffaBoraelis 0 / Pishpai 59）、w1wold3（H=1595115414：母星→Zeta Reticuli 0 /
     建6层→Agena 52 / 建1层→Tania Borealis 53 / 建4层→Kappa Doradus 59）——与游戏内确认完全一致
  2. **OOM 修复**：星图 buf 需覆盖全链（lastEnd），WINDOW 128MB 在 w1wold3 超 x86 2GB → 64MB 平衡
  3. swarm 段尚有 ~4KB 零填充未闭环（不影响球体解析）；
  4. 太阳帆数（sailCursor）在 swarm 内未取；

## 7.6 工厂段解析（A：建筑检测 + 采集站 + 地面基地）——已实现（2026-10-23，FactoryExtractor 全量字段级解析 + 顺序解析）

**目标**：三类需求一次拿（同一 PlanetFactory 段解析器）：
1. **建筑检测**（B）：`entityCount = entityCursor - entityRecycleCursor - 1 > 0`（PlanetFactory.cs:235）
2. **气巨采集站**（D）：实体池按 protoId 过滤 OrbitalCollector（EntityData.protoId，需 LDB 查 EntityType）
3. **地面基地**：`enemySystem.bases` 池（EnemyDFGroundSystem.Import，组件池同 §7）

**定位路径**（GameData.Import 序列，已确认）：
```
... history → [>=13] galaxy.ImportRuntimeData（scanned 段，SaveParser 已能扫）→
[>=2] hidePlayerModel/disableController（2×bool）→ statistics.Import →
player: planetId(Int32) + Player.Import → factoryCount(Int32) → factory×N → ...
```

**反编译侦察结论**：
- `PlanetData.ImportRuntime`（PlanetData.cs:1713）**超简单**：count(Int32) + modData(count 字节) + num×Int64 + num2×(Int32+4×Single+Int32+Int64=24B) —— 工厂前缀可纯字节跳过/校验
- `PlanetFactory.Import` 前缀（8495-8543）：版本 + 行星 id + [>=5 style 3×Int32] + ImportRuntime + [>=3 landed bool] + [>=8 hashSystem×2] + [>=4 三 cursor]
- `HashSystem.Import`（542）：num×8B + (bucketCount+1)×4B + bucketCount×8B —— **bucketCount 运行时静态，未初始化时未知**（需枚举 0..N 或运行时拿）
- 实体池版本分支复杂（<=8 内联 vs >8 ReadMassive），池间无魔法标记 → **特征扫描候选**（版本+行星 id）噪音大，需 ImportRuntime+cursor 全链校验

**实现路线（建议另起会话专注做）**：
- **FactoryExtractor.cs**（DspCore48）：顺序/特征定位 factoryCount → 逐 factory：前缀（版本/行星id/style/ImportRuntime/landed/hash）→ 三 cursor（B）→ 实体池（D：protoId 计数）→ 跳 prebuild/craft/enemy/vege/vein/ruin/cargo/power/factorySystem → enemySystem（地面基地完整，组件池模式复用 HiveExtractor）
- 产出：`saveX-factory.csv`（行星id/entityCount/采集站数/地面基地数/bases 明细）
- 待读结构：Entity.Import + entityAnim/entitySign/entityConn（版本分支）、PrebuildData/CraftData/EnemyData/VegeData/VeinData/RuinData.Import、cargoContainer/cargoTraffic/factoryStorage/powerSystem/factorySystem/combatGroundSystem.Import、Player.Import（若走顺序解析）
- **statistics/player 大段**：特征扫描法可绕过（候选用 ImportRuntime+cursor 链校验），顺序法则需解析

### 2026-10-23 实现完成（`DspCore48\FactoryExtractor.cs` 重构）

**定位策略（根治“慢/卡死”）**：旧实现全文件逐字节扫 `ver(1..100)+pid`（88MB 档 2.27 万候选 → 误报深度解析，大档分钟级/卡死）。新实现：
1. **阶段 1**：定位 `factoryCount(1..999) + ver=10 + pid 合法` 模式（误报率 ~1e-14）→ **顺序解析**到段尾（下一个位置非合法工厂前缀即停）→ 链长`==fc` 才接受（057b 曾把 `fc=136` 误报当定位，链长 1 ≠ 136 → 拒绝走阶段 2）
2. **阶段 2 兜底**：`ver==10` 预筛（687 vs 旧 2.27 万）+ 全链校验，不依赖连续性；阶段 1 失败时抓漏
3. 成本：定位 O(文件) 轻扫 + 顺序解析 O(工厂总字节) — **与文件大小解耦**（882MB 档含 scanned+hive+dyson+工厂全流程 **1.7s**，201 档 88MB 1~2s）
4. `Budget` 分级：前缀探测 `LOCATE_BUDGET=8MB`（hash 段大工厂可达 1.09MB，1MB 会误杀 103）、顺序解析 `PARSE_BUDGET=120MB`

**字段级 bug 修复（全部对照 decomp-full 逐字段）**：
- `n2×24→28`（ImportRuntime num2 循环 = Int32+3×Single+Int32+Int64 = 28B；201 档 n2 全 0 未触发，其它档会错位）
- `SkipBelt` 重写：旧实现把 `FlexibleInt` 读成 `U8()+Flex()+I32()`，每条错多字节 → 多传送带工厂必错位（101/104 cargoTraffic 失败根因）
- `SkipMonitor` 重写：缺 offset/数组段、多读 U16
- `SkipGen`/`SkipConsumer`/`SkipAcc` 重写：networkId/photo·wind·gamma/2×I64 等无条件字段被塞进版本分支，num 非最大值时每条约差几字节（101 gen=164 条累计错位 → node 池读 0 区）
- `SkipFractionator` 补尾部：漏读 fluidInputMax/productOutputMax/fluidOutputMax/[≥1 total×2]/seed
- `SkipGDBuilder`：2026-10-23 曾按 GrowthPattern_DFGround.Builder 重写，但字段仍不对；**2026-10-29 修正为真结构**：
  `U8 版占位 + I16 protoId + FlexibleInt instId + [instId>0: I16 instBuilderId] + pos(3F32) + rot(4F32)
  + [protoId==8127（轨道）: F32 length] + I32 matterNeeded + I16 matterSpeed + I16 connections.Length
  + I16×N + U8 rank`（旧实现的 hexCoord/yaw/outputs 那套是错的）
- `SkipGroundPool` shields 池 rc 校验放宽（`rc>cap+1` → `rc>100000`）：404 档 shields 真实数据 `(0,0,20480)`（cap=0 但 rc 大）被误杀
- `units` 空池显示 `-1 → 0`

**验证（2026-10-23）**：
- `113-Z01-201进入黑洞.dsv`：**11/11 玩家登陆行星全提取**（101/102/103/104/201/303/401/403/404/1701/1703）；10 个有建筑行星 bases=0（基地已摧毁）；`404 Taygeta IV` entities=0、**bases=6**（用户记忆中为 4，存档真值 6，待复核）
- `113-Z01-057准备进攻-1.dsv`：1 工厂 bases=5 ✅（与游戏一致）
- `my-save.dsv`：1 工厂 entities=1（采矿机）✅
- `_lastexit_.dsv`（882MB）：11 工厂，全流程 **1.7s**

**边界认知（重要）**：黑洞星 DSR J0645+29 - I号星（36 基地）玩家未登陆 → **存档无该行星数据（未扫描行星不落盘）→ 无 factory 段，无法提取**。同行星的黑雾基地只能从“玩家登陆过的行星”的 factory.enemySystem 获得；太空巢穴由 HiveExtractor（19884）提供。

### 2026-10-29 修复（巨型基地漏解析：母星 103 + Pishpai I 号星 6001）

四个根因（my-save 931MB 档，目标 100 个工厂）：

1. **bases 池漏读 ObjectPool 槽位标记**：`EnemyDFGroundSystem.bases` 是 `ObjectPool<DFGBaseComponent>`（class 元素），
   Export/Import 每槽前多一个 int32（有元素→槽位号 `i`，空槽→0）；其余 6 池是 `DataPool<T>`（struct，无标记）。
   漏读→后续 connectors/replicators 全错位（982 现场 `[groundPool:replicators] cap=3 cur=1000001`）
2. **prebuild 非空元素后的 16 个连接标志**：`PlanetFactory.Export`（8349-8363）在
   `prebuildPool[m].Export(w)` 之后、且仅 `id != 0` 时写 `prebuildConnPool[m*16 .. +16]`：
   0 → 1 字节；非 0 → 1 字节 1 + int32。漏跳→下一个元素错位（6002 现场第 7019 个非空元素后差 24 字节）
3. **`SkipGDBuilder` 二次修正**（真结构见上方字段级清单）
4. **`TryHashProbe` 的 hashPool 上限 10 万太严**：巨型基地 hashPool 可达 **18.5 万条**（6001），
   旧上限直接判失败 → 回退到错误 bucketCount（140）→ 三 cursor 读成垃圾（cur=18）。放宽到 500 万

验证（my-save）：100 个工厂；母星 103 = 42,822 实体（28 种建筑，2104 星际物流站 16 个）、
6001 = 73,425 实体（2104=7，与游戏内逐项一致）、6002 = 57,039 实体（已有）。
回归：982（103/entities=2/bases=1）、my-save（2 工厂）、057（bases=5）均不变。

### 2026-10-22 实测突破（my-save.dsv 已验证）

- **factory[0] 前缀确认**（`PlanetFactory.Export` 反查）：`ver=10 + planetId + theme + algoId + style + ImportRuntime + landed + hash×2 + 三cursor`——版本恒为 **10**，中间 3 个 Int32 是 theme/algoId/style
- **bucketCount 反推法**（my-save=362）：hash 段 = `4 + num×8 + 12×bc + 4`，bucket 数组每 12B 交错（offset 递增 32 + cursor + recycle，Export 为 `for j<bc+1: offset; if j<bc: cursor+recycle`）；从 landed 后找到 bucket 数组，数 offset 连续递增 32 的组数即可得 bc
- **池 anim 修正**：craft/enemy/vein 池 anim = 20×(cursor-1)（循环 1..cursor-1），实体池 anim = 20×cursor（ReadMassive 全量）；**空池 cur=0 合法**（勿要求 cur≥1）
- **实现验收（my-save）**：工厂 1 个 = `planet=104, entities=1（采矿机）, bases=0, bc=362`——与玩家操作一致
- **057b 验收（2026-10-22）**：`planet=103, entities=2286（309 设施+1658 传送带+分拣器等）, bases=5（5 个黑雾地面基地）, bc=362`——与游戏内一致
- **已修复的字段级 bug 清单**（本日）：
  1. bucketCount 用 offset 递增 32 反推（362）——勿用候选集猜
  2. 实体池三 cursor 由前缀读，SkipEntityPool 直接用（勿重读/回退）
  3. 空池 cur=0 合法（prebuild/craft/enemy/vege/vein/ruin/各池）
  4. craft/enemy/vein 池 anim = 20×(cursor-1)；实体池 anim = 20×cursor
  5. SplitterComponent 版本是第一字段 Int32（勿在尾部读 byte）
  6. FactoryStorage：storage 记录 = id(4) + size(4) + StorageComponent，**空槽（id=0）只写/读 4B**；tank 同
  7. FactorySystem：每 inserter 后 `if ver>=1: InserterPose`（31B）

## 8. 地面基地（EnemyDFGroundSystem）与 PlanetFactory

地面基地**不在 SpaceSector**——挂在 `PlanetFactory.enemySystem`（`PlanetFactory.cs:151`），
`PlanetFactory.Import`（8495 行起）第 **8938-8939 行**：
`enemySystem = new EnemyDFGroundSystem(planet, import: true); enemySystem.Import(r);`

**EnemyDFGroundSystem.Import**（`EnemyDFGroundSystem.cs:175`）：

```
rtseed(Int32) + rtseed_lehmer(UInt32) + maxAssaultWaves(Int32)
builders.Import + bases.Import + connectors.Import + replicators.Import +
turrets.Import + shields.Import + units.Import（组件池同 §7）
```

`bases` 池 = 该行星的**地面基地**（用户"母星 5 个基地"= 母星 factory.enemySystem.bases 5 个）。
**未实现**：需解析 PlanetFactory.Import 前段（行星 id/entityCursor/enemySystem 位置，~440 行结构）。

**建筑检测**（"行星有无建筑"）：`entityCount = entityCursor - entityRecycleCursor - 1 > 0`
（PlanetFactory.cs:235）——同样在 PlanetFactory.Import 前段。

**气巨采集站**：OrbitalCollector 实体（factory 实体池过滤，EntityType 值待 LDB 确认）。

## 8.5 2026-10-30 补：巢穴存活 / 戴森球层状态 / 玩家重命名

### 巢穴是否已被清除（`HiveExtractor`）

存档会保留**已摧毁巢穴的历史记录**（`realized=True, isEmpty=False` 也照样在），不能只看这两个布尔。
游戏 `EnemyDFHiveSystem.isAlive`：空巢/未实现不算；否则看**组件池是否有东西**。实测可用信号
（`hive.csv` 新增列）：

- `cores` / `builders` / `nodes` / `replicators` / `turrets` / `units` / `pbuilders`（池元素数，>0 = 有东西）
- `rootEnemyId`（常为 0，不可单独用）
- 活跃 relay（`baseState==1 && stage==2`）/ 本地火种数

判定：`IsAlive = !isEmpty && (!realized || rootEnemyId>0 || pbuilders>0 || cores>0 || builders>0
|| nodes>0 || 活跃relay>0 || 火种>0)`。

实测：201 档活巢穴 builders=824/pbuilders=811/cores=1；my-save 全档这些信号大量为 0
（玩家已清完大部分黑雾，Pishpai 与母星系巢穴全 0）→ `hiveCount` 归 0。

### 戴森球层状态

层 = `node/frame/shell` 三池，`empty = 三池全 0`。旧实现把「节点+框架全建」判为完成 —— **错**：
壳体可能还在打太阳帆（my-save 的 Pishpai：5 层节点/框架全建但只有 1 层真正完成）。
壳填充度需读 `DysonShell` 的 `nodecps`（`cellPoint`/`cellPointMax`），当前未提取；
**现口径：只区分「空层 / 有建造层」**（`dyson = {layers, empty, built, rocketsInFlight}`）。

### 玩家重命名（`GamePrefsData.astroNameOverride`）

玩家改的星球/恒星名**不在星系数据段**，而在 `preferences`（GamePrefsData）：
`Dictionary<int,string>`，序列化 = `count(int32) + (astroId int32, name = 7-bit 变长长度 + UTF8)×N`。

- 恒星 key = `starId × 100`（如恒星 1 → 100）
- 行星 key = `planetId`（如 103）
- `displayName = overrideName 非空 ? overrideName : name`（`PlanetData.cs:230` / `StarData.cs:87`）

实现：`SaveParser.FindAstroNameOverrides(path)` 全文件扫候选头 + 严格校验（astroId 合法、
名字为可打印 UTF8），无需解析整个 GamePrefsData。实测 my-save：38 条（太阳 / 地球 / 石墨烯 / 白糖…），
merge 后 `nameSource="override"`。

### 矿脉储量：两个不同来源（2026-10-30）

**关键**：同一颗行星的储量存在两处，含义不同：

1. **galaxy 已扫描段**（`ImportScannedData`，SAVE-FORMAT §4）——**生成时的初始储量**（不变）
2. **`PlanetFactory.veinPool`**（工厂段，`VeinData.Export`）——**运行时当前值**（采矿机开采后减少）

旧实现用 (1) → 显示“原始储量”，导致已采空的行星仍显示满量（地球 103 现场）。
现改为**有工厂段时优先用 veinPool**（`amountSource="factory"`），否则回退存档段（`"save"`）。

`VeinData.Export`：`U8(1) + id(4) + type(I16) + modelIndex(I16) + groupIndex(I16) + amount(I32) + productId(I32)
+ pos(3F32) + combatStatId(I32) + minerCount(I32) + minerId0..3(I32×4) + hashAddress(I32)`。

据此额外得出两个字段：
- **`plannedAmount`**：`minerCount > 0` 的矿脉 amount 之和（已规划采集）
- **`unplannedAmount`**：`minerCount == 0` 的 amount 之和（**未规划采集 = 开采潜力**）

验证（my-save）：地球 103 → 铁/铜/石/煤 = 0、油 79717（= **3.19/s**）、未规划 2500（= **0.1/s**）；
Rastaban II（1702）→ 铁 131583 / 铜 126306 / 钛 635438 / 石 2752065 / 金伯利 411918 全与游戏一致。

### 气巨/冰巨气体 item id

`ThemeProto.GasItems`：气巨（Gas 1/2/5）= `[1120 氢, 1121 重氢]`；冰巨（Gas 3/4）= `[1011 可燃冰, 1120 氢]`。
注意 **1011 = 可燃冰**（不是 1123 = 石墨烯）。

## 9. 已知问题 / 待办

- **hive 大巢穴解析失败 ✅ 已修复（2026-10-23）**：201 档 189/189 全提取（修复前 82），
  黑洞/中子星 6+6 巢穴全部入 csv。
  - **根因（两个）**：
    ① `RFlex` 编码错误：HiveExtractor 用 **LEB128**（bit7 高位标志）读游戏 **FlexibleInt**
      （IOHelper.ReadFlexibleInt **flag 前缀编码**：0x00=0 / 0x01+1B / 0x02+ushort2B / 0x03+3B /
      0x04+int32 / 0x05..0xFF=原值）。值 1~4 与 256~65535 时两者字节数不同 → Builder.instId、
      savedWorkProgress、Unit.planetId/enemyId/stateTick 等 flex 字段逐条错位，
      1347 条 builder 累计后组件池头必然错乱。
    ② `SkipCore` 少跳 12B：DFSCoreComponent.Import 为 15×Int32+1×Single=16×4（旧实现 13×4），
      cores 池非空的大型巢穴后续全部错位。
  - **修复**：`RFlex` 重写为游戏 flag 编码（HiveExtractor.cs:233）；`SkipCore` 补齐 16×4。
    其余 12 个 Skip*（Builder/BuilderComp/Node/Connector/Replicator/Gamma/Turret/Relay/
    Evolve/Carrier/TargetQueue/Hatred）逐字段对照 decomp-full 反编译复核，**全部正确，未动**。
  - **验证（113-Z01-201进入黑洞.dsv，seed=39645113）**：old 82 → new **189/189**；回归无损
    （旧 82 个 astroId 全部在集合内，新增 107）；恒星分布：黑洞 64=6（1000505~1000510，
    seed 45756528~33 连续）、中子星 63=6（1000497~1000502）、Kiffa 1=3、Sabik 3=3（存档口径）。
  - **残留**：magic=194 候选 vs ok=189（5 个假阳性 19884 标记在 stage0 被 astroId 校验拒，非漏采）。
- **007/201 档资源倍率存疑 ✅ 已解决（2026-10-23）**：根因 = DspCore48 复刻把 `isRareResource`
  写死 false，漏了游戏原版 GenerateVeins 的「资源极难（≤0.1x）时 resourceCoef 额外 ×0.7（且 >1 时 ^0.8）」；
  修复后模型 0.1x 与存档逐位一致（201 档 401 铁 137102/铜 968480/硅 1273751 全对上），
  未开采判定 0.1x 档同步可用；007 存档/模型1x = 0.07 = 0.1×0.7（0.056 旧记录即此）。
- **戴森球（2026-10-21 已实现扫描+解析；恒星对应待攻）**：`DysonExtractor.cs` 扫球/层/节点/框架/壳
  +建层数+在途火箭；真球判定=层数>1或有在建；层轨道区间公式已确认；母星=地中海行星所在恒星。
  待办：swarm ~4KB 闭环、太阳帆数、旧档兼容（恒星对应已解决：starMap v3 两档验证通过）
- **地面基地（已实现 2026-10-23）**：`FactoryExtractor.cs` 顺序解析打通，产出 `saveX-factory.csv`
  地面基地数列；基地明细（seed/等级/仇恨）按用户决策不需要，只保留数量
- **玩家工厂/星际物流**：按用户决策不解析明细，仅保留建筑检测（实体数）
- **火种（已实现 + 已验证）**：DFTinderComponent 提取 + 位置/ETA（2026-10-23，_lastexit_ 档 30 个）
- **headerVersion/gdVersion 兼容**：按用户决策不扩展，只用于当前版本（7/13）
- **采集站（✅ 2026-10-23）**：protoId 2105；**未开采矿组（✅ 2026-10-23）**：模型参照排序对比，
  my-save验证通过，后期档全已开采（黑雾采矿），判定口径待用户数据校准

## 10. 工具实现索引

| 功能 | 实现 |
|---|---|
| 头部 + gameDesc + 已扫描行星 | `DspCore48\SaveParser.cs`（流式 + 多线程分块） |
| 黑雾太空巢穴 + 火种 + relay | `DspCore48\HiveExtractor.cs`（19884 定位 + 完整 hive.Import；2026-10-23 修 RFlex 编码 + SkipCore，189/189 全提取；relay 投放明细 → 行星地面基地数） |
| 戴森球（层/节点/框架/壳 + 在途火箭） | `DspCore48\DysonExtractor.cs`（1212 定位 + 流式解析 + starMap 恒星对应） |
| 工厂段（建筑检测/采集站/地面基地） | `DspCore48\FactoryExtractor.cs`（已实现 2026-10-23，11/11 全提取；采集站计数 protoId 2105 已输出） |
| CSV 输出（--save） | `DspCore48\SaveExport.cs` |
| 对拍脚本 | 历史诊断脚本（已清理，未随仓库发布） |
| 早期 python 解析 | `src\tools\parse_dsv.py`（历史，被 SaveParser.cs 取代） |

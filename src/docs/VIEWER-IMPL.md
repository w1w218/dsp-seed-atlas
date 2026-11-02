# 星系浏览器实现方案（VIEWER-IMPL）

定位：`VIEWER-PLAN.md` 的 **S4b（字段扩展）+ S5（渲染）** 落地文档。方案已与用户逐条确认（2026-10-28）。
工程：`src\DspView`（net8 CLI，HTTP 服务 + 静态页面），数据源 `galaxy.json`。

## 0. 流程定位（用户定稿）

```
S2 初始生成（--seed --json）→ S3 规划（DspPlan，跑一次）→ 【锁定】
                                                          ↓
                        后续唯一动作：新存档 → DspSearcher --merge → 刷新网页
```

- 初始生成与规划**各只跑一次**，之后 `plan` 与手填规划文本作为基线长期保留
- 后续只做存档合并（名字/储量/黑雾/戴森球/采集器/开阔度/火种/已建状态）
- `meta.bestCost` 网页不使用、不校验、不清理（保留原值；手工改动后它不代表当前布局，属已知）

## 1. 列规范（27 列，按顺序）

| # | 列 | 内容 | 空值规则 |
|---|---|---|---|
| 1 | 恒星 | 存档中文恒星名 | — |
| 2 | 恒星类型 | 主序星按光谱（`G型恒星`/`O型星`…）；`黑洞`/`中子星`/`白矮星`/`巨星` 直接显示 | — |
| 3 | 恒星光度 | `luminosity` 3 位小数 | 0 空白 |
| 4 | 行星名 | 存档中文行星名 | — |
| 5 | 主题 | `themeName` | — |
| 6 | 开阔度 | 空间档 1~10（10 最空旷） | 未探索 `—` |
| 7 | 规划 | **可编辑自由文本**；初始值 = 该行星 `plan.slots` 槽名拼接（如 `铁块-0`）；无槽为空 | 空 |
| 8 | 距离 | `distanceFromBirth` 光年，2 位 | 0 显示 `0` |
| 9 | 备注 | 拼接：`硫酸`（waterItemId=1116）/ `采集站 6/40`（气巨冰巨）/ `戴森球 5层·在建2`（所属恒星系） | 无内容空白 |
| 10 | 黑雾 | 文本 = 该行星黑雾基地数（`max(blackHoleBases 本地, blackHoleBasesRelay 全局)`）；背景色按所属恒星系**太空巢穴数 1~6** 浅红→深红 | 0 空白、无色 |
| 11–24 | 铁 铜 硅 钛 石 煤 油 可燃冰 金伯利 分形硅 有机晶体 光栅石 刺笋 单极磁 | `已规划/未规划`（工厂 veinPool：`minerCount>0` 蓝 / `==0` 绿）；0 的一边不显示；`已规划+未规划 < 初始储量×10%` 时已规划标红；油按 amount/25000 显示 /s；**模型推算（未探索行星，`amountSource="model"`）= 白色** | 全 0 空白 |
| 25–27 | 氢/min 重氢/min 可燃冰气/min | 气巨/冰巨气体：每采集站速率 × **40 站** × 60s（`planet.gas.speeds`，已含恒星系数^0.3） | 无气体空白 |

**颜色语义**

- 规划列文本色：已建（`hasFactory`）= **绿**；未建 = **蓝**；未探索 = 灰
- 黑雾列背景：巢穴数 / 6 映射 6 级色阶（普通星 ≤3 → 浅~中红；黑洞 ≤6 → 最深）
- 未探索行：整行 55% 不透明度

**交互**

- 表头 sticky（分组色条 + 列名）；点击排序（按原始数值）
- 筛选：恒星 / 主题 / 已探索 / 有黑雾 / 有规划 / 有戴森球；搜索框匹配行星名/恒星名/id
- 规划单元格：双击进入编辑 → 回车/失焦保存（走 `POST /api/notes`）→ 成功按已建/未建上色，失败标红回滚
- 行双击 → 3D 进入该行星所在恒星系并高亮

## 2. 3D 规范

### 星系视图（默认）

> 尺寸/配色/轨道参数的权威数值见 **§9**（同一处维护，避免两段漂移）；本节描述结构与交互。

- 恒星按 `stars[].position`（光年）布置，最大星距归一化到 90 世界单位；相机起点 = 出生星 +(70, 90, 110)
- 恒星是 **sprite 色晕 + 锐利白核** 两层（`lum > 0.25` 才叠白核）：色晕 `gs = 1.2 + pow(lum,0.3)×2.6`、白核 `0.30×gs`
- 颜色 = 光谱饱和色（O `0x66c8ff` / B `0x9fc0ff` / A `0xdfe8ff` / F `0xfff2d8` / G `0xffd75e` / K `0xff9d4d` / M `0xff6b3d`）；
  **黑洞 = 紫红光球 `0xd94bff`（不是黑球）**、中子星紫蓝 `0x9a86ff`、白矮 `0xeaf2ff`、巨星按光谱（M/K 橙红 `0xff6a20`、B/A/O 用光谱色）
- 恒星名标签：**出生星（白 `#ffffff`）+ 所有有已建行星（`hasFactory`）的恒星（绿 `#9fe3b8`）**，其余悬停显示
- 悬停 tooltip：星名/类型/行星数/已探索数/巢穴数/戴森球层数
- **双击恒星 → 恒星系视图**（相机 0.6s 过渡）
- 已移除（10-31 重做，旧版残留勿再参考）：`6ly` 线框球壳、规划黄环、巢穴红环、出生星光环

### 恒星系视图

- 中心恒星：`starRadius`（§9）+ **ShaderMaterial 白热核心 + 菲涅尔边缘发光 + 表面噪声**（巨星噪声 0.75 呈斑驳）；
  外加 Canvas 径向渐变日冕 sprite（仅恒星系视图）
- 轨道：`AngleAxis(longitude, up) * AngleAxis(inclination, forward)`（与 `runtimeOrbitRotation` 同序），
  半径用 `sqrt(orbR/maxOrbR)×46` 压缩；平面用 `RingGeometry`（外径 = R）+ `ShaderMaterial` 径向 alpha（外亮内透）
- 行星：轨道平面内按 `phase` 取点；半径 = `sqrt(realRadius/200)`（气巨 ×10 → 约 2.0 倍）；
  颜色按主题（Gas 青绿 `#7fe0b0` / Vocano 橙红 `#e0663f` / Ocean 青蓝 `#35b8c8` / Desert 土黄 `#d8a878` / Ice 白蓝 `#cfe8f5`）；身后带 110° 渐隐轨迹线
- 卫星：`orbitAround > 0` 绕母行星画小轨道（平面取母行星轨道平面，alpha 0.20）
- 规划文本标签挂行星旁（`铁块-0`，已建绿 / 未建蓝）；未探索行星暗色半透明
- 点击行星 → 详情卡（名称/主题/开阔度/矿脉/规划/黑雾/已建）+ 表格滚动高亮
- **ESC → 返回星系视图**；滚轮缩放、拖拽旋转

### 联动

| 动作 | 效果 |
|---|---|
| 表格双击行 | 切恒星系 + 高亮行星 + 详情卡 |
| 3D 单击行星 | 表格滚动高亮 |
| ESC | 恒星系 → 星系视图 |

## 3. 数据模型增量（S4b）

`galaxy.json` 新增字段（其余不变；`plan` / `planNote` 不被 merge 改写）：

```jsonc
"planets": [{
  "radius": 200.0,              // PlanetData.radius（3D 大小；气巨渲染时再 ×10）
  "waterItemId": 0,             // 0 无水 / 1000 水 / 1116 硫酸
  "spaceLevel": 6,              // 开阔度（空间档 1~10）；未扫描 null
  "collectors": 6,              // 气巨/冰巨采集站数（仅存档有；否则 null）
  "hasFactory": true,           // 存档实体数 > 0
  "blackHoleBases": 2,          // 地面黑雾基地数（本地 factory bases 口径）
  "blackHoleBasesRelay": 0,     // 黑雾基地 relay 口径（全局，含未登陆行星）
  "gas": { "items": [1120, 1121], "speeds": [0.7456, 0.0270] },   // 气巨/冰巨气体：每秒每站，已含恒星系数^0.3
  "planNote": "铁块-0"          // 手填/派生规划文本；merge 不改
}],
"veins": [{
  "type": 7, "groups": 18, "initialAmount": 1320000,
  "currentAmount": 1110543,     // 运行时当前值（工厂 veinPool 优先，否则扫描段/模型）
  "plannedAmount": 800000,      // minerCount>0 的 amount 之和（已规划采集）
  "unplannedAmount": 310543,    // minerCount==0 的 amount 之和（未规划采集 = 开采潜力）
  "amountSource": "factory"     // model | save | factory
}],
"stars": [{
  "hiveCount": 2,               // 太空巢穴数（1~6，已按 isAlive 判活）
  "dyson": { "layers": 5, "empty": 3, "built": 2, "rocketsInFlight": 120 }   // 只区分空层/有建造层
}],
"meta": { "save": { "tinders": [ { "targetPlanetId": 103, "stage": 2,
            "speed": 0.45, "etaMinutes": 3.2 } ] } }
```

生成/更新来源：

- `radius`/`waterItemId`：`GalaxyJson.cs` 从 `PlanetData` 写入（**需改代码并重新生成一次**）
- `spaceLevel`：`SpaceAnalyzer.Analyze(veinDirs, radius)`（merge 内调用；未扫描无矿组 → null）
- `collectors`/`hasFactory`/`blackHoleBases`：`FactoryExtractor.Extract`
- `blackHoleBasesRelay`：`HiveExtractor` relay 活跃投放数（未登陆行星也有值）
- `gas`：`Merge.cs` 由 `pdata.gasItems / gasSpeeds × coef` 写入（`coef` 含恒星资源系数^0.3）
- `veins[].amountSource`：初始生成 = `model`；merge 时工厂段有 `veinPool` → `factory`（同时写
  `currentAmount`/`plannedAmount`/`unplannedAmount`），否则回退扫描段 `save`（只写 `currentAmount`）
- `hiveCount`：`HiveExtractor.Extract` 按恒星聚合
- `dyson`：`DysonExtractor.Extract`（按恒星 index 映射）
- `tinders`（含 ETA）：`HiveSummary.Tinders` + `SpaceSectorExtractor.Astros`；
  `ETA(min) = dist(m) / speed(m/s) / 60`，`1 ly = 2,400,000 m`

## 4. 服务模式（C# / HttpListener）

```
DspView --galaxy <galaxy.json> [--serve] [--port 8765]     # 无 --serve 只生成 web\
```

- `GET /`、`/app.js`、`/style.css`、`/lib/*` → 静态文件（白名单路径，防目录穿越）
- `GET /api/galaxy` → galaxy.json 原文（前端优先用它；`file://` 回退 `galaxy-data.js`）
- `POST /api/notes` → `{ "notes": { "103": "磁线圈组-0", "2101": "晶格硅 120/min" } }`
  - 校验：行星 id 存在、文本长度 ≤ 200、非探索行星允许填写
  - 写回 `planets[].planNote`；写前备份 `galaxy.json.bak-note`；返回 `{ok, errors}`
- 保存后前端即时更新颜色（不再整页刷新）
- 单用户本地使用，无并发控制（单进程写）

**为什么不用 Python**：零新依赖（HttpListener 属 BCL）、校验逻辑与 C# 侧共用、Newtonsoft 保字段顺序/数字格式避免无意义 diff。

## 5. 工程结构

```
src\
├── DspView\
│   ├── Program.cs          CLI 解析 + 服务启动 + 打开浏览器
│   ├── Renderer.cs         galaxy.json → galaxy-data.js；复制模板与 lib
│   ├── Api.cs              静态文件 + /api/galaxy + /api/notes（HttpListener）
│   ├── Templates\          index.html / app.js / style.css
│   └── lib\                three.min.js（r128 UMD）/ OrbitControls.js
└── DspCore48\…             Merge.cs 扩字段（S4b）
```

产物：`<galaxy 同目录>\web\`（index.html / app.js / style.css / galaxy-data.js / lib\）

技术：three@0.128.0 UMD（`file://` 与 http 均可用）、原生 JS、无构建步骤。

## 6. 实施顺序与验收

**S4b 字段扩展**
1. `GalaxyJson.cs` 加 `radius`/`waterItemId`
2. 重新生成（`--seed 84272982 --resource-multiplier 0.1 --json`）→ `DspPlan apply`（恢复 plan）→ `DspSearcher --merge`（扩展版）
3. 验收：新字段齐全；plan 40 槽不变；merge 后 84/85 储量一致

**S5-V1 表格 + 服务**
4. DspView + 模板 + 27 列渲染 + 颜色/空白规则 + 火种预警条 + `/api/notes` 保存
5. 验收：`--serve` 打开 243 行；规划编辑保存后 galaxy.json 的 `planNote` 更新且页面颜色刷新；`file://` 只读可看

**S5-V2 星系 3D**
6. 恒星按 position/光谱/光度；6ly 球壳；规划环/巢穴环；悬停；双击进入
7. 验收：64 星位置/颜色正确；双击进入对应恒星系

**S5-V3 恒星系 3D + 联动**
8. 轨道/相位/行星半径/卫星；规划标签；详情卡；表格双向联动；ESC 回退
9. 验收：母星系（内阶一）4 行星轨道方向与游戏一致；联动生效

**S5-V4 打磨**
10. 列宽/冻结、预设筛选、图例、移动端基本可用、性能抽查

## 7. 风险与已定决策

- 轨道旋转顺序：先按原版合成实现，用 seed 84272982 母星系肉眼校准
- 未探索行星：开阔度 `—`、采集站/黑雾/戴森球/已建均空
- 恒星标签：常驻仅出生星 + 有已建行星（`hasFactory`）的恒星，其余悬停显示（避免糊屏）
- 黑雾色阶按 6 级映射（普通星 ≤3、黑洞 ≤6）
- `bestCost` 不显示不校验（用户定）
- 规划是自由文本，不做槽位互斥校验（用户定）

## 8. 已实现状态（2026-10-28）

- **S4b 字段扩展** ✅：`GalaxyJson.cs` 加 `radius`/`waterItemId`；`Merge.cs` 扩展写入
  `spaceLevel`/`collectors`/`hasFactory`/`blackHoleBases`/`blackHoleBasesRelay`/`hiveCount`/`dyson`/`meta.save.tinders`
- **S5-V1 表格 + 服务** ✅：`DspView --galaxy <json> [--serve] [--port 8765]`；27 列（含氢/重氢/可燃冰气）、颜色语义、空白规则、
  火种预警条、规划单元格编辑（`POST /api/notes` 写回 `planNote`）
- **S5-V2 星系 3D** ✅：恒星位置/光谱色/光度大小、6ly 球壳、规划黄环、巢穴红环、双击进入恒星系
  （注：6ly 球壳/规划黄环/巢穴红环已在 10-31 重做中移除，见 §9）
- **S5-V3 恒星系 3D** ✅：轨道（lon→inc 旋转）/相位/行星半径/卫星绕行/规划标签/详情卡/ESC 回退/表格双击联动
- **深链**：`index.html#star=<恒星id>` 直接进入恒星系（分享/截图用）

**已验证**：`--serve` 下 `GET /` 200、`GET /api/galaxy` 200、`POST /api/notes` 写入 `planNote`
并生成 `.bak-note`；Edge headless 截图确认表格 243 行、颜色语义、备注/黑雾色阶、两套 3D 视图渲染正常。
修复过程：卫星曾被错画在恒星旁（`orbitAround` 是序号不是 planetId）、相机裁掉内圈轨道。

**2026-10-29 UI 调整**

- 3D 显隐开关（工具栏「3D 开/关」）：关闭时表格占满全宽，状态存 `localStorage`，3D 懒初始化（关闭时不建渲染器）
- 恒星 3 列（恒星/类型/光度）合并为 rowspan 单元格：1 个行星只显名；≥2 个行星显 名/类型/光度 三行
- 黑雾列改 6 级实色（`#3a1f1c → #c03a24`，6 级内描边），巢穴数 1~6 层次明显
- 恒星列 + 行星名列横向滚动冻结（用 `td.star-frozen`/`td.name-frozen` 类名而非 `nth-child`，避免 rowspan 后续行错位；表格 `width: max-content` 保证冻结偏移准确）

**2026-10-29 修复（重要）**

- 根因：`init3D` 被调用两次（`init3DToggle` 内一次 + 启动流程一次）→ 两个 canvas 叠加：
  上层 canvas 吞掉鼠标事件（旋转/缩放/悬停/双击全失效），且两个 canvas 撑高 `#three` 把详情/图例/提示挤出可视区
- 修复：`init3D` 幂等守卫（`if (renderer) return`）+ 清残留 canvas + canvas 绝对定位（不参与 flex 高度）+ 单次初始化
  （`apply3DLayout` 统一入口）；双击表格时若 3D 关闭则自动打开
- 验证：dump DOM 确认 canvas=1、`#detail`/`#legend`/`#hint` 可见；截图确认详情面板与图例恢复（用户实测交互正常）

**待人工核对**（视觉细节，不影响功能）：1 号星（熔岩）标识是否易认、恒星系内标签重叠、
轨道倾角/经度方向是否与游戏一致。

## 8b. 已知问题 / 待办

- **工厂段解析（2026-10-29 已修复）**：原症状 —— my-save定位到工厂但 `enemySystem` 池序列错位
  （`[groundPool:replicators] cap=3 cur=1000001`）→ `hasFactory`/`collectors`/`blackHoleBases` 为空。
  四个根因（全部对照 `ref\decomp-full` 修正）：
  1. **bases 池漏读 ObjectPool 槽位标记**：`bases` 是 `ObjectPool<DFGBaseComponent>`（class 元素），
     每槽前多一个 int32（有元素=槽位号，空=0）；其余 6 池是 `DataPool<T>` 没有。
  2. **`SkipGDBuilder` 结构整体错误**：真结构 = `U8 版占位 + I16 protoId + FlexibleInt instId +
     [instId>0: I16 instBuilderId] + pos(3F32) + rot(4F32) + [protoId==8127: F32 length] +
     I32 matterNeeded + I16 matterSpeed + I16 connections.Length + I16×N + U8 rank`。
  3. **`SkipPrebuild` 漏跳「非空元素后的 16 个连接标志」**：`PlanetFactory.Export` 在
     `prebuildPool[m].Export(w)` 之后、且仅 `id != 0` 时写 `prebuildConnPool[m*16..+16]`
     （0→1 字节；非 0→1 字节+int32）。漏跳会让后续元素错位（6002 现场差 24 字节）。
  4. **`TryHashProbe` 的 hashPool 上限 10 万太严**：巨型基地 hashPool 可达 18.5 万条（6001），
     旧上限直接判失败后回退到错误 bucketCount（140）→ 三 cursor 读成垃圾（cur=18）。放宽到 500 万。
  - 结果（my-save）：**100 个工厂**，含之前漏掉的母星 103（42,822 实体）与 Pishpai I 号星 6001
    （73,425 实体 / 2104 星际物流站 7 个，与游戏内一致）；回归 982 / my-save / 057 均不变
  - 黑雾基地仍双口径：本地（工厂段残留）+ relay（全局，含未登陆行星）
- 未扫描行星：开阔度/采集站/黑雾/戴森球/已建均空（数据源不存在，非 bug）
- **「已探索」判定（2026-10-30 修）**：`scanned` 来自存档已扫描段，只记录**玩家登陆过**的行星；
  气巨/冰巨不能登陆（采集站在轨道上）→ 恒为 false，会出现「建了 40 个采集站却显示未探索」。
  前端加载数据后归一化：`scanned || hasFactory || collectors>0` 即视为已探索
  （当前档 4 颗气巨命中：102/403/701/1502）；无采集站的气巨仍显示未探索（合理）
- 大档（900MB+）存档解析/融合必须走 **`DspSave`（net8 x64）**；x86 的 DspSearcher 会 OOM
  （精算仍必须 x86，存档解析不需要）

## 9. 2026-10-31 3D 尺寸/颜色重做（对照游戏星图截图）

**尺寸体系**（`app.js` 顶部常量）

- 行星按游戏 `realRadius = radius × scale` 取数：普通行星 200×1=200，气巨 80×10=**800**（气巨大是常态，不是 bug）
- 视觉半径用 **sqrt 压缩**：`planetRadius = sqrt(realRadius/200)` → 普通 1.0、气巨 2.0、卫星 0.45
- 恒星 `starRadius = 3.2 + log10(max(0.005,lum)+1)×1.2`（G≈3.56 / O≈4.7 / 黑洞 2.4 / 中子星 2.6）
- 比例：恒星 : 气巨 : 行星 : 卫星 ≈ 7 : 4 : 2 : 1 —— 行星不再大于恒星
- 卫星轨道 `max(母星半径×2.4, 1.6)`，气巨不再吞卫星

**轨道**

- 距离改 **sqrt 压缩**（`sqrt(orbR/maxOrbR)×46`），外圈更密，内圈行星不再挤成一团
- 下限 `max(R, starR×1.5 + planetR)` 防穿模
- 轨道平面：外缘落在轨迹线上（`RingGeometry` 外径 = R），向内侧 `0.12R+0.4` 范围用 `ShaderMaterial` 做径向 alpha（`pow(t,1.6)`，外亮内透→融入背景）；不是以轨道线为中心的对称环带；中心留 1px 细线
- 卫星轨道同款：中心在母行星、平面取母行星轨道平面（`planetQ`），alpha 固定 0.20
- 轨迹线：行星身后 110° 弧，`AdditiveBlending + vertexColors` 从行星色渐变到黑 → 一头浓一头淡

**颜色**

- 行星：Gas 青绿 `#7fe0b0`、Vocano 橙红 `#e0663f`、Ocean 青蓝 `#35b8c8`、Desert 土黄 `#d8a878`、Ice 白蓝 `#cfe8f5`
  - 修 bug：原 `PLANET_COLOR` 键写的是 `Lava`，数据里是 `Vocano` → 火山行星全落默认灰
- 恒星：光谱饱和色（O/B 蓝白、F 白、G 金黄、K 橙、M 橙红）
- 星系视图：`gs = 1.2 + pow(lum,0.3)×2.6` + 纹理收敛（颜色 4%~45% 淡出）+ 亮星叠一枚 `0.30gs` 白核 → 从"白雾团"变"彩色亮点"
- 背景星点调暗（size 1.2 / opacity 0.5）

**恒星外观（2026-10-31 下午，对照游戏星图）**

- 恒星本体从平涂球改为 **ShaderMaterial**（`41c37bc`）：白热核心 `#fffdf2` + 菲涅尔边缘发光 `pow(1-dot,2.2)`
  + 两阶 value noise 表面滴流；`makeStarMaterial(color, uNoise, uCoreEdge)` 参数控制
  （巨星 `noise=0.75 / coreEdge=0.45` 呈斑驳，其余 `0.15 / 0.80`）
- 恒星系视图额外叠 **Canvas 径向渐变日冕 sprite**（`makeSunGlow`）：0% 纯白 → 5% 象牙白 → 12% 纯色 →
  26% 70% → 50% 22% → 78% 8% → 透明，AdditiveBlending
- 特殊恒星配色（`bbdac23`）：**黑洞 `0xd94bff` 紫红光球（不再黑球+描边）**、中子星 `0x9a86ff` 紫蓝、
  白矮 `0xeaf2ff`、巨星大球（`starRadius ×2.4`，M/K 橙红 `0xff6a20` / B/A/O 用光谱色）、O 星青蓝 `0x66c8ff`
- 星系视图：sprite 色晕 `gs = 1.2 + pow(lum,0.3)×2.6` + 亮星（`lum > 0.25`）叠 `0.30×gs` 白核
- 恒星名标签：出生星（白）+ 所有有已建行星（`hasFactory`）的恒星（绿 `#9fe3b8`），其余悬停显示
- **同步移除**（旧文档残留，勿再参考）：`6ly` 线框球壳、规划黄环、巢穴红环、出生星光环

**注意**：`Renderer` 从 `bin\...\Templates` 复制模板，改 `DspView\Templates` 后必须 **重新 build** 再跑 `DspView --galaxy`，否则 web 目录仍是旧版。
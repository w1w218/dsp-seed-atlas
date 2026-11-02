# 星系数据浏览器方案（VIEWER-PLAN）

定稿日期：2026-10-20（方案待确认，暂不开发）

目标：单种子 → 一颗星系的完整数据（恒星/行星/矿脉）→ 生产规划 → 存档融合 → HTML 视图
（平铺行星表 + 3D 星系图）。最终形态是一个"存档信息浏览器"，后续可叠加决策功能。

## 0. 分工与边界

- 现有 `DspSearcher`（搜索器）**不动**：仍负责全空间扫描、产出 results.csv
- 新增能力全部是 CLI，无界面，全部挂在 `src\DspSearcher`（net48 主栈）内，共享 `DspCore48` 权威引擎
- 参考实现：Java 规划原型 `<本地 Java 原型（未随仓库发布）>`（规则改动后续单独讨论）

## 1. 总体架构

```
                 ┌──────────────────────────────────────────┐
                 │  galaxy.json（唯一真相源，全部工具读写它）  │
                 │  meta / stars / planets / veins / plan /   │
                 │  save（存档融合结果）                      │
                 └──────────────────────────────────────────┘
                     ▲            ▲            ▲         │
       S2 初始生成    │   S1 存档   │  S3 规划   │  S4 合并 │  S5 渲染
    DspSearcher     │  DspSeed…  │  DspPlan  │ DspSeed… │  DspView
    --seed N --json   │  --save    │           │ --merge  │  --galaxy → web/
                     │            │           │          ▼
                     │            │           │      index.html + app.js
                     │            │           │      + style.css + three.min.js
```

三个可执行程序（2026-10-20 A2 调整后）：

- **`DspSearcher`**（net48 WPF，引游戏 DLL + DspCore48，**唯一操作栈**）：`--seed` 初始生成 / `--save` 存档解析 / `--merge` 存档融合
- **`DspPlan`**（新 CLI）：生产规划（遗传算法选址，后续加）
- **`DspView`**（新 CLI）：JSON → HTML 渲染（后续加）

> A2 架构（2026-10-20）：`DspSearcher` 从 net8 迁 **net48 + 游戏 DLL** 成为唯一操作栈；
> `DspCore48`（net48 类库）= 权威生成引擎（源：DspFindSeed-3.1 验证版，197/198）；
> `DspFindSeed-3.1` 退为验证基座；`DspSearcher`（net8 复刻）降级为参考。
> 浮点精度边界已修复（2026-10-20）：根因 = `DspSearcher` 进程位宽（AnyCPU 默认 x64/SSE2 vs 验证基座 x86/x87）
> → CalcVerts 顶点 1 ulp → QueryHeight 临界翻转（3301/2403/1502）；`DspSearcher.csproj` 加
> `<PlatformTarget>x86</PlatformTarget>` 后对齐存档（3301 石 1,143,086、198 颗对拍仅剩母星 104），见 `FLOAT-NOTES.md`。

四个环节在方案里叫 S1~S5（见 §4）。**实现顺序（已定）**：
S1 存档解析移植 → S2 初始生成 → S3 规划 → S4 存档融合 → S5 渲染。

> 顺序含义：规划基于"满储量"做选址；存档融合只更新现状（名字/剩余储量/探索标记），
> 不回改已经算好的规划。若将来要"按剩余储量重规划"，重跑 S3 即可（数据都在 JSON 里）。

## 2. galaxy.json 数据模型

单位约定：坐标 = 星系坐标（光年，与 `StarData.position` 同口径）；储量 = 游戏原始 amount（整数）；
时间 = 游戏 tick；矿种编号 = `EVeinType` 1~14（1 铁 2 铜 3 硅 4 钛 5 石 6 煤 7 油 8 可燃冰
9 金伯利 10 分形硅 11 有机晶体 12 光栅石 13 刺笋 14 单极磁）。

```jsonc
{
  "meta": {
    "seed": 39645113, "starCount": 64, "galaxyAlgo": 20200403,
    "resourceMultiplier": 1.0, "starNameLCID": 2052,      // 2052=中文 0=英文
    "generatedAt": "2026-10-20T10:00:00", "generator": "DspSearcher/1.0",
    "save": { "file": "...dsv", "gameName": "新档", "gameTick": 1234567,
              "mergedAt": "..." }                          // 未融合时为 null
  },

  "stars": [{
    "id": 1, "name": "KiffaBoraelis", "nameSource": "save",  // save | namegen | placeholder
    "type": "MainSeqStar", "spectr": "G", "mass": 1.0, "luminosity": 1.006,
    "position": { "x": 0.0, "y": 0.0, "z": 0.0 },
    "distanceFromBirth": 0.0, "resourceCoef": 0.6,
    "planetCount": 4, "special": []                          // ["blackhole","neutron","giant"]
  }],

  "planets": [{
    "id": 101, "starId": 1, "number": 1,
    "name": "KiffaBoraelis 1号星", "nameSource": "save",
    "themeId": 1, "themeName": "地中海", "themeNameEn": "Mediterranean",  // 双写，防止中文名漂移
    "type": "Ocean", "isGasGiant": false,
    "orbitAround": 0, "orbitIndex": 1,                        // orbitAround>0 = 卫星（该值为母行星 id）
    "orbit": { "radius": 1.0, "inclination": 0.0, "longitude": 0.0, "phase": 0.0 },
    "landPercent": 60.05, "temperature": 0.0,
    "scanned": true,                                          // 存档是否已扫描
    "singularities": ["潮汐锁定"]
  }],

  "veins": [{
    "planetId": 101, "type": 1, "groups": 7,
    "initialAmount": 2106543,        // 模型算出的满储量
    "currentAmount": 2106543,        // 存档覆盖后的真实剩余；未扫描时 = initialAmount
    "amountSource": "model"          // model | save
  }],

  "plan": {                          // 实际结构（S3 落地）：对象而非数组
    "meta": { "bestCost": 12672285.9, "solver": "anneal+lns",
              "generatedAt": "...", "breakdown": { "mid": 12432389, "home": 129060, "hydrogen": 110838 } },
    "slots": [{
      "id": "铁块-0", "group": "铁块", "planetId": 2904, "star": "...", "planet": "...",
      "theme": "灰烬冻土", "dist": 4.948,
      "products": [{ "id": 1101, "name": "铁块", "output": 347882.752 }],
      "net": { "1101": 346628.224, "1001": -347882.752 }   // 净额（=承担量−本槽消耗）
    }]
  }
}
```

设计要点：

- **分区块写入**：S2 写 `stars/planets/veins`，S3 只写 `plan`，S4 只改 `name/scanned/currentAmount/save` ——
  各环节互不覆盖，工具可以任意顺序重跑
- **id 是唯一主键**：行星 id = 恒星 id × 100 + 行星序号（与游戏一致），存档与模型就是靠它对齐
- **名字双轨**：`name` 可被存档覆盖（中文真名）；`nameSource` 记录来源，便于判断是否已融合

## 3. 工程结构（2026-10-20 A2 后）

```
src\
├── DspSearcher\                 net48 WPF 主栈（唯一操作入口，已迁移）：
│   ├── Core\                    搜索核心（快筛/精算/评分/断点/UI）——已就绪
│   ├── 引用 DspCore48          权威生成引擎（net48 类库）
│   └── 后续新增（S1~S5 模块/子命令）：
│       ├── GalaxyJson.cs             ← galaxy.json 模型（✅ S2 已建：嵌套结构 meta/stars/planets/veins/plan/save）
│       └── Program.cs                ← 扩展 --json（✅ 已加）/ --save / --merge / --plan / --render 子命令
│       （Naming 不需要——A2 后 DspCore48 引擎自带中文命名 StarGen.StarNameLCID=2052，已由 S1 验证）
├── DspCore48\                  net48 权威生成引擎类库（A2 已建：生成链 + 存档解析）
│   ├── GenApi.cs                     CreateGalaxy/Light/PopulatePlanets/ModelPlanet/VeinsCsv/Save/Init
│   ├── UniverseGen/StarGen/PlanetGen/PlanetManager(0~14)/VeinGen/SaveParser/SaveExport
│   └── prototypes\                   游戏原型 XML（构建复制到输出）
├── DspFindSeed-3.1\           验证基座（A2 后退为验证用，不参与日常）
├── DspPlan\                    新 CLI：规划器（S3，后续建）
└── DspView\                    新 CLI：渲染器（S5，后续建）
```

## 4. 阶段拆解

### S1 存档解析移植（✅ 已完成 2026-10-20）

**实现**（在 A2 架构内直接落地，无需再搬代码——`SaveParser/SaveExport` 已在 `DspCore48`）：

- **CLI**：`DspSearcher.exe --headless --save <dsv> [--out <csv 文件>]`（输出目录自动创建）
  → 产出 `saveX.csv`（恒星级）+ `saveX-planets.csv`（行星级）
- **中文命名默认**：`StarGen.StarNameLCID` 默认 `2052`（中文），`PlanetGen` 行星名按 lcid
  生成（中文用数字序号，英文用罗马数字）→ `--seed` 单种子精算与 `--save` 存档解析
  两侧恒星名/行星名均为中文（同 seed 同命名，可直接印证）
- **字段对齐**（模型与存档两个 CSV 前 12 列同构 + 矿列同名同口径，直接可对拍）：
  ```
  种子,恒星id,恒星名,恒星距离,恒星类型,恒星光谱,行星id,行星名,主题,气巨,卫星母,6ly内,
  {矿名}组,{矿名}量×14（量=amount 原始值，油不再预转换）
  ```
  存档侧追加：`轨道,已扫描,陆海比,原油流速/s`
- **口径**：存档 `amount` = 当前剩余储量（未开采时=初始）；存档只含已扫描行星（如 198/235）

**验收（已通过）**

1. `--seed 1` 与 `--save my-save.dsv` 对拍：198 颗已扫描，**197 颗逐位一致**（含油，
   字段同口径后连油浮点粒度差都消失）；仅母星 104（出生点特殊矿，约定忽略）
2. `--seed 6807826` 与 `--save 6807826-沙盒.dsv` 对拍：200 颗，198 一致；
   剩余母星 103（约定忽略）+ 2101 草原（已知 Slerp 精度边界，见 README）
3. 恒星名/行星名为中文，与游戏内一致

### S2 初始生成 + 命名移植（✅ 已完成 2026-10-25）

**实现**（`--json` 已落地）：

```
DspSearcher --headless --seed N --json out\<seed>\galaxy.json --out <dir>
```

- **`DspSearcher\GalaxyJson.cs`**（新）：嵌套结构序列化 `meta + stars[]（含 planets[]，行星内嵌 veins[]）+ plan[] + save`；
  数据来自单种子精算的内存 galaxy + 行星级矿脉明细（不再从 CSV 转）
- **SingleSeed 重构**：抽 `ModelGalaxy`（建模+矿脉汇总，RefineOne/SingleSeed 共用）——单种子一次完整生成+建模，
  不再二次生成；galaxy.json 从**建模 galaxy** 序列化（landPercent/轨道等字段有效，地中海 58.5% 等真实陆海比）
- **命名**：无需移植 NameGen——DspCore48 引擎自带（`StarGen.StarNameLCID=2052` 中文），恒星/行星名已验证
  （南河一 3号星 等，与 S1 存档解析同源）
- **字段**：恒星 position/资源系数、行星轨道四参数（3D 用）、陆海比、温度、奇观（潮汐锁定等）、14 矿组数+初始储量
- **待补**：`themeNameEn`（引擎 LDB 无英文名源，留空待本地化表/主题数据）

**验收（已通过）**

1. seed=39645113：64 恒星中文名、母星 103 地中海 landPercent 0.611、支撑 87.2h 与精算一致；
   行星 id/矿脉与存档对齐（旧 104 为未魔改随机流时代的过时数据）
2. seed=14629840：地中海 landPercent 0.585、支撑 100.3h；json 矿脉汇总 = results.csv 逐位一致
3. 结构：顶层 meta/stars/plan/save，veins 嵌套于行星；气巨 veins 空数组；plan/save 预留 null/[]

### S3 规划器（✅ 已完成 2026-10-27，重新设计，详见 `PLANNER-DESIGN.md`）

**路线变更（2026-10-25）**：不再“严格同构移植 Java GA”，改为**重新设计**（数据用我们的、模型重写、求解器重选），
分阶段推进：Phase A 基础条件 → Phase B 模型定稿 → Phase C 求解器选型 → Phase D 实现验证。
独立设计文档 `PLANNER-DESIGN.md`（结论以此为准，本节只作索引）。

**当前状态（2026-10-26）**

- Phase A ✅ 收口（A1-A5 敲定，A6 固定布局可延后）：49 制造品（母星 6 + 共星 19 + 附属 5 + 独立 19）+ 18 采集品；
  **40 槽**（独立 27 + 共星 13）+ 母星固定；可建规则（气巨/冰巨/O 星不可建、6 低开阔度主题只分担、母星保护区）
- Phase B ✅ 定稿：趟当量距离口径（同行星 0 / 同系 1 趟 / 跨系 d_ly×1 趟）、依赖树推流、约束、目标函数三类贡献
  （中间品槽间 / 母星 G8 原料 / 氢物流）
- Phase A ✅ / Phase B ✅ / Phase C ✅ / Phase D ✅（2026-10-27 完成）：
  求解器定为**多链退火 + LNS 局部精修**（全规模 CP-SAT 实测不可行）；`DspPlan` 已实现并写回 plan 区块
- 结果（84272982）：**12,672,286**（中间品 / 母星 / 氢 = 12,432,389 / 129,060 / 110,838），1-opt/2-opt 与 window=12 LNS 均无改进
- 详见 `PLANNER-DESIGN.md`（模型）与 `PLANNER-IMPL.md`（实现/命令/实测）

**S4 存档融合 / S5 渲染 —— 待做**

**验收（Phase D）**

1. 小用例（2-3 产品 × 3-4 行星）手算期望解一致
2. 解自洽：同产品多基地总量 ≥ 需求；依赖链逐层供给 ≥ 消耗
3. 与 Java 版同输入对比 minDistance（作为历史基准，允许更好）
4. 回归：S2 双种子 galaxy.json 不变

### S4 存档融合（✅ 已完成 2026-10-27）

**实现**：`DspSearcher\Merge.cs` + `--merge`/`--galaxy` 参数，**直接读 .dsv 一步到位**（不经中间 CSV）：

```
DspSearcher --headless --merge "<my-save.dsv>" --galaxy <…\single-84272982\galaxy.json>
```

- 解析：`SaveParser.ParseHeader` + `FindScannedPlanets`（流式）+ 用存档 seed/lcid 重新生成星系取中文名
- 合并（按行星 id）：`stars[].name`、`planets[].name`（nameSource=save）、`scanned`、
  `landPercent`（存档陆海比）、`veins[].groups/currentAmount`（amountSource=save）、`meta.save`
- **不动**：`plan` 区块、`veins[].initialAmount`（模型初值保留，用于对比开采/模型差异）
- 备份 `galaxy.json.bak-merge` + `merge-report.txt`（已扫描数/名字覆盖/组数冲突/倍率校验）
- 校验：seed/starCount 必须一致；资源倍率不一致时警告；重复 merge 幂等（除 mergedAt）

**实测（my-save，seed 84272982，0.1x）**：已扫描 85/243；恒星名 64、行星名 243 全部落位；
**84/85 行星 currentAmount 与模型初值逐位一致**（唯一差异 = 母星 103 出生点特殊矿，已知）；
组数冲突 2 处（母星 103 石/油，已知）；未扫描 158 颗保持初值；plan 40 槽 / 12,672,286 未受影响；
幂等（除 mergedAt 完全一致）。

> 前置修复（2026-10-27）：核对时发现 `DspCore48\PlanetManager\PlanetAlgorithm.cs` 的
> PA7/PA11/PA12/PA13 四个变体 `isRareResource`/`infiniteResource` 写死 false（10-23 只修了基类）
> → ≤0.1x 档这些主题储量偏高 30~45%（比例 = `0.7 × coef^-0.2`）。已修为与基类同口径，
> 重新生成后 84/85 一致。同步修 `DspFastScan` 引擎（5 处 isRareResource/infiniteResource + amount 倍率 +
> TheoryScorer 极难修正 + MakeDesc 自动推导）；0.1x 单种子复测 vs net48 中位比值 1.005
> （P5/P95 = 0.87/1.20，跨引擎浮点长尾；修复前该类行星系统性 +29%）。

### S5 渲染（HTML + 3D）（✅ 已完成 2026-10-28，实现见 `VIEWER-IMPL.md`）

> 实际落地与本节草案的差异：27 列（恒星 rowspan 合并、开阔度、规划可编辑、备注、黑雾色阶、氢/重氢/可燃冰气）、
> `--serve` 服务模式与 `POST /api/notes`、3D 双视图与联动、火种预警条、3D 显隐开关。以 `VIEWER-IMPL.md` 为准。

**产物目录**（非单文件，数据与视图分离）：

```
out\39645113\
├── galaxy.json          ← 工具链读写
└── web\
    ├── index.html
    ├── app.js
    ├── style.css
    └── three.min.js     ← 本地文件，离线可用
```

**表格**（平铺，全部行星一行一颗，不分层折叠）：

- 恒星级列：恒星星名 / 类型光谱 / 距出生点(ly) / 资源系数 / 行星数
- 行星级列：行星名 / 主题 / 类型 / 气巨·卫星母 / 6ly 内 / 已探索 /
  14 矿 × （组数 / 初始储量 / 剩余储量 / 开采率）
- 规划列：承担基地（物品 + 序号）/ 备注
- 未扫描行星整行灰显，并标注"未探索"
- 交互：列排序、按恒星/主题/矿种筛选、双击行 → 3D 跳转并高亮
- 储量显示做易读换算（亿 / 万；油单独按 /s 显示流速）

**3D**（Three.js，本地 `three.min.js`）：

- **星系视图（默认）**：按 `stars[].position` 摆 64 颗恒星；颜色按光谱；黑洞/中子星/巨星特殊标记；
  恒星名悬浮标签；`distanceFromBirth ≤ 6ly` 高亮；鼠标悬停显示摘要
- **双击恒星 → 进入恒星系视图**：相机过渡到该恒星；显示恒星本体 + 轨道环
  （用 `orbit.radius/inclination/longitude/phase`）+ 行星小球（大小按 radius）；气巨用不同配色
- **ESC 回退**到星系视图（相机过渡回原位）
- 表格与 3D 双向联动：双击表格行 = 进入该行星所在恒星系并高亮该行星
- 性能：64 恒星 + ~240 行星，普通 InstancedMesh/Line 即可，无需特殊优化

**验收**

1. 双击 `index.html`（file://）可直接打开，无需本地服务器（Three.js 走相对路径；JSON 内嵌或同目录 fetch）
2. 表格 242 行完整、排序/筛选可用、未扫描行星灰显
3. 双击恒星进入恒星系、ESC 可回退、表格双击可跳转联动

## 5. 关键坑（实现时注意）

1. **行星 id 对齐**：存档 `astroId` 与模型行星 id 同口径（恒星 id×100+序号）；对齐依据是
   `compare_save_model.py` 已验证的口径，不要另造键
2. **中文主题名**：7 个主题显示名曾修正（红土→黑石盐滩、白色海洋→樱林海、三色→飓风石林、
   红冰→猩红冰湖、草原→热带草原、极寒→极寒冻土、潘多拉→潘多拉沼泽）；JSON 里主题 id 与
   显示名双写，判定逻辑一律用 id，**禁止用中文名做逻辑判断**（Java 原型就是靠中文地形名判定的，
   移植时必须改成 id 或枚举）
3. **石油口径**：`amount/25000 = /s`；对拍与展示都按这个口径
4. **存档版本**：`headerVersion > 7` 或 GameData 版本变化需同步更新解析器
5. **未扫描行星**：无名字、无剩余储量 —— 初始值与"未探索"标记必须保留，不能被融合逻辑清空
6. **坐标系**：恒星是星系坐标（光年）；行星轨道参数是恒星系内单位。3D 两个视图分别用两套坐标
7. **名称连接符**：中文行星名里的连接符/「号星」文本需从本地化表或实际存档确认后再定
8. **Java 侧构建风险**：`dyson` 的父 POM 是私有 `私有父 POM`；
   对拍时优先用本机已编译的 target 产物，不要试图从零 build

## 6. 不做的事（本方案范围外）

- 不改搜索器 `DspSearcher` 的扫描/评分/输出逻辑
- 不做完整产线规划（机器台数/配方平衡/电力/物流塔）——规划目前定位是"基地选址 + 分工"
- 不做存档写入（只读）
- Java 原型规则改动（储量感知、行星可放多基地、需求接配比体系等）留待 S3 开工后专门讨论

## 7. 已定结论（原“待确认”，均已落地）

- 表格列清单：**27 列**定稿（恒星 3 + 行星名/主题/开阔度/规划/距离/备注/黑雾 + 14 矿 + 3 气体），
  见 `VIEWER-IMPL.md` §1
- 规划器模型：已按 `PLANNER-DESIGN.md` 重新设计落地（推流 + 40 槽净额 + 退火 + LNS），不再是 Java 版“一行星一基地”
- 需求输入：已接入矩阵 10万/min + 戴森球配比（`DspPlan` 推流阶段），见 `PLANNER-IMPL.md`
- `--serve`：已实现（`DspView --serve`，HttpListener），用于规划文本写回 `planNote`

# CLI 命令总览（COMMANDS）

`src` 下所有可执行程序的统一命令手册。**找不到命令时先看这里。**

约定：
- 路径均以 `<仓库根>` 为根；`<OUT>` 可自定（如 `<仓库根>\_scratch\out`）
- **x86 vs x64**：`DspSearcher`(net48, **x86**) 是精算入口（浮点必须与游戏一致）；
  存档解析/融合走 **`DspSave`**(net8, x64)，大档（900MB+）在 x86 下会 OOM。
  `DspSearcher --save/--merge` 已内置 256MB 上限：超限直接写日志并 `Exit(2)`，提示改用 `DspSave`
- 构建：先设 `DSP_GAME_DIR`（游戏安装目录），再 `MSBuild src\DspSearcher.sln /p:Configuration=Debug`（VS2022 自带 MSBuild）

## 零、双击入口（日常推荐）

双击 **`src\scripts\DSP.bat`** 进入交互菜单（自动找 pwsh、UTF-8、窗口不闪退）：

```
 1) 初始生成    2) 规划    3) 存档融合    4) 启动/打开网页    5) 一键全流程
 6) 查看状态    7) 停止服务    8) 存档解析 CSV    9) 最近日志    s) 设置
```

同一脚本也支持子命令（供自动化 / 后续网页面板复用）：

```powershell
src\scripts\dsp.ps1 status
src\scripts\dsp.ps1 init  84272982 -Mult 0.1
src\scripts\dsp.ps1 plan  84272982 -Seconds 300 [-Threads N] [-LnsWindow 12] [-LnsRounds 10] [-SkipLns]
src\scripts\dsp.ps1 merge 84272982 -Save "982-Z01-000.dsv"     # 文件名或完整路径均可
src\scripts\dsp.ps1 serve 84272982 [-Port 8765]
src\scripts\dsp.ps1 stop
src\scripts\dsp.ps1 csv   -Save "982-Z01-000.dsv"
src\scripts\dsp.ps1 all   84272982 -Save "982-Z01-000.dsv"
```

- **存档选择**：回车 = 记忆目录下**最新**存档（自动排除 `_autosave*` 与 `_lastexit_.dsv`）；
  只输文件名即可（如 `982-Z01-000.dsv`）；输入完整路径成功后会把该目录记为默认
- **融合前校验**：脚本用 `DspSave --header` 读存档头，种子 / 恒星数与 `galaxy.json` 不一致直接拒绝
- **日志**：按任务分文件（`_scratch\logs\<时间戳>-<动作>.log`），失败时打印末尾 12 行
- 输出根 = `DspSearcher\bin\Debug\net48\search-out`（与 Searcher 默认一致，已有数据直接可见）
- 状态记忆：`_scratch\dsp-state.json`（种子 / 倍率 / 规划秒数 / 线程 / LNS / 存档 / 存档目录）
- **每个 exe 自动以自身 bin 目录为工作目录**（依赖 `prototypes\` / `data\`；手动调用时也必须先 cd 到 bin，否则报 `Could not find Prototypes\ThemeProtoSet.xml`）
- x86 / x64 由脚本固定，无需选择；交互菜单在非控制台（stdin 重定向）下会提示改用子命令

## 一、端到端流程（最常用）

```powershell
$M  = "<仓库根>\src"
$O  = "<仓库根>\_scratch\out"
$SEED = 39645113
$DSV  = "<存档目录>\my-save.dsv"

# 1) 初始生成：星系 → galaxy.json（精算，x86，约 25s/种子）
& "$M\DspSearcher\bin\Debug\net48\DspSearcher.exe" --headless --seed $SEED `
   --resource-multiplier 0.1 --out "$O\single-$SEED" --json "$O\single-$SEED\galaxy.json"

# 2) 规划：选址（退火 + LNS），写回 plan 区块（DspPlan 用 Release 构建）
& "$M\DspPlan\bin\Release\net8.0\DspPlan.exe" search "$O\single-$SEED\galaxy.json" --seconds 300
& "$M\DspPlan\bin\Release\net8.0\DspPlan.exe" lns    "$O\single-$SEED\galaxy.json"
& "$M\DspPlan\bin\Release\net8.0\DspPlan.exe" apply  "$O\single-$SEED\galaxy.json"

# 3) 存档融合：中文名 / 剩余储量 / 已扫描 / 工厂 / 戴森球 / 黑雾（net8 x64）
& "$M\DspSave\bin\Debug\net8.0\DspSave.exe" --merge $DSV --galaxy "$O\single-$SEED\galaxy.json"

# 4) 渲染 + 起服务（http://localhost:8765/，后台无窗口）
Start-Process "$M\DspView\bin\Debug\net8.0\DspView.exe" `
  -ArgumentList "--galaxy `"$O\single-$SEED\galaxy.json`" --serve --port 8765" `
  -WorkingDirectory "$M\DspView\bin\Debug\net8.0" -WindowStyle Hidden
```

> 顺序原则：**初始生成 → 规划 → 存档融合 → 渲染**。规划基于满储量做；存档融合只更新现状，
> 不回改规划（要按剩余储量重规划就重跑第 2 步）。

## 二、`DspSearcher`（net48 x86，精算 / 单种子 / 存档）

```powershell
DspSearcher.exe --headless <参数>          # 无窗口运行（GUI 直接双击则开窗口）
```

| 参数 | 说明 |
|---|---|
| `--seed N` | 单种子精算（不再搜索） |
| `--json <path>` | 配合 `--seed`：输出 galaxy.json（嵌套真相源） |
| `--resource-multiplier N` | 资源倍率（默认 1；0.1/0.5/1/2…） |
| `--out <dir>` | 任务目录（默认 `search-out\<时间戳>`） |
| `--from N --to N` | 搜索区间（默认 0~99999999） |
| `--threads N` | 快筛线程数 |
| `--refine-threads N` | 精算线程数（默认 8，x86 内存受限） |
| `--resume <ckpt>` | 从 checkpoint 续跑 |
| `--save <dsv>` | 存档解析 → CSV（**≤256MB**；超过直接报错退出，大档请用 DspSave） |
| `--space 0/1` | 是否算空间档（默认 1） |
| `--mine 0/1` | 是否算未开采矿组（默认 1） |
| `--merge <dsv> --galaxy <json>` | 存档融合（**≤256MB**；超过直接报错退出，大档用 DspSave） |
| `--debug-gd <dsv>` | 用游戏自身 GameData.Import 读档（需 Unity 环境，通常失败） |

示例：

```powershell
# 单种子初始生成（0.1x 资源）
DspSearcher.exe --headless --seed 84272982 --resource-multiplier 0.1 `
  --out "$O\single-84272982" --json "$O\single-84272982\galaxy.json"

# 全空间搜索
DspSearcher.exe --headless --from 0 --to 99999999 --threads 32 --out "$O\search"
```

产物：`galaxy.json`（`--json`）、`results.csv` / `refined-detail.csv` / `search-report.txt`（搜索）、
`saveX*.csv`（存档）。

## 三、`DspFastScan`（net8 x64，快筛提速）

与 DspSearcher 快筛同参数，**只快筛不精算**（~5.5×），结果供 net48 精算接力：

```powershell
DspFastScan.exe --from 0 --to 99999999 --threads 32 --out "$O\search-fast" --skip-refine
```

额外参数：`--min-o N`（O 星数）、`--near N` / `--far N`（黑洞中子星距离）、
`--radius N`（覆盖半径 ly）、`--ice-giant 0/1`、`--pool-size N`、`--refine-threads N`。

## 四、`DspPlan`（规划器）

数据从 `DspPlan\bin\Release\net8.0\data\`（items.csv / recipes.csv / matrix-100k.md，构建时自动复制）读取；
`<galaxy>` 缺省为 `search-out\single-84272982\galaxy.json`。产物写在 **`bin\Release\net8.0\out\`**（不是当前目录）。

| 子命令 | 作用 |
|---|---|
| （无） | 推流：目标 = 宇宙矩阵 10万/min + 太阳帆 20900 + 火箭 150 + 反物质棒 1.5 → `out\plan_demand.json` / `plan_slots.json` |
| `eval [galaxy]` | 随机布局性能自检（200 次评估） |
| `search [galaxy] --seconds N --threads N --seed N --resume --stagnation S --locked <json> --slot-limit N --pool-limit N` | 多链退火，产出 `out\plan-search.json`（checkpoint）+ `plan_result.json` |
| `lns [galaxy] --window N --rounds N --per-solve N --threads N` | LNS 窗口精修（CP-SAT 局部），更新 checkpoint |
| `apply [galaxy]` | 把最优布局写回 `galaxy.json` 的 `plan` 区块（自动备份 `.bak-plan`）+ `plan-report.txt` |
| `verify [galaxy]` | 1-opt / 2-opt 局部最优校验 |
| `verifyflow [galaxy]` | 运输贪心 vs 精确最小费用流对拍 |
| `cpsat [galaxy] --seconds N --hint <ckpt> --locked <json>` | 全规模 CP-SAT（实测不划算，留作对照） |

```powershell
DspPlan.exe search "$O\single-84272982\galaxy.json" --seconds 600 --threads 16
DspPlan.exe lns    "$O\single-84272982\galaxy.json" --window 12 --rounds 10
DspPlan.exe apply  "$O\single-84272982\galaxy.json"
```

## 五、`DspSave`（net8 x64，存档解析 / 融合）★ 大档必用

```powershell
DspSave.exe --save   <dsv> [--out <csv>] [--space 0/1] [--mine 0/1]   # → saveX*.csv
DspSave.exe --merge  <dsv> --galaxy <galaxy.json>                      # → 写回 galaxy.json
DspSave.exe --header <dsv>                                             # → 单行 JSON（毫秒级，只读头部）
```

- `--header` 输出 `{gameName, seed, starCount, resourceMultiplier, gameTick, starNameLCID}`，
  供脚本做「存档与 galaxy.json 是否同一局」预校验；不加载原型表、不解析行星段，931 MB 档也是毫秒级

- `--merge` 更新：恒星/行星中文名、`scanned`、`landPercent`、`veins[].currentAmount`、
  `spaceLevel`、`collectors`、`hasFactory`、`blackHoleBases`(本地) / `blackHoleBasesRelay`(全局)、
  `stars[].hiveCount` / `dyson`、`meta.save`（含 tick / tinders）
- 不动 `plan` 区块与 `veins[].initialAmount`；备份 `galaxy.json.bak-merge`，报告 `merge-report.txt`
- 实测 931MB 档：merge 8.4s

## 六、`DspView`（渲染 + 服务）

```powershell
DspView.exe --galaxy <galaxy.json> [--serve] [--port 8765]
```

- 无 `--serve`：只生成 `<galaxy 同目录>\web\`（index.html / app.js / style.css / galaxy-data.js / lib）
- `--serve`：本地 HTTP（静态 + `GET /api/galaxy` + `POST /api/notes` 规划文本写回 `planNote`）
- 后台无窗口启动：`Start-Process ... -WindowStyle Hidden`
- 本机 HTTP 代理会拦 localhost：用浏览器直连或 `curl.exe --noproxy '*' http://localhost:8765/`
- 深链：`http://localhost:8765/#star=60` 直达恒星系视图

## 七、常见任务速查

```powershell
# 只看网页（数据没变，重渲染）
DspView.exe --galaxy "$O\single-84272982\galaxy.json" --serve

# 新存档 → 只更新网页（不重跑生成/规划）
DspSave.exe --merge "新存档.dsv" --galaxy "$O\single-84272982\galaxy.json"

# 存档解析看看原始 CSV（行星级/恒星级/工厂/戴森球/黑雾/relay）
DspSave.exe --save "新存档.dsv" --out "$O\save-check.csv"

# 停止后台服务
Get-Process DspView | Stop-Process -Force
```

## 八、产物位置

| 程序 | 产物 |
|---|---|
| DspSearcher 单种子 | `<out>\galaxy.json` |
| DspSearcher 搜索 | `<out>\results.csv`、`refined-detail.csv`、`search-report.txt`、`search-checkpoint.json` |
| DspSearcher / DspSave 存档 | `saveX.csv`（恒星）、`saveX-planets.csv`、`-factory.csv`、`-dyson.csv`（+layers）、`-hive.csv`、`-relays.csv`、`-tinders.csv` |
| DspPlan | `out\plan-search.json`（checkpoint）、`plan_result.json`、`plan-report.txt`、`plan_demand.json`、`plan_slots.json` |
| DspView | `<galaxy 同目录>\web\`（含 `galaxy-data.js`） |

## 九、文档索引

- 机制公式：`mechanics.md`
- 存档二进制格式（权威）：`docs\SAVE-FORMAT.md`；提取规格：`docs\SAVE-EXTRACT.md`
- 搜索器：`docs\README.md`、`docs\SEARCHER-PLAN.md`、`docs\FEASIBILITY.md`
- 规划器：`docs\PLANNER-DESIGN.md`、`docs\PLANNER-IMPL.md`
- 浏览器：`docs\VIEWER-PLAN.md`、`docs\VIEWER-IMPL.md`
- 浮点精度：`docs\FLOAT-NOTES.md`

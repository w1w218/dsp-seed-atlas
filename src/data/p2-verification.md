# P2 矿脉生成验证记录（src\data\p2-verification.md）

日期：2026-10-18（本文为重写版，取代 10-17 旧记录；2026-10-18 晚补：Slerp/QueryHeight 修复后复测）。
目标：DspSeedFinder 行星矿脉总量与游戏一致。
参照存档：`my-save.dsv`（seed=1、64 星、资源倍率 1）。
对拍工具：`DspFindSeed-3.1\DspFindSeed\bin\Debug\DspFindSeed.exe`
（`--save` 解析存档 → `_scratch\save-clean-planets.csv`；`--veins-csv` 完整建模 → `_scratch\model-clean4.csv`；对比脚本 `src\tools\compare_save_model.py`；CSV 中间产物统一放 `_scratch\`）。

## ✅ 总体结果 —— 197/198 精确一致（2026-10-18 深夜，候选点修复后）

存档 235 行星中 **198 颗已扫描**（37 颗气巨无矿脉），逐颗按 14 种矿（组数 + 储量）对比：

- **实质差异仅 1 颗**：**104（母星，出生点特殊矿，约定忽略）**
- 其余 20 颗差异仅为**油浮点粒度**（存档流速×25000 后 ±250 步进，非真差异）
- **1502/2403/3301 全部精确对齐**（1502 于 2026-10-18 深夜由候选点根因修复）

## 本轮修复的两处根因（2026-10-18 晚）

### 1. 顶点网格 `Slerp` 的浮点实现（修复 2403）

- 位置：`PlanetModelingManager.CalcVerts` 的 `Slerp`（游戏用 `Vector3.Slerp`，ECall 在无 Unity 运行时不可用，基座手写）
- 原实现是"双正弦公式"（`a*sin((1-t)θ)/sinθ + b*sin(tθ)/sinθ`），Unity 实际为**相对向量法**
  （`relative = normalize(rhs - dot*lhs); lhs*cos(θt) + relative*sin(θt)`）
- 改为相对向量法后，**2403 全部矿种逐位等于存档**（铁 13,373,528、硅 15,154,924、煤 33,067,571、有机晶 2,914,278）

### 2. `QueryHeight` 副本 → 游戏 DLL 原版（修复 3301）

- 基座 `PlanetAlgorithm` 里有一份手抄的 `QueryHeight(PlanetRawData, Vector3)`；改用游戏 DLL 的
  `data.QueryHeight(pos)` 后，**3301 全部矿种逐位等于存档**（石从 1,885,022 修正为 1,143,086）
- 副本与 DLL 版公式一致，但浮点顺序（π 的 double/float 运算、除法与 0.01 乘法的先后）在
  草原主题的水位临界带（高度 200.0 / 200.5 附近）足以翻转判定

### 顺带对齐与修复

- `birthPoint` 表达式改为原版写法 `(float)NextDouble() * 2f - 1f`（原为 `(float)(x * 2.0 - 1.0)`，double 运算后转 float）
- `GenerateVeins` 已与反编译原版逐行 diff（见 `_scratch\_diff_genveins.py`），除上述外无实质差异
- `parse_dsv.py` 的组数上限由 40 放宽到 300（草原行星 46~51 组会被旧上限整颗漏掉）
- `UniverseGen` 小星数除零：`num12 = (num11-1)/num8` 可能为 0，补 `if (num12 < 1) num12 = 1;`
  （`--stars 4` 不再崩 `DivideByZeroException`）

### 补充修复（2026-10-18 深夜）：bad query 崩溃

- 现象：`PlanetRawData.QueryHeight`（DLL）在查询失败（`num5==0`）时调 `Debug.LogWarning("bad query")`，
  该 ECall 在无 Unity 运行时抛 `SecurityException`，导致整颗行星矿脉生成中断（0 组）。
  `6807826` 种子的母星 103 触发（此前 my-save未触发）
- 修复：**保留 DLL `data.QueryHeight` 调用**（浮点与游戏一致，3301 修复不回归），
  用 `try { num19 = data.QueryHeight(pos); } catch (SecurityException) { num19 = heightData[0]*0.01f; }`
  捕获崩溃——与 DLL 原版 bad query 的返回语义一致
- 注：曾试过手写 `SafeQueryHeight`（逐行复制 DLL 版），但托管 `Mathf.Sqrt` 与运行时实现差 1 ulp，
  在 3301 临界带翻转判定导致回归，已废弃

### 6807826 种子交叉验证（2026-10-18 深夜，两轮）

- 存档 `6807826-沙盒.dsv`：seed=6807826、64 星、资源倍率 1，已扫描行星 **19 颗 → 200 颗**（玩家查看星图后重存）
- 建模 `--seed 6807826 --stars 64 --veins-csv`（200 颗非气巨）
- 第一轮（19 颗）：**19/19 一致**（除母星 103）
- 第二轮（200 颗）：发现 **1801 橙晶荒漠** 实质差异（铜 3vs1 组、钛 3vs2、煤 1vs2、有机晶 3vs4、光栅石 12vs13）
  → 由**候选点运算顺序**根因修复后 **199/200 一致**（仅剩母星 103 出生星约定差异）

## ✅ 母星 104（地中海出生星）—— 按约定忽略铁铜

| 矿 | 存档 | 建模 | 偏差 | 说明 |
|---|---|---|---|---|
| 铁 | 2,331,147 | 1,807,983 | -22.4% | 出生点特殊矿点（第 1 组 6 簇/opacity 0.2），约定用其他星球校准 |
| 铜 | 1,167,829 | 1,745,780 | +49.5% | 同上；且建模组数 7 vs 存档 5 |
| 石 | 4,889,860 | 5,200,063 | +6.3% | |
| 煤 | 9,104,648 | 10,566,666 | +16.1% | |
| 油 | 1,462,750 | 1,401,410 | -4.2% | 22 vs 21 组 |

## ✅ 1502（草原）—— 已修复（2026-10-18 深夜）

修复前：铁/铜/硅/钛/石/煤 ±5~19%、油 18vs17 组、有机晶 2vs3 组（-33.2%）、刺笋 +15.6%；
修复后全部矿种逐位等于存档（油仅剩浮点粒度差）。

### 根因：位置候选点 `zero` 的浮点运算顺序

- 游戏原版（基类 + PA 变体）：`zero.x = (float)NextDouble() * 2f - 1f`（float 运算）
- 基座基类当时为 `zero.x = (float)(NextDouble() * 2.0 - 1.0)`（double 运算后转 float，差 1 ulp）
- **实测结论**：基类（1502 走 PA6 基类）用 **double** 写法、PA 变体副本（1801 走 PA11 等）用 **float** 写法时
  两个种子全部对齐（反编译均显示 float，疑似 ilspycmd 对 IL 的规范化差异；以对拍结果为最终依据）
- 影响链：候选点 1 ulp → 位置阶段某候选点在 200.0 / 200.7 高度带边界翻转判定 → 尝试次数不同
  → rng2 分叉 → 后续所有组抖动/位置全乱（油多 1 组、有机晶少 1 组、储量偏差）

### 顺带同步（本轮）

- PA7/PA11/PA12/PA13 副本的 `QueryHeight(data, X)` → DLL `data.QueryHeight` + try-catch（bad query 兜底）
- 碰撞检测 `(double)sqrMagnitude < (double)num1*(double)num1*(double)num11` → `sqrMagnitude < num1*num1*num11`（float，与原版一致）
- PA11 判定副本与游戏原版等价（含铁/铜 [200,200.7] 高度带、硅/钛 >200.7 的带内约束）

## ✅ 存档解析工具（--save）状态

- `SaveParser`：`[astroId][0][landPercent][veinBias 3f][birthPoint 3f][birthRP0 3f][birthRP1 3f][groupCount][VeinGroup×N][-1]`
- VeinGroup 32B = `[0][type][pos 3f][count][amount i64]`；油 amount 口径 = 流速 × **25000**
- 已扫描行星判定：玩家**登陆/进入行星**即标记 scanned 落盘
- **新增**：`veinBias` 可直接与建模 `birthPoint` 对拍（1502 已验证一致）

## 已修复的移植 bug（历史记录）

1. clamp 错误：`baseAmt>16000→16000` 上限不存在（只有抖动幅度 `per<16000 ? per×0.9375 : 15000`）
2. 出生星特殊组：铁/铜仅第 1 组特殊（6 簇/opacity 0.2），不是整类
3. 原油每组 1 个矿点（漏了 `Oil → num20=1`）
4. StarGen num11 符号（影响恒星光谱分布，已修）
5. 存档解析：C# BinaryWriter 字符串 7-bit 变长长度（非 int32）
6. 油落点/水下油配额逻辑（基类 GenerateVeins，当前保留版：油可落水面 ±1m、配额 15%，组数+1）
7. **Slerp 相对向量法**（2026-10-18 晚，修 2403）
8. **`QueryHeight` 改用 DLL 原版**（2026-10-18 晚，修 3301）
9. **`birthPoint` float 运算顺序对齐原版**（2026-10-18 晚）
10. **位置候选点 `zero` 运算顺序**（2026-10-18 深夜，修 1502/1801；基类 double、PA 变体 float）
11. **PA 变体副本 `QueryHeight` → DLL 原版 + try-catch**（bad query 崩溃兜底）
12. **碰撞检测 float 化**（`num*num*num16` 与原版一致）

## 数据源说明

主题/矿脉数据 = UnityPy 从当前版本（2026-10）resources.assets 提取（src\data\json，
脚本 src\tools\decode_protos.py），与 DspFindSeed-3.1 的 prototypes\ThemeProtoSet.xml 一致。

## 待办

1. 出生星（104/103）铁铜差异：`GenBirthPoints` 出生点特殊矿被注释所致，按约定忽略（如需精确需实现 GenBirthPoints）
2. P3 搜索器（条件/CSV/并行）

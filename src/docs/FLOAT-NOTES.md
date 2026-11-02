# 3301 浮点差异排查记录（DspCore48 vs DspFindSeed）——已定位已修复

日期：2026-10-20。状态：**根因已定位并修复**（DspSearcher 进程位宽错误）。

## 结论（一句话）

`DspSearcher`（SDK 风格 csproj，AnyCPU 且未设 `Prefer32Bit`）在 64 位系统上实际运行于
**x64 进程（SSE2 浮点）**；`DspFindSeed`（经典 csproj，AnyCPU + Prefer32Bit）运行于
**x86 进程（x87 浮点）**。同一个 Slerp/CalcVerts IL 在两种浮点路径下产生
**1~4 ulp 顶点差异** → QueryHeight 插值临界判定翻转 → 矿脉落点 rnd2 错位 → 储量偏差。

**修复：`DspSearcher.csproj` 加 `<PlatformTarget>x86</PlatformTarget>`**（一行），
DspSearcher 回归 x86 进程后与 DspFindSeed/游戏/存档完全一致。

> 此前 FLOAT-NOTES 旧版记录的"宿主进程 JIT 差异 ±1 ulp、机制未定位、建议接受"结论
> **已过时**。真正的根因是进程位宽（x64 vs x86），且旧版"进程位数均 x86"的判断是错的
> （未验证运行时位宽，AnyCPU 在 64 位 OS 默认 x64）。

## 排查过程（逐层排除，全部可复现）

### 现象

- DspFindSeed-3.1（x86，与游戏/存档一致，权威）：seed=1 的 3301（草原）石 = **1,143,086**
- DspSearcher（A2 迁移后）：3301 石 = 1,885,022（组数一致，仅储量差）
- 同类受影响行星：2403、1502 等；**组数 100% 一致，高度场（heightData）全量一致**

### 证据链（全部实测）

1. **DLL 哈希一致**：DspSearcher bin / DspFindSeed bin / 游戏 Managed 三处
   `Assembly-CSharp.dll` SHA-256 均 `EF314425DADE51F5...`（README 记录值）
2. **IL 一致**：ilspycmd `-il` 对比 DspCore48.dll vs DspFindSeed.exe 全模块，
   `PlanetAlgorithm.GenerateVeins`（1959 行）、`PlanetModelingManager.CalcVerts`（624 行）、
   `Slerp`（80 行）归一化命名空间后**逐行一致**（仅 RVA 注释行不同）
3. **源码一致**：`PlanetModelingManager.cs` 两项目 diff 0 差异
4. **全量数组 dump 对比（3301）**（探针已清理，产物在 `_scratch\s2-probe\`）：
   - `heightData`：**161,604/161,604 全一致**
   - `vertices`：**30,130/161,604 个顶点差 1~4 ulp**（多为 y 分量差 1，如 `0xbcc10802 vs 0xbcc10801`）
   - `indexMap`：14/60,000 差 1
   - → 差异源头是 **CalcVerts 生成的顶点网格**，不是 QueryHeight 本身
5. **Math 函数一致**：两进程 `Math.Acos(0.5)/Math.Cos/Sin`（double 与 float 转换）hex 逐位相同
6. **JIT 优化无关**：`COMPLUS_JITMinOpts=1` 后 vertices 差异不变（30,130 处、hex 全同）
7. **FPU 控制字不同（伴生现象）**：x64 进程 `_controlfp=0x0008001F`（24 位路径），
   x86 进程 `0x0009001F`；`_controlfp(0x9001F)` 恢复对 x64 无效（SSE2 不读 x87 控制字）→
   控制字差异是位宽的**结果**不是原因
8. **运行时位宽实锤**：进程内 `Environment.Is64BitProcess` ——
   DspSearcher = **True**（x64），DspFindSeed = **False**（x86）
9. **游戏本体位宽**：`DSPGAME.exe` PE Machine = `0x8664`（x64）——
   但 Unity 原生 `Vector3.Slerp` 的数值行为恰好等价于 **x86/x87 路径**，
   因此 DspFindSeed（x86）复现了游戏结果，DspSearcher（x64/SSE2）差 1 ulp

### 为什么高度场不受影响

`GenerateTerrain` 的输出是 ushort 离散高度（取整），1 ulp 的顶点方向差异不足以改变
取整结果 → `landPercent`（86.16%）与高度表全量一致。只有**临界判定**（矿脉落点的
`QueryHeight ≥ radius±1.0`、插值窗口 `sqrMagnitude > num4`）踩在 1 ulp 边界上的行星
（2403/3301/1502）才显性出错。

## 修复

`DspSearcher\DspSearcher.csproj`：

```xml
<TargetFramework>net48</TargetFramework>
<PlatformTarget>x86</PlatformTarget>   <!-- 修复：AnyCPU 默认 x64 → 显式 x86 -->
```

防御性保留（GenApi.cs）：

- `GenApi.Init()` 与 `GenApi.ModelPlanet()` 开头各一行
  `_controlfp(0x0009001F, 0xFFFFFFFF);`（x86 下恢复 53 位精度控制字，防止未来
  图形/媒体初始化改 FPU 控制字）

## 验证（x86 修复后，seed=1）

- `vertices` 与 DspFindSeed **0 差异**（161,604 全等）；`heightData` 0 差异
- 3301 石矿 = **1,143,086**（= 存档权威值）；2403/1502 同步消失
- 全星系对拍（198 已扫描行星）：组数不一致仅 **母星 104** 3 处（出生点特殊矿，
  约定忽略）；储量 >2% 仅母星 104 5 处（铁 -22.4%/铜 +49.5%/石 +6.3%/煤 +16.1%/
  油 -4.2%，与 P2 历史结论一致）；其余微小差 = 已知油浮点粒度（±0.01% 内）
- 6807826-沙盒复测（提交前）：200 已扫描，198 精确一致；剩余母星 103（约定忽略）+
  2101 草原（有机晶 4vs2 组/-51.9%，DspFindSeed 模型侧同样存在，非本次修复引入，
  1502 类生成边界差异待排查，见 docs\README.md）

## 遗留

- 母星 104（出生点特殊矿，P2 起约定忽略）
- 油储量 F2 精度粒度（存档侧流速×25000 反算的 ±125 舍入，非模型问题）

## 复现探针（历史诊断脚本，已清理，未随仓库发布）

- `s2-compare.py`：模型 refined-detail.csv vs 存档行星表对拍
- `s2-cmpdump2.py`：core/dfs 全量 vertices+heightData+indexMap 对比
- `s2-pecheck.py`：PE Machine/CorFlags 检查
- dump 产物：本地临时目录 `_scratch\s2-probe\`（修复前后各一份，已清理）

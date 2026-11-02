# DSP 存档解析（src\tools\parse_dsv.py + DspFindSeed-3.1 --save）

> ⚠️ **历史文档（2026-10-21）**：存档结构知识已沉淀到 `docs\SAVE-FORMAT.md`（权威参考）；
> 权威解析器为 `DspCore48\SaveParser.cs`（流式+多线程，`DspSearcher --save`），本页为早期 python 解析笔记。

用途：读取 `.dsv` 存档，验证 DspSeedFinder 生成器的结果（星表/陆海比/矿脉储量）。

## 两条实现路线

- `src\tools\parse_dsv.py`：纯 Python 解析（头部/GameData/已扫描行星段特征扫描），早期验证用（历史迭代 parse_dsv2~4 已归入 `_scratch\`）
- `DspFindSeed-3.1\DspFindSeed\SaveExport.cs`（`--save`）：C# 实现，解析 + 用存档 seed 重新生成星系，
  导出恒星级/行星级 CSV（与 `seed1_single.csv` 同构），**当前对拍主工具**

## 存档格式要点（已逆向确认）

```
头部: account + GameData
  Header: [6+8][ver int][ver>=7: 2][12][ver>=6: 4][16][nshot int][nshot bytes]
  account: [4][4][8][s7(...)][8]
  GameData: [gdVer][patch][4][4][8][s7][gameName s7] gameDesc: [galaxyAlgo][galaxySeed][starCount]
            [resourceMultiplier][n themeIds][gameTick u64] ...
已扫描行星段（步长 2 全文件扫描）:
  [astroId][0][landPercent f][veinBias 3f][birthPoint 3f][birthRP0 3f][birthRP1 3f]
  [groupCount][VeinGroup × (groupCount-1) × 32B][...]
  VeinGroup(32B): [z int=0][veinType int(1-14)][pos 3f][count 簇数][amount i64 真实储量]
```

- 字符串为 C# BinaryWriter 7-bit 变长（非 int32）
- 油 amount 口径：流速 /s = amount / **25000**（已用 104 母星 58.51/s、702 水世界 87.62/s 验证）

## 验证结论（my-save.dsv，2026-10-20 复现）

- ✅ **galaxySeed=1、starCount=64、resourceMultiplier=1** 与生成器完全一致
- ✅ **已扫描行星 198 颗**（235 行星中；37 颗气巨无矿脉）——玩家登陆/进入行星即标记 scanned 落盘
- ✅ 陆海比逐颗对照游戏面板一致（地中海出生星 60.05%、黑洞星 98.87%、水世界 0.28%）
- ✅ **矿脉对拍：197/198 颗组数+储量完全一致**（20 颗仅油浮点粒度差；对比脚本 `src\tools\compare_save_model.py`）
  ——注意：此为 **DLL 版（DspFindSeed-3.1）** 建模 vs 存档（2026-10-18 P2 结论，2026-10-20 复现）；
  解析器两次独立运行哈希一致，存档/dLL 侧可信
- ⚠️ 剩余差异：仅母星 104（出生点特殊矿，约定忽略）
- 📌 **纯复刻 DspSeedFinder 对拍状态（2026-10-20）**：主题 198/198、组数 2770/2772（仅 1502 差 1）、
  储量中位一致但长尾 ±80%（浮点 1 ulp 边界），详见 `src\docs\README.md`
- 📌 **2026-10-20 A2**：存档解析已随 `DspCore48`（net48 类库，含 SaveParser/SaveExport）提供，
  `DspSearcher`（net48 WPF）可直接调用；DspCore48 与 DspFindSeed-3.1 存在 QueryHeight ±1 ulp
  （组数全对、储量个别行星 ±5%），详见 `src\docs\FLOAT-NOTES.md`

## 使用方法

> 生成的 CSV 属可再算中间产物，统一输出到 `_scratch\`（git 忽略目录），避免污染正式仓库。

```bash
# 存档解析（生成 saveX.csv 恒星级 + saveX-planets.csv 行星级）
DspFindSeed.exe --save "<存档目录>\my-save.dsv" --out <仓库根>\_scratch\save-clean.csv

# 完整建模（198 行星，14 矿种 组数+储量）
DspFindSeed.exe --seed 1 --stars 64 --veins-csv <仓库根>\_scratch\model-clean.csv

# 对拍
python src\tools\compare_save_model.py _scratch\save-clean-planets.csv _scratch\model-clean.csv
```

## 限制

- 未扫描行星无存档数据（存档只记录玩家登陆过的行星；198/235）
- 存档 amount = 当前剩余储量（未开采时=初始；开采后变小）——对拍用未开采行星或开盘新档
- 存档版本升级（headerVersion>7 或 GameData 版本变化）需同步更新解析器

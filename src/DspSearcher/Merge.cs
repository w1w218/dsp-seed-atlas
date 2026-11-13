using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DspCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DspSearcher
{
    /// <summary>S4 存档融合：.dsv → galaxy.json（一步到位，不经过中间 CSV）。
    /// 只写：stars[].name / planets[].name（存档中文名）、scanned、landPercent（存档陆海比）、
    /// veins[].groups / currentAmount（存档当前剩余储量）、meta.save。
    /// 不动：plan 区块、veins[].initialAmount（模型初值保留）、轨道/主题/坐标。</summary>
    public static class Merge
    {
        public static int Run(string dsvPath, string galaxyJsonPath, Action<string> log = null)
        {
            void L(string m) => log?.Invoke(m);

            if (!File.Exists(dsvPath)) { L($"存档不存在: {dsvPath}"); return 1; }
            if (!File.Exists(galaxyJsonPath)) { L($"galaxy.json 不存在: {galaxyJsonPath}"); return 1; }

            // ---- 1. 存档头 + 已扫描行星 ----
            SaveParser.SaveDesc sd;
            using (var fs = new FileStream(dsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                sd = SaveParser.ParseHeader(fs);
            if (sd == null) { L("存档头解析失败"); return 1; }
            L($"存档: {sd.gameName}  seed={sd.galaxySeed} 恒星={sd.starCount} 倍率={sd.resourceMultiplier} tick={sd.gameTick}");

            var scanned = SaveParser.FindScannedPlanets(dsvPath, sd.searchFrom);
            var byId = new Dictionary<int, SaveParser.ScannedPlanet>();
            foreach (var p in scanned) byId[p.astroId] = p;
            L($"已扫描行星: {scanned.Count}");

            // ---- 2. galaxy.json ----
            var root = JObject.Parse(File.ReadAllText(galaxyJsonPath));
            var meta = (JObject)root["meta"];
            int seed = meta.Value<int>("seed");
            if (seed != sd.galaxySeed) { L($"种子不一致: json={seed} 存档={sd.galaxySeed}"); return 1; }
            int starCount = meta.Value<int>("starCount");
            if (starCount != sd.starCount) { L($"恒星数不一致: json={starCount} 存档={sd.starCount}"); return 1; }
            float mult = meta.Value<float>("resourceMultiplier");
            bool multMismatch = Math.Abs(mult - sd.resourceMultiplier) > 0.001f;
            if (multMismatch)
                L($"警告: 资源倍率不一致 json={mult} 存档={sd.resourceMultiplier}（initialAmount 与存档不可比）");

            // ---- 3. 重新生成星系（只为取名字与行星 id 对应；不建模地形/矿脉） ----
            GenApi.EnsureInit();
            var gd = new DspCore.GameDesc();
            gd.SetForNewGame(DspCore.UniverseGen.algoVersion, sd.galaxySeed, sd.starCount, 1, sd.resourceMultiplier);
            DspCore.StarGen.StarNameLCID = sd.starNameLCID;
            var galaxy = DspCore.UniverseGen.CreateGalaxy(gd);
            if (galaxy == null) { L("CreateGalaxy 失败"); return 1; }
            var starById = galaxy.stars.ToDictionary(s => s.id);
            var planetById = new Dictionary<int, PlanetData>();
            foreach (var s in galaxy.stars)
                foreach (var p in s.planets)
                    planetById[p.id] = p;

            // ---- 4a. 存档附加数据：工厂（已建/采集器/黑雾基地）、巢穴、戴森球、空间段（火种） ----
            var validIds = new HashSet<int>(planetById.Keys);

            // 玩家重命名（GamePrefsData.astroNameOverride）：恒星 key = starId×100，行星 key = planetId
            Dictionary<int, string> nameOverride = null;
            try { nameOverride = SaveParser.FindAstroNameOverrides(dsvPath); L($"重命名: {nameOverride?.Count ?? 0} 条"); }
            catch (Exception e) { L($"重命名解析失败（忽略）: {e.Message}"); }
            Dictionary<int, FactorySummary> facByPlanet = null;
            try
            {
                var fl = FactoryExtractor.Extract(dsvPath, validIds);
                if (fl != null) facByPlanet = fl.GroupBy(f => f.PlanetId).ToDictionary(g2 => g2.Key, g2 => g2.First());
                L($"工厂段: {fl?.Count ?? 0} 个 factory");
            }
            catch (Exception e) { L($"工厂段提取失败（忽略）: {e.Message}"); }

            List<HiveExtractor.HiveSummary> hives = null;
            try { hives = HiveExtractor.Extract(dsvPath, sd.starCount, validIds); L($"黑雾: {hives?.Count ?? 0} 个巢穴"); }
            catch (Exception e) { L($"黑雾提取失败（忽略）: {e.Message}"); }

            List<DysonSummary> dysons = null;
            try { dysons = DysonExtractor.Extract(dsvPath, sd.starCount, galaxy); L($"戴森球: {dysons?.Count ?? 0} 个"); }
            catch (Exception e) { L($"戴森球提取失败（忽略）: {e.Message}"); }

            List<SpaceSectorExtractor.AstroPos> astros = null;
            List<SpaceSectorExtractor.EnemyPos> enemies = null;
            try
            {
                var t = SpaceSectorExtractor.Extract(dsvPath, sd.starCount);
                astros = t.Astros; enemies = t.Enemies;
            }
            catch (Exception e) { L($"空间段提取失败（忽略）: {e.Message}"); }

            // 巢穴按恒星聚合；relay 活跃投放按行星聚合（远程口径）
            var hiveByStar = new Dictionary<int, int>();
            var relayByPlanet = new Dictionary<int, int>();
            if (hives != null)
                foreach (var h in hives)
                {
                    if (h.IsSpaceHive && h.IsAlive)
                    {
                        hiveByStar.TryGetValue(h.StarId, out int hc0);
                        hiveByStar[h.StarId] = hc0 + 1;
                    }
                    foreach (var r in h.Relays)
                        if (r.TargetAstroId > 0 && r.BaseState == 1 && r.Stage == 2)
                        {
                            relayByPlanet.TryGetValue(r.TargetAstroId, out int rc0);
                            relayByPlanet[r.TargetAstroId] = rc0 + 1;
                        }
                }

            var dysonByStar = new Dictionary<int, DysonSummary>();
            if (dysons != null)
                foreach (var d in dysons)
                    if (d.StarIndex >= 0 && d.StarIndex < galaxy.stars.Length)
                        dysonByStar[galaxy.stars[d.StarIndex].id] = d;

            // 火种：ETA = 距离 / 1200 m/s / 60；1 ly = 2,400,000 m
            var tinderList = new JArray();
            if (hives != null && astros != null && enemies != null)
            {
                var astroById = astros.GroupBy(a => a.AstroId).ToDictionary(g2 => g2.Key, g2 => g2.First());
                var enemyById = enemies.Where(e2 => e2.DfTinderId > 0)
                                       .GroupBy(e2 => e2.EnemyId).ToDictionary(g2 => g2.Key, g2 => g2.First());
                foreach (var h in hives)
                    foreach (var tr in h.Tinders)
                    {
                        double dist = 0; double? eta = null;
                        if (enemyById.TryGetValue(tr.EnemyId, out var ep) &&
                            astroById.TryGetValue(tr.TargetHiveAstroId, out var tp))
                        {
                            double dx = tp.X - ep.X, dy2 = tp.Y - ep.Y, dz = tp.Z - ep.Z;
                            dist = Math.Sqrt(dx * dx + dy2 * dy2 + dz * dz);
                            eta = dist / 1200.0 / 60.0;
                        }
                        tinderList.Add(new JObject
                        {
                            ["enemyId"] = tr.EnemyId,
                            ["originHive"] = tr.OriginHiveAstroId,
                            ["targetHive"] = tr.TargetHiveAstroId,
                            ["direction"] = tr.Direction,
                            ["stage"] = tr.Stage,
                            ["distanceLy"] = Math.Round(dist / 2400000.0, 3),
                            ["etaMinutes"] = eta.HasValue ? Math.Round(eta.Value, 1) : (double?)null,
                        });
                    }
                L($"火种: {tinderList.Count} 个");
            }

            // ---- 4. 合并 ----
            int nameStars = 0, namePlanets = 0, scannedN = 0, groupConflicts = 0;
            var conflicts = new List<string>();
            foreach (var st in root["stars"].Cast<JObject>())
            {
                int sid = st.Value<int>("id");
                if (nameOverride != null && nameOverride.TryGetValue(sid * 100, out var ovStar))
                {
                    st["name"] = ovStar;
                    st["nameSource"] = "override";
                    nameStars++;
                }
                else if (starById.TryGetValue(sid, out var sdata) && !string.IsNullOrEmpty(sdata.name))
                {
                    st["name"] = sdata.name;
                    st["nameSource"] = "save";
                    nameStars++;
                }
                // 显式归零：merge 只覆盖有值的字段，旧值不会自动清（599 现场：巢穴已清但仍显示 3）
                st["hiveCount"] = hiveByStar.TryGetValue(sid, out int hcStar) ? hcStar : 0;
                if (dysonByStar.TryGetValue(sid, out var dy))
                {
                    // 层状态只区分「空层 / 有建造」（壳填充度需读 nodecps，暂时不算）：
                    // 节点+框架全建也不等于完成（壳体可能未打完），见 2026-10-30 用户反馈
                    int empty = 0, built = 0;
                    foreach (var ly in dy.Layers)
                    {
                        if (ly.NodeTotal + ly.FrameTotal + ly.ShellTotal == 0) empty++;
                        else built++;
                    }
                    st["dyson"] = new JObject
                    {
                        ["layers"] = dy.LayerCount,
                        ["empty"] = empty,
                        ["built"] = built,
                        ["rocketsInFlight"] = dy.RocketInFlight,
                    };
                }
                else st["dyson"] = null;

                foreach (var pl in ((JArray)st["planets"]).Cast<JObject>())
                {
                    int pid = pl.Value<int>("id");
                    PlanetData pdata;
                    bool hasPlanetData = planetById.TryGetValue(pid, out pdata);
                    bool isScanned = byId.TryGetValue(pid, out var sp) && sp != null;
                    if (nameOverride != null && nameOverride.TryGetValue(pid, out var ovPlanet))
                    {
                        pl["name"] = ovPlanet;
                        pl["nameSource"] = "override";
                        namePlanets++;
                    }
                    else if (hasPlanetData && !string.IsNullOrEmpty(pdata.name))
                    {
                        pl["name"] = pdata.name;
                        pl["nameSource"] = "save";
                        namePlanets++;
                    }

                    // 工厂段（仅玩家造访过的行星有）：已建 / 采集器 / 本地面雾基地
                    FactorySummary fac = null;
                    if (facByPlanet != null) facByPlanet.TryGetValue(pid, out fac);
                    if (fac != null)
                    {
                        pl["hasFactory"] = fac.EntityCount > 0;
                        pl["collectors"] = fac.ProtoIdCount.TryGetValue(2105, out int cc) ? cc : 0;
                        pl["blackHoleBases"] = fac.GroundBases;
                    }
                    else if (isScanned)
                    {
                        // 已扫描但无工厂段（气巨 / 未造厂）：显式清零，避免上次 merge 的旧值残留
                        pl["hasFactory"] = false;
                        pl["collectors"] = 0;
                        pl["blackHoleBases"] = 0;
                    }

                    // 黑雾 relay 口径：有活跃记录写实际数；
                    // 只有“确认清理”（有 factory 段且 bases==0）才归零；
                    // 其余（未登陆/没造过东西）保持原值，不臆造 0
                    if (relayByPlanet.TryGetValue(pid, out int rb)) pl["blackHoleBasesRelay"] = rb;
                    else if (fac != null && fac.GroundBases == 0) pl["blackHoleBasesRelay"] = 0;

                    // 气巨/冰巨气体（每采集站速率 /s，已乘恒星资源系数^0.3；采集站上限 40）
                    if (hasPlanetData && pdata.gasItems != null && pdata.gasItems.Length > 0)
                    {
                        var gi = new JArray(); var gs = new JArray();
                        float starCoef = starById.TryGetValue(sid, out var s0) ? s0.resourceCoef : 1f;
                        double coef = Math.Pow(starCoef, 0.3);
                        for (int gg = 0; gg < pdata.gasItems.Length; gg++)
                        {
                            gi.Add(pdata.gasItems[gg]);
                            gs.Add(Math.Round(pdata.gasSpeeds[gg] * coef, 6));
                        }
                        pl["gas"] = new JObject { ["items"] = gi, ["speeds"] = gs };
                    }

                    pl["scanned"] = isScanned;
                    if (!isScanned) continue;
                    scannedN++;
                    pl["landPercent"] = Math.Round(sp.landPercent, 4);   // 存档陆海比（0~1，与 JSON 原口径一致）

                    // 开阔度（空间档 1~10）：存档矿组方向 + 行星物理半径（radius×1200m）
                    if (sp.groups.Count > 0 && pdata != null)
                    {
                        var dirs = new System.Collections.Generic.List<Vector3>();
                        foreach (var g in sp.groups)
                            if (g.veinType >= 1 && g.veinType <= 14)
                                dirs.Add(new Vector3(g.x, g.y, g.z));
                        var sr = SpaceAnalyzer.Analyze(dirs, pdata.radius * 1200.0);
                        pl["spaceLevel"] = sr.Level;
                    }

                    // 聚合存档矿组：组数 = 该类型组条数，储量 = Σ amount
                    var grp = new int[15];
                    var amt = new long[15];
                    foreach (var g in sp.groups)
                        if (g.veinType >= 1 && g.veinType <= 14)
                        {
                            grp[g.veinType]++;
                            amt[g.veinType] += g.amount;
                        }

                    if (!(pl["veins"] is JArray veins)) continue;
                    // 工厂段有矿脉运行时副本（veinPool）时优先：amount = 当前剩余，minerCount 区分已/未规划采集
                    FactorySummary facV = null;
                    if (facByPlanet != null && facByPlanet.TryGetValue(pid, out var fv0)) facV = fv0;
                    foreach (var v in veins.Cast<JObject>())
                    {
                        int t = v.Value<int>("type");
                        if (t < 1 || t > 14) continue;
                        int mg = v.Value<int>("groups");
                        if (mg != grp[t])
                        {
                            groupConflicts++;
                            conflicts.Add($"行星 {pid} {pl.Value<string>("name")} · {SaveParser.VeinNames[t]}: 组 模型{mg} / 存档{grp[t]}");
                        }
                        // 工厂段存在但该矿种的运行时副本为空（解析缺失）→ 回退存档值，避免误写 0（显示成采空）
                        bool useFactory = facV != null && !(facV.VeinAmount[t] == 0 && amt[t] > 0);
                        if (useFactory && facV.VeinGroupCount[t] == 0 && grp[t] > 0) useFactory = false;

                        v["groups"] = useFactory ? facV.VeinGroupCount[t] : grp[t];
                        v["currentAmount"] = useFactory ? facV.VeinAmount[t] : amt[t];
                        if (useFactory)
                        {
                            v["plannedAmount"] = facV.VeinPlanned[t];
                            v["unplannedAmount"] = facV.VeinUnplanned[t];
                            v["amountSource"] = "factory";
                        }
                        else
                        {
                            // 无工厂段（或运行时副本缺失）：无采矿机 → 整块储量计入未规划采集
                            v["plannedAmount"] = 0L;
                            v["unplannedAmount"] = amt[t];
                            v["amountSource"] = "save";
                        }
                    }
                }
            }

            // ---- 5. meta.save ----
            meta["save"] = new JObject
            {
                ["file"] = Path.GetFileName(dsvPath),
                ["gameName"] = sd.gameName,
                ["gameTick"] = sd.gameTick,
                ["resourceMultiplier"] = sd.resourceMultiplier,
                ["scannedPlanets"] = scannedN,
                ["mergedAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["tinders"] = tinderList,
            };

            // ---- 6. 备份 + 写回（无 BOM） ----
            string bak = galaxyJsonPath + ".bak-merge";
            File.Copy(galaxyJsonPath, bak, true);
            File.WriteAllText(galaxyJsonPath, root.ToString(Formatting.Indented), new UTF8Encoding(false));

            // ---- 7. 报告 ----
            int totalPlanets = root["stars"].Sum(s => ((JArray)s["planets"]).Count);
            var sb = new StringBuilder();
            sb.AppendLine("===== 存档融合报告（S4 merge） =====");
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"存档: {Path.GetFileName(dsvPath)}  name={sd.gameName}  seed={sd.galaxySeed}  恒星={sd.starCount}");
            sb.AppendLine($"资源倍率: 存档 {sd.resourceMultiplier} / json {mult}{(multMismatch ? "  ⚠ 不一致" : "")}   tick={sd.gameTick}");
            sb.AppendLine($"galaxy.json: {galaxyJsonPath}");
            sb.AppendLine();
            sb.AppendLine($"已扫描行星: {scannedN} / {totalPlanets}（其余保持模型初值 + scanned=false）");
            sb.AppendLine($"覆盖恒星名: {nameStars} 颗；行星名: {namePlanets} 颗（nameSource=save）");
            sb.AppendLine($"剩余储量覆盖: {scannedN} 颗行星的 veins[].currentAmount（amountSource=save）");
            sb.AppendLine($"组数冲突: {groupConflicts} 处（以存档为准）");
            if (conflicts.Count > 0)
                foreach (var c in conflicts.Take(20))
                    sb.AppendLine($"  - {c}");
            sb.AppendLine();
            sb.AppendLine("plan 区块未改动；veins[].initialAmount 保留模型初值，可与 currentAmount 对比看开采/模型差异。");

            string reportPath = Path.Combine(Path.GetDirectoryName(galaxyJsonPath) ?? ".", "merge-report.txt");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            L(sb.ToString());
            L($"备份: {bak}");
            L($"报告: {reportPath}");
            return 0;
        }
    }
}

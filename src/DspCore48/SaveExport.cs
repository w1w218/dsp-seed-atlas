using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;

namespace DspCore
{
    /// <summary>
    /// 存档导出：--save <save>.dsv
    ///  1. 解析存档头 → seed/starCount/资源倍率
    ///  2. 扫描全部“已扫描行星”VeinGroup（真实储量 amount）
    ///  3. 用存档 seed 重新生成星系（行星 id 对齐），导出两个 CSV：
    ///     * save1-single.csv：与 seed1_single.csv 同构（恒星级，矿脉列=已扫描行星真实储量汇总）
    ///     * save1-planets.csv：行星级明细（每行星一行，14 矿种 组数+储量）
    /// </summary>
    public static class SaveExport
    {
        static readonly string Title = "星系名字,星区数量,亮度,行星,距离,星系类型,是否环内行星,星球类型,最大重氢速率,卫星总数,潮汐锁定,是否有水,是否有硫酸,铁矿脉,铜矿脉,硅矿脉,钛矿脉,石矿脉,煤矿脉,原油涌泉,可燃冰矿,金伯利矿,分形硅矿,有机晶体矿,光栅石矿,刺笋矿脉,单极磁矿\n";

        public static int Run(string savePath, string outPath, bool computeSpace = true, bool computeMine = true)
        {
            if (!File.Exists(savePath))
            {
                Console.Error.WriteLine($"存档不存在: {savePath}");
                return 1;
            }
            FileStream fs = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            SaveParser.SaveDesc sd;
            try { sd = SaveParser.ParseHeader(fs); }
            finally { fs.Dispose(); }
            if (sd == null)
            {
                Console.Error.WriteLine("存档头解析失败");
                return 1;
            }
            Console.WriteLine($"存档: {sd.gameName}  seed={sd.galaxySeed} 恒星={sd.starCount} 资源倍率={sd.resourceMultiplier}  tick={sd.gameTick}");

            // 扫描已扫描行星段（流式分块 + 多线程，不整文件入内存）
            var planets = SaveParser.FindScannedPlanets(savePath, sd.searchFrom);
            Console.WriteLine($"已扫描行星: {planets.Count} 个");
            var byId = new Dictionary<int, SaveParser.ScannedPlanet>();
            foreach (var p in planets) byId[p.astroId] = p;

            // 用存档 seed 重新生成星系（行星 id 对齐）；星名按存档语言（starNameLCID）
            if (sd.galaxySeed <= 0 || sd.starCount <= 0)
            {
                Console.Error.WriteLine("存档 seed/starCount 无效");
                return 1;
            }
            int lcid = sd.starNameLCID;  // 0=英文 2052=中文
            Console.WriteLine($"星名语言: {(lcid == 2052 ? "中文(2052)" : "英文(0)")}");
            GameDesc gd = new GameDesc();
            gd.SetForNewGame(UniverseGen.algoVersion, sd.galaxySeed, sd.starCount, 1, sd.resourceMultiplier);
            StarGen.StarNameLCID = lcid;
            GalaxyData galaxy = UniverseGen.CreateGalaxy(gd);
            if (galaxy == null) { Console.Error.WriteLine("CreateGalaxy 失败"); return 1; }

            // ---- 导出1: 恒星级同构 CSV ----
            string out1 = outPath;
            if (string.IsNullOrEmpty(out1))
                out1 = Path.Combine(Directory.GetCurrentDirectory(), $"save{sd.galaxySeed}-single.csv");
            string dir1 = Path.GetDirectoryName(out1);
            string stem = Path.GetFileName(out1);
            if (stem.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                stem = stem.Substring(0, stem.Length - 4);
            string out2 = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-planets.csv");
            Directory.CreateDirectory(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1);   // 输出目录不存在时自建
            var sb = new StringBuilder();
            sb.Append(Title);
            long missed = 0;    // 未扫描行星计数
            for (int i = 0; i < galaxy.stars.Length; i++)
            {
                var star = galaxy.stars[i];
                var distanc = (float)(star.uPosition - galaxy.stars[0].uPosition).magnitude / 2400000.0f;
                bool isInDsp = star.dysonRadius * 2 > star.planets[0].sunDistance;
                sb.Append(star.name + "," + galaxy.starCount + "," + star.dysonLumino.ToString("F4") + "," + star.planetCount + "," +
                          distanc.ToString("F3") + "," + GetStarTypeString(star) + "," + (isInDsp ? "是" : "否") + ",");

                long[] total = new long[15];
                bool hasWater = false, hasAcid = false;
                int cxsd = 0, sats = 0;
                float gasSpeed = 0;
                string pnames = "";
                foreach (var planet in star.planets)
                {
                    pnames += GetPlanetTypeString(planet);
                    string sing = GetPlanetSingularityString(planet);
                    if (!string.IsNullOrEmpty(sing)) pnames += "-" + sing;
                    pnames += ";";
                    if (planet.waterItemId == 1000) hasWater = true;
                    if (planet.waterItemId == 1116) hasAcid = true;
                    if (GetPlanetTypeString(planet) == "气态巨星")
                    {
                        gasSpeed += planet.gasSpeeds[0] * Mathf.Pow(star.resourceCoef, 0.3f);
                        gasSpeed += planet.gasSpeeds[1] * Mathf.Pow(star.resourceCoef, 0.3f);
                    }
                    if (planet.orbitAroundPlanet != null) sats++;
                    if (sing.Contains("潮汐锁定")) cxsd++;

                    SaveParser.ScannedPlanet sp;
                    if (byId.TryGetValue(planet.id, out sp))
                    {
                        foreach (var g in sp.groups)
                            if (g.veinType >= 1 && g.veinType <= 14)
                                total[g.veinType] += g.amount;
                    }
                    else
                    {
                        missed++;
                    }
                }
                sb.Append(pnames + "," + gasSpeed + "," + sats + "," + cxsd + "," +
                          (hasWater ? "是," : "否,") + (hasAcid ? "是," : "否,"));
                for (int v = 1; v <= 14; v++)
                {
                    if (v == 7)
                        sb.Append((total[v] / OilSpeedDivisor).ToString("F2") + ",");   // 油=流速 /s
                    else
                        sb.Append(total[v] + ",");
                }
                sb.Append("\n");
            }
            File.WriteAllText(out1, sb.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"完成(恒星级): {out1}  未扫描行星 {missed} 个（未计入矿脉汇总）");

            // ---- 导出2: 行星级明细（字段对齐 refined-detail.csv，便于与种子精算直接比对） ----
            var sb2 = new StringBuilder();
            sb2.Append("种子,恒星id,恒星名,恒星距离,恒星类型,恒星光谱,行星id,行星名,主题,气巨,卫星母,6ly内,");
            sb2.Append("轨道,已扫描,陆海比,");
            for (int v = 1; v <= 14; v++)
                sb2.Append($"{SaveParser.VeinNames[v]}组,{SaveParser.VeinNames[v]}量,{SaveParser.VeinNames[v]}未开采量,");
            sb2.Append("原油流速/s,空间档,未开采组,已开采组,\n");
            for (int i = 0; i < galaxy.stars.Length; i++)
            {
                var star = galaxy.stars[i];
                var distanc = (float)(star.uPosition - galaxy.stars[0].uPosition).magnitude / 2400000.0f;
                bool in6 = distanc <= 6.0;
                foreach (var planet in star.planets)
                {
                    sb2.Append($"{sd.galaxySeed},{star.id},{star.name},{distanc:F3},{star.type},{star.spectr},{planet.id},{planet.name},{GetPlanetTypeString(planet)},");
                    sb2.Append($"{(planet.type == EPlanetType.Gas ? 1 : 0)},{planet.orbitAround},{(in6 ? 1 : 0)},{planet.orbitIndex},");
                    SaveParser.ScannedPlanet sp;
                    if (planet.type == EPlanetType.Gas)
                    {
                        // 气态巨星/冰巨星：无矿脉、无陆海比，气体产出为运行时生成（不落盘）
                        sb2.Append($"气巨,,");
                        for (int v = 1; v <= 14; v++) sb2.Append(",,,");
                        sb2.Append(",,,");   // 原油流速/s 空 + 空间档空 + 未开采/已开采组空
                    }
                    else if (byId.TryGetValue(planet.id, out sp))
                    {
                        sb2.Append($"是,{sp.landPercent * 100:F2},");
                        long[] grp = new long[15], amt = new long[15];
                        foreach (var g in sp.groups)
                            if (g.veinType >= 1 && g.veinType <= 14) { grp[g.veinType]++; amt[g.veinType] += g.amount; }
                        // 未开采/已开采矿组（重点功能）：存档组 amount 与模型同 seed 生成参照对比（排序匹配，不依赖组顺序）
                        int unmined = 0, mined = 0;
                        long[] unminedAmt = new long[15];
                        if (computeMine)
                        {
                            try
                            {
                                DspCore.GenApi.ModelPlanet(planet);
                                // 模型按 (type, groupIndex) 聚合组总量（存档 VeinGroup.amount = 组内所有矿点总和）
                                var modelGroups = new Dictionary<(int, int), long>();
                                for (int vi = 1; vi < planet.data.veinCursor; vi++)
                                {
                                    var vd = planet.data.veinPool[vi];
                                    if ((int)vd.type >= 1 && (int)vd.type <= 14)
                                    {
                                        var key = ((int)vd.type, vd.groupIndex);
                                        modelGroups.TryGetValue(key, out long gs);
                                        modelGroups[key] = gs + vd.amount;
                                    }
                                }
                                var modelByType = new Dictionary<int, List<long>>();
                                foreach (var kv in modelGroups)
                                {
                                    if (!modelByType.TryGetValue(kv.Key.Item1, out var ml))
                                    { ml = new List<long>(); modelByType[kv.Key.Item1] = ml; }
                                    ml.Add(kv.Value);
                                }
                                var saveByType = new Dictionary<int, List<long>>();
                                foreach (var g in sp.groups)
                                {
                                    if (g.veinType >= 1 && g.veinType <= 14)
                                    {
                                        if (!saveByType.TryGetValue(g.veinType, out var sl))
                                        { sl = new List<long>(); saveByType[g.veinType] = sl; }
                                        sl.Add(g.amount);
                                    }
                                }
                                foreach (var kv in saveByType)
                                {
                                    var ms = modelByType.TryGetValue(kv.Key, out var mlist) ? mlist : new List<long>();
                                    var ss = kv.Value.OrderBy(x => x).ToList();
                                    var mo = ms.OrderBy(x => x).ToList();
                                    int n = Math.Min(ss.Count, mo.Count);
                                    for (int j = 0; j < n; j++)
                                    {
                                        if (ss[j] == mo[j]) { unmined++; unminedAmt[kv.Key] += ss[j]; }
                                        else mined++;
                                    }
                                    mined += ss.Count - n;   // 模型没有的多余组视为已开采/异常
                                }
                            }
                            catch (Exception mex) { Console.WriteLine($"  行星 {planet.id} 未开采判定异常: {mex.Message}"); }
                        }
                        for (int v = 1; v <= 14; v++)
                            sb2.Append($"{grp[v]},{amt[v]},{unminedAmt[v]},");   // 组数/量/未开采量（油未开采量=原始 amount，流速见下）
                        sb2.Append($"{amt[7] / OilSpeedDivisor:F2},");   // 附加：原油流速 /s（剩余）
                        if (computeSpace)
                        {
                            var dirs = new List<UnityEngine.Vector3>();
                            foreach (var g in sp.groups) dirs.Add(new UnityEngine.Vector3(g.x, g.y, g.z));
                            var sr = SpaceAnalyzer.Analyze(dirs, planet.radius * 1200.0);
                            sb2.Append($"{sr.Level},");
                        }
                        else
                        {
                            sb2.Append(",");
                        }
                        sb2.Append($"{unmined},{mined},");
                    }
                    else
                    {
                        sb2.Append($"否,,");
                        for (int v = 1; v <= 14; v++) sb2.Append(",,,");
                        sb2.Append(",,,");   // 原油流速/s 空 + 空间档空 + 未开采/已开采组空
                    }
                    sb2.Append("\n");
                }
            }
            File.WriteAllText(out2, sb2.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"完成(行星级): {out2}");

            // 黑雾摘要（spaceSector 尾部 hive 段，行星基地/太空巢穴 + 在途火种数）
            try
            {
                var planetIds = new HashSet<int>();
                for (int hi = 0; hi < galaxy.stars.Length; hi++)
                    foreach (var p in galaxy.stars[hi].planets)
                        planetIds.Add(p.id);
                var hives = HiveExtractor.Extract(savePath, sd.starCount, planetIds);
                if (hives.Count > 0)
                {
                    var hsb = new StringBuilder();
                    hsb.Append("hiveAstroId,类型,恒星id,行星id,seed,realized,isEmpty,ticks,在途火种,本地火种,rootEnemyId,cores,builders,nodes,replicators,turrets,units,pbuilders,存活\n");
                    foreach (var h in hives)
                        hsb.Append($"{h.HiveAstroId},{(h.IsSpaceHive ? "太空巢穴" : "行星基地")},{h.StarId},{h.PlanetId},{h.Seed},{h.Realized},{h.IsEmpty},{h.Ticks},{h.TindersArrivingInTransit},{h.TinderCount},{h.RootEnemyId},{h.CoresCount},{h.BuildersCount},{h.NodesCount},{h.ReplicatorsCount},{h.TurretsCount},{h.UnitsCount},{h.PbuildersActive},{(h.IsAlive ? "是" : "否")}\n");
                    string hivePath = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-hive.csv");
                    File.WriteAllText(hivePath, hsb.ToString(), new UTF8Encoding(true));
                    // 火种明细（含位置/到目标距离/ETA；1 游戏光年 = 2,400,000 m，uPos 单位 = m）
                    var tsb = new StringBuilder();
                    tsb.Append("hiveAstroId,enemyId,originHive,targetHive,direction,stage,uSpeed,posX,posY,posZ,distTargetLY,etaMin\n");
                    try
                    {
                        var ssec = SpaceSectorExtractor.Extract(savePath, sd.starCount);
                        var astroPos = new Dictionary<int, SpaceSectorExtractor.AstroPos>();
                        foreach (var a in ssec.Astros) astroPos[a.AstroId] = a;
                        var enemyPos = new Dictionary<int, SpaceSectorExtractor.EnemyPos>();
                        foreach (var e in ssec.Enemies) enemyPos[e.EnemyId] = e;
                        foreach (var h in hives)
                            foreach (var t in h.Tinders)
                            {
                                double dx = 0, dy = 0, dz = 0, distLY = 0, etaMin = 0;
                                double px = 0, py = 0, pz = 0;
                                if (enemyPos.TryGetValue(t.EnemyId, out var ep))
                                {
                                    px = ep.X; py = ep.Y; pz = ep.Z;
                                    if (astroPos.TryGetValue(t.TargetHiveAstroId, out var ap))
                                    {
                                        dx = ap.X - ep.X; dy = ap.Y - ep.Y; dz = ap.Z - ep.Z;
                                        double distM = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                                        distLY = distM / 2400000.0;
                                        etaMin = t.USpeed > 0.01 ? distM / t.USpeed / 60.0 : 0;
                                    }
                                }
                                tsb.Append($"{h.HiveAstroId},{t.EnemyId},{t.OriginHiveAstroId},{t.TargetHiveAstroId},{t.Direction},{t.Stage},{t.USpeed:F4},{px:F1},{py:F1},{pz:F1},{distLY:F4},{etaMin:F1}\n");
                            }
                    }
                    catch (Exception tpex)
                    {
                        Console.WriteLine($"火种位置计算异常（仅 fallback 基础列）: {tpex.Message}");
                        foreach (var h in hives)
                            foreach (var t in h.Tinders)
                                tsb.Append($"{h.HiveAstroId},{t.EnemyId},{t.OriginHiveAstroId},{t.TargetHiveAstroId},{t.Direction},{t.Stage},{t.USpeed:F4},0,0,0,0,0\n");
                    }
                    if (hives.Any(x => x.Tinders.Count > 0))
                    {
                        string tinderPath = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-tinders.csv");
                        File.WriteAllText(tinderPath, tsb.ToString(), new UTF8Encoding(true));
                    }
                    // relay 投放明细（地面基地线索：targetAstroId=基地行星，baseState=1&&stage=2 为已投放）
                    var rsb = new StringBuilder();
                    rsb.Append("hiveAstroId,relayId,targetAstroId,baseState,stage\n");
                    foreach (var h in hives)
                        foreach (var r in h.Relays)
                            rsb.Append($"{h.HiveAstroId},{r.RelayId},{r.TargetAstroId},{r.BaseState},{r.Stage}\n");
                    if (hives.Any(x => x.Relays.Count > 0))
                    {
                        string relayPath = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-relays.csv");
                        File.WriteAllText(relayPath, rsb.ToString(), new UTF8Encoding(true));
                    }
                    Console.WriteLine($"黑雾 hive: {hives.Count} 个（{hivePath}）");
                }
                else
                {
                    Console.WriteLine("黑雾 hive: 未找到（存档未开启黑雾或无 hive 段）");
                }
            }
            catch (Exception hex)
            {
                Console.WriteLine($"黑雾提取异常: {hex.Message}");
            }

            // 戴森球（1212 魔法标记特征扫描 + DysonSphere.Import 后半解析）
            try
            {
                var dysons = DysonExtractor.Extract(savePath, sd.starCount, galaxy);
                // 真球判定（用户规则）：层数 > 1（默认 1 空层 + 最多 10 层）或 有在建层（节点>0）
                var real = dysons.Where(d => d.LayerCount >= 2 || d.Layers.Any(l => l.NodeTotal > 0)).ToList();
                if (real.Count == 0)
                    Console.WriteLine("戴森球: 无真球（无层数>1 或有在建层的球）");
                if (real.Count > 0)
                {
                    var dsb = new StringBuilder();
                    dsb.Append("恒星index,恒星名,恒星类型,层数,建层数,空层数,在建层数,完成层数,节点总数,节点已建,框架总数,框架已建,壳数,在途火箭,匹配说明\n");
                    foreach (var d in real)
                    {
                        // 恒星匹配（优先 starMap 结构定位；否则母星模板/区间匹配兜底）：
                        int bestStar = -1;
                        double bestDiff = double.MaxValue;
                        if (d.StarIndex >= 0 && d.StarIndex < galaxy.stars.Length)
                        {
                            bestStar = galaxy.stars[d.StarIndex].id;   // starMap（0/1 链）已定恒星
                        }
                        else if (d.LayerCount == 1 && d.Layers.Count == 1 &&
                                 d.Layers[0].NodeTotal == 62 && d.Layers[0].FrameTotal == 116 && d.Layers[0].ShellTotal == 56)
                        {
                            // 母星模板（用户 2026-10-21 确认：两档母星球均 = 1 层 + 62 节点/116 框架/56 壳）→ 地中海恒星
                            for (int si = 0; si < galaxy.stars.Length; si++)
                                if (galaxy.stars[si].planets != null &&
                                    galaxy.stars[si].planets.Any(p => p != null && p.theme == 1))
                                { bestStar = galaxy.stars[si].id; break; }
                        }
                        if (bestStar < 0)
                        {
                            foreach (var st in galaxy.stars)
                            {
                                double defOrbit = st.dysonRadius * 40000.0;
                                double minOrbit = Math.Max(4000.0, st.physicsRadius * 1.5);
                                double maxOrbit = Math.Round(defOrbit * 2 / 100.0) * 100.0;
                                foreach (var l in d.Layers)
                                {
                                    if (l.OrbitRadius < minOrbit || l.OrbitRadius > maxOrbit) continue;
                                    double dd = Math.Abs(l.OrbitRadius - defOrbit);
                                    if (dd < bestDiff) { bestDiff = dd; bestStar = st.id; }
                                }
                            }
                        }
                        var bst = bestStar > 0 ? galaxy.stars[bestStar - 1] : null;
                        string starName = bst != null ? bst.name : "?";
                        string starType = bst != null ? bst.type.ToString() : "?";
                        // 匹配详情：区间内恒星按差排序（前 6）
                        var best = galaxy.stars
                            .SelectMany(st => d.Layers.Select(l => new
                            {
                                st,
                                orbit = l.OrbitRadius,
                                defOrbit = st.dysonRadius * 40000.0,
                                minOrbit = Math.Max(4000.0, st.physicsRadius * 1.5),
                                maxOrbit = Math.Round(st.dysonRadius * 40000.0 * 2 / 100.0) * 100.0,
                                diff = Math.Abs(l.OrbitRadius - st.dysonRadius * 40000.0),
                            }))
                            .Where(x => x.orbit >= x.minOrbit && x.orbit <= x.maxOrbit)
                            .GroupBy(x => x.st.id)
                            .Select(g => new { g.First().st, defOrbit = g.Min(x => x.defOrbit), diff = g.Min(x => x.diff) })
                            .OrderByDescending(x => x.st.type.ToString() == "MainSeqStar" && x.st.spectr == ESpectrType.O)
                            .ThenBy(x => x.diff)
                            .Take(6)
                            .Select(x => $"{x.st.name}({x.st.type}/{x.st.spectr}) 推荐={x.defOrbit:F0}m 差={x.diff:F0}m");
                        int builtLayers = d.Layers.Count(l => l.NodeTotal > 0);   // 建层数（有节点的层）
                        // 层状态（用户规则）：空=无设计；完成=节点+框架全部建成；在建=其余
                        int emptyLayers = d.Layers.Count(l => l.NodeTotal == 0 && l.FrameTotal == 0 && l.ShellTotal == 0);
                        int completeLayers = d.Layers.Count(l => (l.NodeTotal + l.FrameTotal) > 0 && l.NodeBuilt == l.NodeTotal && l.FrameBuilt == l.FrameTotal);
                        int buildingLayers = d.Layers.Count - emptyLayers - completeLayers;
                        dsb.Append($"{(bestStar > 0 ? bestStar - 1 : -1)},{starName},{starType},{d.LayerCount},{builtLayers},{emptyLayers},{buildingLayers},{completeLayers}," +
                                   $"{d.Layers.Sum(x => (long)x.NodeTotal)},{d.Layers.Sum(x => (long)x.NodeBuilt)}," +
                                   $"{d.Layers.Sum(x => (long)x.FrameTotal)},{d.Layers.Sum(x => (long)x.FrameBuilt)}," +
                                   $"{d.Layers.Sum(x => (long)x.ShellTotal)},{d.RocketInFlight},{string.Join(" ; ", best)}\n");
                    }
                    string dysonPath = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-dyson.csv");
                    File.WriteAllText(dysonPath, dsb.ToString(), new UTF8Encoding(true));
                    // 层明细
                    var lsb = new StringBuilder();
                    lsb.Append("恒星index,层id,轨道半径,节点总数,节点已建,框架总数,框架已建,壳数\n");
                    foreach (var d in real)
                        foreach (var l in d.Layers)
                            lsb.Append($"{d.StarIndex},{l.LayerId},{l.OrbitRadius:F3},{l.NodeTotal},{l.NodeBuilt},{l.FrameTotal},{l.FrameBuilt},{l.ShellTotal}\n");
                    string layerPath = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-dyson-layers.csv");
                    File.WriteAllText(layerPath, lsb.ToString(), new UTF8Encoding(true));
                    Console.WriteLine($"戴森球: {dysons.Count} 颗（{dysonPath}）");
                }
                else
                {
                    Console.WriteLine("戴森球: 未找到（无 1212 标记，存档无戴森球？）");
                }
            }
            catch (Exception dex)
            {
                Console.WriteLine($"戴森球提取异常: {dex.Message}");
            }
        // 工厂段（建筑检测 B + 采集站 D + 地面基地 C）
        try
        {
            var factories = FactoryExtractor.Extract(savePath);
            var fsb = new StringBuilder();
            fsb.Append("种子,行星id,版本,实体容量,实体数,回收,地面基地数,单位数,采集站数,bucketCount,偏移\n");
            foreach (var f in factories)
            {
                int collectors = f.ProtoIdCount.TryGetValue(2105, out int oc) ? oc : 0;   // OrbitalCollector（ACH_OrbitalCollectorCount 权威：protoId==2105）
                fsb.Append($"{sd.galaxySeed},{f.PlanetId},{f.Version},{f.EntityCapacity},{f.EntityCount},{f.EntityRecycleCursor},{f.GroundBases},{f.GroundUnits},{collectors},{f.HashBucketCount},{f.Offset}\n");
            }
            string factoryPath = Path.Combine(string.IsNullOrEmpty(dir1) ? Directory.GetCurrentDirectory() : dir1, stem + "-factory.csv");
            File.WriteAllText(factoryPath, fsb.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"工厂: {factories.Count} 个（{factoryPath}）");
        }
        catch (Exception fex)
        {
            Console.WriteLine($"工厂提取异常: {fex.Message}");
        }
            return 0;
        }

        /// <summary>原油涌泉流速口径：amount/25000 = /s（已用 104 母星 58.51/s、702 水世界 87.62/s 验证）。</summary>
        static readonly double OilSpeedDivisor = 25000.0;

        static string distTag(int seed) => seed.ToString();

        static string GetStarTypeString(StarData star)
        {
            string t = "";
            if (star.type == EStarType.GiantStar)
                t = star.spectr > ESpectrType.K ? (star.spectr > ESpectrType.F ? (star.spectr != ESpectrType.A ? t + "蓝巨星" : t + "白巨星") : t + "黄巨星") : t + "红巨星";
            else if (star.type == EStarType.WhiteDwarf) t += "白矮星";
            else if (star.type == EStarType.NeutronStar) t += "中子星";
            else if (star.type == EStarType.BlackHole) t += "黑洞";
            else if (star.type == EStarType.MainSeqStar) t += star.spectr + "型恒星";
            return t;
        }

        static string GetPlanetTypeString(PlanetData planetData)
        {
            ThemeProto tp = LDB.themes.Select(planetData.theme);
            return tp != null ? tp.DisplayName : "未知";
        }

        static string GetPlanetSingularityString(PlanetData planetData)
        {
            string s = "";
            if (planetData.orbitAround > 0) s += "卫星";
            if ((planetData.singularity & EPlanetSingularity.TidalLocked) != EPlanetSingularity.None) s += "潮汐锁定永昼永夜";
            if ((planetData.singularity & EPlanetSingularity.TidalLocked2) != EPlanetSingularity.None) s += "潮汐锁定1:2";
            if ((planetData.singularity & EPlanetSingularity.TidalLocked4) != EPlanetSingularity.None) s += "潮汐锁定1:4";
            if ((planetData.singularity & EPlanetSingularity.LaySide) != EPlanetSingularity.None) s += "横躺自转";
            if ((planetData.singularity & EPlanetSingularity.ClockwiseRotate) != EPlanetSingularity.None) s += "反向自转";
            if ((planetData.singularity & EPlanetSingularity.MultipleSatellites) != EPlanetSingularity.None) s += "多卫星";
            return s;
        }
    }
}

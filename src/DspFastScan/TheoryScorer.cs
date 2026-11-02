
using DspSeedFinder.Data;
using DspSeedFinder.Gen;



namespace DspSearcher.Core;

/// <summary>理论储量评分（快筛）：组数×期望点数×op×10万×resourceCoef，对齐 GenerateVeins 精算口径。
/// 含距离加成（resourceCoef = 7^logchain(dist/32)×0.6，由 StarGen 生成时已写入恒星）。</summary>
public static class TheoryScorer
{
    public const int VEIN_MAX = 15;

    /// <summary>星系理论储量 + 4.3/4.4 覆盖判定合一循环（组数只算一遍，快筛热点）。
    /// 返回 veins（1..14 累计）、gases（itemId→每分钟，气巨采集站固定 40）、homeIce/iceGiantCount（4.3）、
    /// coverage（1..13，单极磁石不算）+hasSulfuric（火山灰行星，4.4）/missing/planetsIn6Ly、birthOnIce（4.3b）、
    /// minOStarDist（最近 O 星到母星=银河中心）、homeIceSatellites（母星冰巨星卫星数，含母星）。</summary>
    public static (long[] veins, Dictionary<int, double> gases, bool homeIce, int iceGiantCount,
        bool[] coverage, string missing, int planetsIn6Ly, bool birthOnIce, bool hasSulfuric,
        double minOStarDist, int homeIceSatellites)
        ScoreAndCover(GalaxyData galaxy, double coverRadius)
    {
        var veins = new long[VEIN_MAX];
        var gases = new Dictionary<int, double>();
        var coverage = new bool[VEIN_MAX];
        int iceGiantCount = 0, planetsIn6Ly = 0, iceSat = 0;
        bool homeIce = false, hasSulfuric = false;
        double minO = double.MaxValue;
        var star0 = galaxy.stars.Length > 0 ? galaxy.stars[0] : null;

        // 母星所绕行星（冰巨星卫星数统计用）——number 为恒星内独立编号，须限定同一恒星
        int hostNumber = -1;
        var birth = FindPlanet(galaxy, galaxy.birthPlanetId);
        if (birth != null && birth.orbitAround != 0 && birth.orbitAroundPlanet != null
            && birth.orbitAroundPlanet.type == EPlanetType.Gas)
            hostNumber = birth.orbitAroundPlanet.number;

        foreach (var star in galaxy.stars)
        {
            bool starNear = star.position.magnitude <= coverRadius;
            if (star.spectr == ESpectrType.O) minO = Math.Min(minO, star.position.magnitude);
            foreach (var pl in star.planets)
            {
                if (hostNumber >= 0 && pl.orbitAround == hostNumber && star == birth?.star) iceSat++;
                if (pl.type == EPlanetType.Gas)
                {
                    for (int i = 0; i < pl.gasItems.Length && i < pl.gasSpeeds.Length; i++)
                    {
                        int item = pl.gasItems[i];
                        double rate = pl.gasSpeeds[i] * SearchConfig.GAS_STATIONS * 60.0;
                        gases.TryGetValue(item, out double cur);
                        gases[item] = cur + rate;
                    }
                    var th0 = LDB.Theme(pl.theme);
                    bool ice = th0 != null && (th0.ID == 4 || th0.ID == 5);
                    if (star == star0 && ice) { homeIce = true; iceGiantCount++; }
                    if (starNear && ice) coverage[8] = true;   // 冰巨星气体可燃冰
                    continue;
                }
                // 非气巨：组数一次调用，同时用于覆盖判定与储量累计
                int[] g = VeinGen.GenerateGroupCounts(pl);
                if (starNear)
                {
                    planetsIn6Ly++;
                    for (int t = 1; t <= 14; t++)
                        if (g[t] > 0) coverage[t] = true;
                    if (pl.theme == Filters.THEME_VOLCANIC) hasSulfuric = true;   // 火山灰行星 = 硫酸
                }
                var th = LDB.Theme(pl.theme);
                if (th == null) continue;
                bool isBirth = galaxy.birthPlanetId == pl.id;
                float resourceCoef = star.resourceCoef;
                if (isBirth) resourceCoef *= 2f / 3f;
                else if (UniverseGen.CurrentResourceMultiplier <= 0.1001f)
                {
                    if (resourceCoef > 1f) resourceCoef = (float)Math.Pow(resourceCoef, 0.8);
                    resourceCoef *= 0.7f;
                }
                for (int t = 1; t <= 14; t++)
                {
                    int spot = g[t];
                    if (spot <= 0) continue;
                    // 普通矿 vc/op 在 VeinCount/VeinOpacity（仅 7 项）；稀有矿（8~14）由 RareSettings 提供，
                    // 越界取 0 再由下方覆盖——不能 continue 跳过（否则稀有矿储量全 0）
                    float vc = t <= th.VeinCount.Count ? th.VeinCount[t - 1] : 0f;
                    float op = t <= th.VeinOpacity.Count ? th.VeinOpacity[t - 1] : 0f;
                    // 特殊恒星固定矿 vc/op（GenerateVeins 预设；随后 RareVeins 覆盖可将其替换）
                    if (star.type == EStarType.WhiteDwarf)
                    {
                        if (t == 9 || t == 10) { vc = 0.7f; op = 1f; }
                        else if (t == 12) { vc = 0.7f; op = 0.3f; }
                    }
                    else if (star.type == EStarType.NeutronStar || star.type == EStarType.BlackHole)
                    {
                        if (t == 14) { vc = 0.7f; op = 0.3f; }
                    }
                    // 主题稀有矿 vc/op（GenerateVeins 同序：后置覆盖特殊星预设）
                    for (int n = 0; n < th.RareVeins.Count; n++)
                    {
                        if (th.RareVeins[n] == t && th.RareSettings.Count >= n * 4 + 3)
                        {
                            vc = th.RareSettings[n * 4 + 2];
                            op = vc;
                            break;
                        }
                    }
                    double perGroup = t == 7 ? 1.0 : Math.Max(vc * 22.0, 1.0);
                    double baseAmt = op * 100000.0 * resourceCoef;
                    if (t == 7) baseAmt = op * 100000.0 * Math.Sqrt(resourceCoef);
                    if (baseAmt < 20.0) baseAmt = 20.0;
                    veins[t] += (long)(spot * perGroup * baseAmt);
                }
            }
        }

        var miss = new List<string>();
        for (int t = 1; t <= 13; t++)
            if (!coverage[t]) miss.Add(VeinGen.VeinName(t));
        if (!hasSulfuric) miss.Add("硫酸");   // 火山灰行星（4.4 要求）
        string missing = string.Join("/", miss);

        bool birthOnIce = false;
        if (galaxy.birthPlanetId != 0 && birth != null)
        {
            if (birth.orbitAround != 0 && birth.orbitAroundPlanet != null)
            {
                var thb = LDB.Theme(birth.orbitAroundPlanet.theme);
                birthOnIce = birth.orbitAroundPlanet.type == EPlanetType.Gas && thb != null && (thb.ID == 4 || thb.ID == 5);
            }
        }
        return (veins, gases, homeIce, iceGiantCount, coverage, missing, planetsIn6Ly, birthOnIce, hasSulfuric,
            minO >= double.MaxValue ? 0 : minO, hostNumber >= 0 ? iceSat : 0);
    }

    private static PlanetData FindPlanet(GalaxyData galaxy, int id)
    {
        foreach (var st in galaxy.stars)
            foreach (var pl in st.planets)
                if (pl.id == id) return pl;
        return null;
    }

    /// <summary>单行星理论储量（索引 1..14）。与 GenerateVeins 结构对齐。</summary>
    public static long[] PlanetTheoretical(PlanetData planet)
    {
        var amt = new long[VEIN_MAX];
        var theme = LDB.Theme(planet.theme);
        if (theme == null || planet.type == EPlanetType.Gas) return amt;

        int[] groups = VeinGen.GenerateGroupCounts(planet);
        bool isBirth = planet.galaxy.birthPlanetId == planet.id;
        float resourceCoef = planet.star.resourceCoef;
        if (isBirth) resourceCoef *= 2f / 3f;
        else if (UniverseGen.CurrentResourceMultiplier <= 0.1001f)
        {
            if (resourceCoef > 1f) resourceCoef = (float)Math.Pow(resourceCoef, 0.8);
            resourceCoef *= 0.7f;
        }

        for (int type = 1; type <= 14; type++)
        {
            int spot = groups[type];
            if (spot <= 0) continue;

            float vc = type <= theme.VeinCount.Count - 1 ? theme.VeinCount[type - 1] : 0f;
            float op = type <= theme.VeinOpacity.Count - 1 ? theme.VeinOpacity[type - 1] : 0f;
            // 稀有矿的 vc/op 由 RareSettings 覆盖（GenerateVeins 中 vcs[type]=vc; ops[type]=vc）
            for (int n = 0; n < theme.RareVeins.Count; n++)
            {
                if (theme.RareVeins[n] == type && theme.RareSettings.Count >= n * 4 + 3)
                {
                    vc = theme.RareSettings[n * 4 + 2];
                    op = vc;
                    break;
                }
            }

            // 期望点数：非油 Next(round(vc*20), round(vc*24)+1) 均匀 → 期望 ≈ vc*22；油每组 1 点
            double perGroup = type == 7 ? 1.0 : Math.Max(vc * 22.0, 1.0);

            double baseAmt = op * 100000.0 * resourceCoef;
            if (type == 7) baseAmt = op * 100000.0 * Math.Sqrt(resourceCoef);
            if (baseAmt < 20.0) baseAmt = 20.0;

            amt[type] = (long)(spot * perGroup * baseAmt);
        }
        return amt;
    }

    /// <summary>星系理论储量汇总（索引 1..14），含气巨气体供给（itemId→每分钟速率×站数×60）。</summary>
    public static (long[] veins, Dictionary<int, double> gases) GalaxyTheoretical(GalaxyData galaxy, int gasStations)
    {
        var veins = new long[VEIN_MAX];
        var gases = new Dictionary<int, double>();
        foreach (var star in galaxy.stars)
        {
            foreach (var pl in star.planets)
            {
                var amt = PlanetTheoretical(pl);
                for (int t = 1; t <= 14; t++) veins[t] += amt[t];
                if (pl.type == EPlanetType.Gas)
                {
                    for (int i = 0; i < pl.gasItems.Length && i < pl.gasSpeeds.Length; i++)
                    {
                        int item = pl.gasItems[i];
                        double rate = pl.gasSpeeds[i] * gasStations * 60.0;  // 每分钟
                        gases.TryGetValue(item, out double cur);
                        gases[item] = cur + rate;
                    }
                }
            }
        }
        return (veins, gases);
    }
}

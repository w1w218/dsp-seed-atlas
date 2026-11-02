
using DspCore;


namespace DspSearcher.Core;

/// <summary>过滤条件判定（4.1~4.4）。星表层在 CreateGalaxyLight 后立即判（淘汰即弃，不生成行星）。</summary>
public static class Filters
{
    public const int THEME_GAS3 = 4;   // Gas 3 冰巨星
    public const int THEME_GAS4 = 5;   // Gas 4 冰巨星
    public const int THEME_VOLCANIC = 13;  // Volcanic 1 火山灰（WaterItemId=1116 硫酸海）

    /// <summary>冰巨星主题判定（Gas 3/4，id 4/5）。</summary>
    public static bool IsIceGiant(int theme) => theme == THEME_GAS3 || theme == THEME_GAS4;

    public static bool IsGasGiant(PlanetData pl) => pl.type == EPlanetType.Gas;

    /// <summary>星表层结果。</summary>
    public class StarVerdict
    {
        public bool Pass = false;          // 4.1 通过（唯一强制的星表条件）
        public int OStarCount = 0;
        public int BlackHoleCount = 0;
        public int NeutronCount = 0;
        public double NearSpecialDist = 0; // 黑洞/中子星中较近者
        public double FarSpecialDist = 0;  // 较远者
        public bool SpecialPairOk = false; // 4.2 达标（近≤NearDist 且远≥FarDist）
    }

    public static StarVerdict StarLayer(GalaxyData galaxy, SearchConfig cfg)
    {
        var v = new StarVerdict();
        double near = double.MaxValue, far = 0;
        foreach (var st in galaxy.stars)
        {
            if (st.spectr == ESpectrType.O) v.OStarCount++;
            if (st.type == EStarType.BlackHole) { v.BlackHoleCount++; near = Math.Min(near, st.position.magnitude); far = Math.Max(far, st.position.magnitude); }
            else if (st.type == EStarType.NeutronStar) { v.NeutronCount++; near = Math.Min(near, st.position.magnitude); far = Math.Max(far, st.position.magnitude); }
        }
        v.NearSpecialDist = near == double.MaxValue ? 0 : near;
        v.FarSpecialDist = far;
        v.SpecialPairOk = cfg.NeedSpecialStarPair
            ? v.BlackHoleCount >= 1 && v.NeutronCount >= 1 && v.NearSpecialDist <= cfg.NearDist && v.FarSpecialDist >= cfg.FarDist
            : true;
        v.Pass = v.OStarCount >= cfg.MinOStar;
        return v;
    }

    /// <summary>行星层结果：4.3 强制 + 4.4 强制；4.3b 加分。</summary>
    public class PlanetVerdict
    {
        public bool Pass = false;
        public bool HomeIceGiant = false;     // 4.3
        public bool BirthOnIceGiant = false;  // 4.3b
        public int IceGiantCount = 0;
        public bool[] Coverage = new bool[15]; // 4.4 6ly 内覆盖（索引 1..13）
        public bool HasSulfuric;                // 4.4 火山灰行星（硫酸）
        public string Missing = "";            // 缺失资源名（调试）
        public int PlanetsIn6Ly = 0;
    }

    /// <summary>4.3：母星系（stars[0]）是否含冰巨星（仅需 stars[0] 行星生成）。</summary>
    public static bool HomeIceGiantOnly(GalaxyData galaxy, out int iceGiantCount)
    {
        iceGiantCount = 0;
        var star0 = galaxy.stars.Length > 0 ? galaxy.stars[0] : null;
        if (star0 == null) return false;
        foreach (var pl in star0.planets)
            if (IsGasGiant(pl) && IsIceGiant(pl.theme))
                iceGiantCount++;
        return iceGiantCount > 0;
    }

    /// <summary>4.4：覆盖半径内（以出生星为中心）13 资源全覆盖（单极磁石不算；可燃冰含冰巨星气体）
    /// + 火山灰行星（硫酸）。只依赖覆盖半径内恒星的已生成行星。</summary>
    public static bool CoverageOnly(GalaxyData galaxy, SearchConfig cfg, out bool[] coverage, out string missing, out int planetsIn6Ly, out bool hasSulfuric)
    {
        coverage = new bool[15];
        missing = "";
        planetsIn6Ly = 0;
        hasSulfuric = false;
        foreach (var st in galaxy.stars)
        {
            if (st.position.magnitude > cfg.CoverageRadius) continue;
            foreach (var pl in st.planets)
            {
                planetsIn6Ly++;
                if (IsGasGiant(pl))
                {
                    if (IsIceGiant(pl.theme)) coverage[8] = true;  // 冰巨星气体可燃冰
                    continue;
                }
                if (pl.theme == THEME_VOLCANIC) hasSulfuric = true;   // 火山灰行星 = 硫酸
                int[] g = DspCore.VeinGen.GenerateGroupCounts(pl);
                for (int t = 1; t <= 14; t++)
                    if (g[t] > 0) coverage[t] = true;
            }
        }
        var miss = new List<string>();
        for (int t = 1; t <= 13; t++)
            if (!coverage[t]) miss.Add(DspCore.VeinGen.VeinName(t));
        if (!hasSulfuric) miss.Add("硫酸");
        missing = string.Join("/", miss);
        return missing.Length == 0;
    }

    /// <summary>4.3b：母星是否为冰巨星的卫星（行星层全部生成后调用）。</summary>
    public static bool BirthOnIceGiantOnly(GalaxyData galaxy)
    {
        if (galaxy.birthPlanetId == 0) return false;
        var birth = FindPlanet(galaxy, galaxy.birthPlanetId);
        if (birth == null || birth.orbitAround == 0 || birth.orbitAroundPlanet == null) return false;
        return IsGasGiant(birth.orbitAroundPlanet) && IsIceGiant(birth.orbitAroundPlanet.theme);
    }

    public static PlanetVerdict PlanetLayer(GalaxyData galaxy, SearchConfig cfg)
    {
        var v = new PlanetVerdict
        {
            HomeIceGiant = HomeIceGiantOnly(galaxy, out int igc),
            IceGiantCount = igc,
            BirthOnIceGiant = BirthOnIceGiantOnly(galaxy),
        };
        v.Pass = v.HomeIceGiant && CoverageOnly(galaxy, cfg, out v.Coverage, out v.Missing, out v.PlanetsIn6Ly, out v.HasSulfuric);
        return v;
    }

    private static PlanetData FindPlanet(GalaxyData galaxy, int id)
    {
        foreach (var st in galaxy.stars)
            foreach (var pl in st.planets)
                if (pl.id == id) return pl;
        return null;
    }
}

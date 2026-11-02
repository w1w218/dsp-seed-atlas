using System.Text.Json;

namespace DspPlan;

/// <summary>候选行星（A5 + 2026-10-27 候选池规则）。</summary>
sealed class Candidate
{
    public int PlanetId;
    public int StarId;
    public string StarName = "";
    public string PlanetName = "";
    public string Theme = "";
    public double DistFromBirth;
    public bool LowOpen;        // 低开阔度：只能承担多槽（槽数≥2）槽位
    public bool IsBirth;        // 母星（G8 固定，不可用于其他槽）
}

static class CandidatePool
{
    static readonly HashSet<string> LowOpenThemes = new()
        { "海洋丛林", "樱林海", "潘多拉沼泽", "草原", "水世界", "热带草原" };
    static readonly HashSet<string> BadStarType = new() { "BlackHole", "NeutronStar" };

    /// <summary>按母星距离累计 need 颗可用可建行星，再取 extraStars 个额外恒星系。</summary>
    public static (List<Candidate> pool, double[] birthPos, List<(int starId, string name, double x, double y, double z)> gasGiants)
        Build(string galaxyJson, int need = 40, int extraStars = 3)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(galaxyJson));
        var root = doc.RootElement;
        var stars = root.GetProperty("stars").EnumerateArray()
            .Select(s => new
            {
                Id = s.GetProperty("id").GetInt32(),
                Name = s.GetProperty("name").GetString() ?? "",
                Type = s.GetProperty("type").GetString() ?? "",
                Spectr = s.GetProperty("spectr").GetString() ?? "",
                Dist = s.GetProperty("distanceFromBirth").GetDouble(),
                X = s.GetProperty("position").GetProperty("x").GetDouble(),
                Y = s.GetProperty("position").GetProperty("y").GetDouble(),
                Z = s.GetProperty("position").GetProperty("z").GetDouble(),
                Planets = s.GetProperty("planets").EnumerateArray().Select(p => new
                {
                    Id = p.GetProperty("id").GetInt32(),
                    Number = p.GetProperty("number").GetInt32(),
                    Name = p.GetProperty("name").GetString() ?? "",
                    Theme = p.GetProperty("themeName").GetString() ?? "",
                    IsGas = p.GetProperty("isGasGiant").GetBoolean(),
                }).ToList()
            })
            .OrderBy(s => s.Dist).ToList();

        var birthStar = stars.First();                      // distanceFromBirth = 0
        var birth = new[] { birthStar.X, birthStar.Y, birthStar.Z };
        foreach (var s in stars) StarPosCache[s.Id] = (s.X, s.Y, s.Z);

        // 母星：母星系中主题为"地中海"的行星（出生点）
        int birthPlanetId = birthStar.Planets.FirstOrDefault(p => p.Theme == "地中海")?.Id ?? -1;

        var pool = new List<Candidate>();
        int usedStars = 0;
        var usedStarIds = new HashSet<int>();
        foreach (var s in stars)
        {
            if (BadStarType.Contains(s.Type) || s.Spectr == "O") continue;
            var ok = s.Planets.Where(p => !p.IsGas).ToList();
            if (ok.Count == 0) continue;
            usedStars++;
            usedStarIds.Add(s.Id);
            foreach (var p in ok)
            {
                pool.Add(new Candidate
                {
                    PlanetId = p.Id, StarId = s.Id, StarName = s.Name, PlanetName = p.Name,
                    Theme = p.Theme, DistFromBirth = s.Dist,
                    LowOpen = LowOpenThemes.Contains(p.Theme),
                    IsBirth = p.Id == birthPlanetId,
                });
            }
            if (pool.Count - (birthPlanetId > 0 ? 1 : 0) >= need) break;
        }

        // 再取 extraStars 个有可建行星的恒星系
        int extra = 0;
        foreach (var s in stars)
        {
            if (usedStarIds.Contains(s.Id)) continue;
            if (BadStarType.Contains(s.Type) || s.Spectr == "O") continue;
            var ok = s.Planets.Where(p => !p.IsGas).ToList();
            if (ok.Count == 0) continue;
            foreach (var p in ok)
                pool.Add(new Candidate
                {
                    PlanetId = p.Id, StarId = s.Id, StarName = s.Name, PlanetName = p.Name,
                    Theme = p.Theme, DistFromBirth = s.Dist,
                    LowOpen = LowOpenThemes.Contains(p.Theme),
                    IsBirth = false,
                });
            if (++extra >= extraStars) break;
        }

        // 氢源：全部气巨/冰巨星（不在候选池内）
        var gas = new List<(int, string, double, double, double)>();
        foreach (var s in stars)
            foreach (var p in s.Planets.Where(p => p.IsGas))
                gas.Add((s.Id, s.Name, s.X, s.Y, s.Z));

        return (pool, birth, gas);
    }

    /// <summary>距离矩阵：同恒星系 = 1，跨恒星 = 光年距离。</summary>
    public static double[,] Distances(List<Candidate> pool, List<(int starId, string name, double x, double y, double z)> _)
    {
        int n = pool.Count;
        var d = new double[n, n];
        // 恒星坐标缓存
        var starPos = new Dictionary<int, (double x, double y, double z)>();
        for (int i = 0; i < n; i++)
        {
            var c = pool[i];
            if (!starPos.ContainsKey(c.StarId))
                starPos[c.StarId] = StarPosCache[c.StarId];
        }
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                if (i == j) { d[i, j] = 0; continue; }
                if (pool[i].StarId == pool[j].StarId) { d[i, j] = 1.0; continue; }
                var a = starPos[pool[i].StarId];
                var b = starPos[pool[j].StarId];
                d[i, j] = Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));
            }
        return d;
    }

    /// <summary>恒星坐标缓存（Build 时填充）。</summary>
    public static readonly Dictionary<int, (double x, double y, double z)> StarPosCache = new();
}

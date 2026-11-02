namespace DspPlan;

/// <summary>成本评估器（B4 v2）：中间品槽间物流 + 母星 G8 原料 + 氢物流（偏好项）。</summary>
sealed class CostModel
{
    public readonly List<Slot> Slots;
    public readonly List<Candidate> Pool;
    public readonly double[,] Dist;
    public readonly double[] ToBirth;        // 候选 → 母星
    public readonly double[] ToNearestGas;   // 候选 → 最近气巨/冰巨
    public double HydrogenWeight = 0.1;   // 氢为偏好项（用户：只稍微加强关系）
    public readonly int BirthIdx;          // 母星在 pool 中的下标（虚拟汇位置）

    public readonly Dictionary<int, List<(int slot, double amt)>> Sources = new();
    public readonly Dictionary<int, List<(int slot, double amt)>> Sinks = new();
    public readonly Dictionary<int, double> HydrogenBySlot = new();
    public readonly List<(int item, double amt)> HomeImports = new();

    public CostModel(List<Slot> slots, List<Candidate> pool, double[,] dist,
                     (double x, double y, double z) birthPos,
                     List<(int starId, string name, double x, double y, double z)> gasStars,
                     RecipeBook book, Flow flow)
    {
        Slots = slots; Pool = pool; Dist = dist;
        BirthIdx = Enumerable.Range(0, pool.Count).First(p => pool[p].IsBirth);

        // 距离辅助表
        ToBirth = new double[pool.Count];
        ToNearestGas = new double[pool.Count];
        var birthStarId = pool[0].StarId;   // 母星系
        for (int i = 0; i < pool.Count; i++)
        {
            var c = pool[i];
            var p = CandidatePool.StarPosCache[c.StarId];
            ToBirth[i] = c.StarId == birthStarId ? 1.0 : Dist3(p, birthPos);
            double best = double.MaxValue;
            foreach (var g in gasStars)
            {
                double d = g.starId == c.StarId ? 1.0 : Dist3(p, (g.x, g.y, g.z));
                if (d < best) best = d;
            }
            ToNearestGas[i] = best == double.MaxValue ? 0 : best;
        }

        // 中间品源/汇（按槽净额）
        for (int si = 0; si < slots.Count; si++)
        {
            foreach (var (item, net) in slots[si].Net())
            {
                if (net > 1e-6)
                {
                    if (!Sources.TryGetValue(item, out var l)) Sources[item] = l = new();
                    l.Add((si, net));
                }
                else if (net < -1e-6)
                {
                    if (!Sinks.TryGetValue(item, out var l)) Sinks[item] = l = new();
                    l.Add((si, -net));
                }
            }        }

        // 氢需求（槽）
        for (int si = 0; si < slots.Count; si++)
        {
            var s = slots[si];
            double h2 = 0;
            if (s.Group == "重氢")
                h2 = flow.Demand.GetValueOrDefault(Route.Deuterium) / Math.Max(1, CountOf("重氢"));
            else
                h2 = s.Consumption.GetValueOrDefault(1120);
            if (h2 > 1e-6) HydrogenBySlot[si] = h2;
        }

        // 母星 G8 进口原料（非 G8 产品）
        var home = new HashSet<int>(ProgramHome());
        var homeConsume = new Dictionary<int, double>();
        foreach (var item in home)
        {
            double qty = flow.Demand.GetValueOrDefault(item);
            if (qty <= 0) continue;
            var recipes = flow.ChooseRecipes(item);
            if (recipes.Count == 0) continue;
            double share = 1.0 / recipes.Count;
            foreach (var r in recipes)
            {
                double outPerRun = 0;
                foreach (var (rid, n) in r.Results) if (rid == item) outPerRun += n;
                if (outPerRun <= 0) continue;
                double runs = qty * share / (outPerRun * (1 + 0.25 * flow.SprayPercent(r.Id) / 100.0));
                foreach (var (ing, n) in r.Items)
                    homeConsume[ing] = homeConsume.GetValueOrDefault(ing) + runs * n;
            }
        }
        foreach (var (ing, amt) in homeConsume)
            if (!home.Contains(ing) && amt > 1e-6)
                HomeImports.Add((ing, amt));

        // 母星作为虚拟汇（slot = -1）并入 Sinks：与中间品共享源槽净余（与 CP-SAT 模型一致）
        foreach (var (item, amt) in HomeImports)
        {
            if (!Sinks.TryGetValue(item, out var l)) Sinks[item] = l = new();
            l.Add((-1, amt));
        }
    }

    static int CountOf(string group) => DspPlan.Slots.Groups.First(g => g.Name == group).Count;
    static int[] ProgramHome() => DspPlan.Slots.Home;

    static double Dist3((double x, double y, double z) a, (double x, double y, double z) b)
        => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));

    /// <summary>成本分解：中间品（含母星共享源）/ 母星增量 / 氢。</summary>
    public (double mid, double home, double hydrogen) Breakdown(int[] layout)
    {
        double mid = 0, home = 0, h2 = 0;
        foreach (var (item, srcList) in Sources)
        {
            if (!Sinks.TryGetValue(item, out var sinkList)) continue;
            var src = new List<(int idx, double amt)>(srcList.Count);
            foreach (var (slot, amt) in srcList) src.Add((layout[slot], amt));

            var snkAll = new List<(int idx, double amt)>(sinkList.Count);
            foreach (var (slot, amt) in sinkList) snkAll.Add((slot == -1 ? BirthIdx : layout[slot], amt));
            double total = Transport.Solve(src, snkAll, Dist);

            double midOnly = 0;
            var snkSlots = sinkList.Where(z => z.slot >= 0).Select(z => (layout[z.slot], z.amt)).ToList();
            if (snkSlots.Count > 0) midOnly = Transport.Solve(src, snkSlots, Dist);
            mid += midOnly;
            home += total - midOnly;
        }
        foreach (var (slot, amt) in HydrogenBySlot)
            h2 += amt * ToNearestGas[layout[slot]] * HydrogenWeight;
        return (mid, home, h2);
    }

    /// <summary>给定布局（layout[slotIdx] = poolIdx）计算总成本。</summary>
    public double Evaluate(int[] layout)
    {
        double cost = 0;

        // 1) 中间品 + 母星（统一分配，共享源槽净余；母星为虚拟汇 slot=-1 → BirthIdx）
        foreach (var (item, srcList) in Sources)
        {
            if (!Sinks.TryGetValue(item, out var sinkList)) continue;
            var src = new List<(int idx, double amt)>(srcList.Count);
            foreach (var (slot, amt) in srcList) src.Add((layout[slot], amt));
            var snk = new List<(int idx, double amt)>(sinkList.Count);
            foreach (var (slot, amt) in sinkList) snk.Add((slot == -1 ? BirthIdx : layout[slot], amt));
            cost += Transport.Solve(src, snk, Dist);
        }

        // 2) 氢：偏好项（最近气巨/冰巨）
        foreach (var (slot, amt) in HydrogenBySlot)
            cost += amt * ToNearestGas[layout[slot]] * HydrogenWeight;

        return cost;
    }
}

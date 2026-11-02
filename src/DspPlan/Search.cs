namespace DspPlan;

/// <summary>C4：模拟退火选址（relocate / swap 邻域，多起点，可断点续跑）。</summary>
sealed class Annealing
{
    readonly CostModel model;
    readonly List<Slot> slots;
    readonly List<int> usable;        // pool 下标（可用候选，排除母星）
    readonly bool[] lowOpen;
    readonly int[] slotGroupSlots;    // slot → 该组槽数（=1 的单槽产品不能放低开阔度）

    public double BestCost = double.MaxValue;
    public int[] BestLayout;
    public readonly List<(double cost, int[] layout)> Elite = new();
    public long Steps, Accepted;
    public double ElapsedSec;

    public readonly Dictionary<int, int> Locked;   // slotIdx → poolIdx（锁定布局）

    public Annealing(CostModel model, List<Slot> slots, List<Candidate> pool,
                     Dictionary<int, int> locked = null)
    {
        this.model = model;
        this.slots = slots;
        Locked = locked ?? new Dictionary<int, int>();
        usable = Enumerable.Range(0, pool.Count).Where(i => !pool[i].IsBirth).ToList();
        lowOpen = pool.Select(c => c.LowOpen).ToArray();
        slotGroupSlots = slots.Select(s => Slots.Groups.First(g => g.Name == s.Group).Count).ToArray();
    }

    bool Allowed(int slotIdx, int poolIdx) => !(slotGroupSlots[slotIdx] <= 1 && lowOpen[poolIdx]);

    public int[] RandomLayout(Random rnd)
    {
        var layout = new int[slots.Count];
        var used = new HashSet<int>(Locked.Values);
        foreach (var (si, pi) in Locked) layout[si] = pi;
        var free = usable.Where(i => !used.Contains(i)).ToList();
        Shuffle(free, rnd);
        int k = 0;
        for (int si = 0; si < slots.Count; si++)
        {
            if (Locked.ContainsKey(si)) continue;
            while (k < free.Count && !Allowed(si, free[k])) k++;
            if (k >= free.Count) throw new Exception("可用行星不足（锁定占用过多）");
            layout[si] = free[k++];
        }
        return layout;
    }

    static void Shuffle<T>(IList<T> a, Random rnd)
    {
        for (int i = a.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
    }

    static bool[] BuildOccupied(int[] layout, int n)
    {
        var occ = new bool[n];
        foreach (var p in layout) if (p >= 0) occ[p] = true;
        return occ;
    }

    /// <summary>从 start 出发跑一次退火，返回最优成本。</summary>
    public double Run(int[] start, TimeSpan budget, int seed, Action<string> log = null,
                      Action<double, int[]> onNewBest = null, double stagnationSec = 0)
    {
        var rnd = new Random(seed);
        var layout = (int[])start.Clone();
        var occupied = BuildOccupied(layout, lowOpen.Length);
        double cur = model.Evaluate(layout);
        if (cur < BestCost) { BestCost = cur; BestLayout = (int[])layout.Clone(); AddElite(cur, layout); onNewBest?.Invoke(cur, layout); }

        // 采样平均 |Δ| 作为 T0
        double sum = 0; int samples = 0;
        for (int t = 0; t < 300; t++)
        {
            int si = rnd.Next(slots.Count);
            int from = layout[si];
            int to = usable[rnd.Next(usable.Count)];
            if (to == from || occupied[to] || !Allowed(si, to)) continue;
            layout[si] = to;
            sum += Math.Abs(model.Evaluate(layout) - cur);
            layout[si] = from;
            samples++;
        }
        double T0 = Math.Max(1e-9, sum / Math.Max(1, samples));
        double TEnd = T0 * 1e-4;
        double total = Math.Max(0.001, budget.TotalSeconds);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double localBest = cur, localImproveAt = 0;
        while (true)
        {
            double frac = sw.Elapsed.TotalSeconds / total;
            if (frac >= 1.0) break;
            // 早停：连续 stagnationSec 秒本链无改进
            if (stagnationSec > 0 && sw.Elapsed.TotalSeconds - localImproveAt > stagnationSec)
            {
                log?.Invoke($"  早停: {sw.Elapsed.TotalSeconds:F0}s，已 {stagnationSec:F0}s 无改进（本链 {localBest:N0}）");
                break;
            }
            double T = T0 * Math.Pow(TEnd / T0, frac);

            if (rnd.NextDouble() < 0.85)
            {
                // relocate
                int si = rnd.Next(slots.Count);
                if (Locked.ContainsKey(si)) { Steps++; continue; }
                int from = layout[si];
                int to = usable[rnd.Next(usable.Count)];
                if (to == from || occupied[to] || !Allowed(si, to)) { Steps++; continue; }
                layout[si] = to;
                double nv = model.Evaluate(layout);
                double d = nv - cur;
                if (d <= 0 || rnd.NextDouble() < Math.Exp(-d / T))
                {
                    cur = nv; occupied[from] = false; occupied[to] = true; Accepted++;
                    if (cur < localBest) { localBest = cur; localImproveAt = sw.Elapsed.TotalSeconds; }
                    if (cur < BestCost)
                    {
                        BestCost = cur; BestLayout = (int[])layout.Clone(); AddElite(cur, layout);
                        onNewBest?.Invoke(cur, layout);
                        log?.Invoke($"  [{sw.Elapsed.TotalSeconds:F0}s] 新最优 {cur:N0}  steps={Steps:N0}");
                    }
                }
                else layout[si] = from;
            }
            else
            {
                // swap
                int a = rnd.Next(slots.Count), b = rnd.Next(slots.Count);
                if (a == b || Locked.ContainsKey(a) || Locked.ContainsKey(b)) { Steps++; continue; }
                int pa = layout[a], pb = layout[b];
                if (!Allowed(a, pb) || !Allowed(b, pa)) { Steps++; continue; }
                layout[a] = pb; layout[b] = pa;
                double nv = model.Evaluate(layout);
                double d = nv - cur;
                if (d <= 0 || rnd.NextDouble() < Math.Exp(-d / T))
                {
                    cur = nv; Accepted++;
                    if (cur < localBest) { localBest = cur; localImproveAt = sw.Elapsed.TotalSeconds; }
                    if (cur < BestCost)
                    {
                        BestCost = cur; BestLayout = (int[])layout.Clone(); AddElite(cur, layout);
                        onNewBest?.Invoke(cur, layout);
                        log?.Invoke($"  [{sw.Elapsed.TotalSeconds:F0}s] 新最优 {cur:N0}  steps={Steps:N0}");
                    }
                }
                else { layout[a] = pa; layout[b] = pb; }
            }
            Steps++;
        }
        ElapsedSec = sw.Elapsed.TotalSeconds;
        return BestCost;
    }

    public void AddElite(double cost, int[] layout)
    {
        Elite.Add((cost, (int[])layout.Clone()));
        Elite.Sort((a, b) => a.cost.CompareTo(b.cost));
        if (Elite.Count > 20) Elite.RemoveRange(20, Elite.Count - 20);
    }
}

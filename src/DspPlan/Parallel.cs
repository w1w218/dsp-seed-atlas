using System.Text.Json;
using System.Text;

namespace DspPlan;

/// <summary>全局最优池（多链共享，线程安全）。</summary>
sealed class GlobalBest
{
    readonly object _lock = new();
    public double Cost = double.MaxValue;
    public int[] Layout;
    public readonly List<(double cost, int[] layout)> Elite = new();
    public long Steps;
    public double ElapsedSec;

    public void Offer(double c, int[] l)
    {
        lock (_lock)
        {
            if (c < Cost) { Cost = c; Layout = (int[])l.Clone(); }
            Elite.Add((c, (int[])l.Clone()));
            Elite.Sort((a, b) => a.cost.CompareTo(b.cost));
            if (Elite.Count > 20) Elite.RemoveRange(20, Elite.Count - 20);
        }
    }

    public void AddSteps(long n) { lock (_lock) Steps += n; }

    public (double cost, int[] layout) Snapshot()
    {
        lock (_lock) return (Cost, Layout == null ? null : (int[])Layout.Clone());
    }
}

/// <summary>断点文件（plan-search.json）。</summary>
sealed class SearchCheckpoint
{
    public double BestCost { get; set; }
    public int[] BestLayout { get; set; }
    public double ElapsedSec { get; set; }
    public long Steps { get; set; }
    public string Galaxy { get; set; } = "";
    public int Seed { get; set; }
    public string SavedAt { get; set; } = "";

    public static SearchCheckpoint Load(string path)
        => JsonSerializer.Deserialize<SearchCheckpoint>(File.ReadAllText(path));

    public void Save(string path)
    {
        SavedAt = DateTime.Now.ToString("s");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}

static class ParallelSearch
{
    /// <summary>多链并行退火；resumeFrom 非空时以它为初始解之一。</summary>
    public static GlobalBest Run(CostModel model, List<Slot> slots, List<Candidate> pool,
        int threads, TimeSpan budget, int seed, Action<string> log, string ckptPath,
        Action<string> logp = null, double saveEverySec = 60, GlobalBest resume = null,
        double stagnationSec = 0, Dictionary<int, int> locked = null)
    {
        var global = resume ?? new GlobalBest();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = new Task[threads];

        for (int t = 0; t < threads; t++)
        {
            int tid = t;
            tasks[t] = Task.Run(() =>
            {
                var a = new Annealing(model, slots, pool, locked);
                var rnd = new Random(seed * 1000 + tid);
                var start = (tid == 0 && global.Layout != null) ? (int[])global.Layout.Clone() : a.RandomLayout(rnd);
                double localBest = a.Run(start, budget, seed * 1000 + tid, null,
                    (c, l) => global.Offer(c, l), stagnationSec);
                global.AddSteps(a.Steps);
                logp?.Invoke($"  链 {tid}: {localBest:N0}（{a.ElapsedSec:F0}s，{a.Steps:N0} 步）");
            });
        }

        double nextSave = saveEverySec;
        while (true)
        {
            bool all = Task.WaitAll(tasks, 1000);
            double el = sw.Elapsed.TotalSeconds;
            if (all) break;
            if (el >= nextSave && ckptPath != null)
            {
                var (c, l) = global.Snapshot();
                if (l != null)
                    new SearchCheckpoint { BestCost = c, BestLayout = l, ElapsedSec = el, Steps = global.Steps }
                        .Save(ckptPath);
                log?.Invoke($"  [{el:F0}s] checkpoint 已存  最优 {c:N0}");
                nextSave += saveEverySec;
            }
        }
        global.ElapsedSec = sw.Elapsed.TotalSeconds;

        if (ckptPath != null)
        {
            var (c, l) = global.Snapshot();
            if (l != null)
                new SearchCheckpoint { BestCost = c, BestLayout = l, ElapsedSec = global.ElapsedSec, Steps = global.Steps }
                    .Save(ckptPath);
        }
        return global;
    }
}

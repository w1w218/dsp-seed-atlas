using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DspPlan;

// ---------- 数据模型 ----------
sealed class ItemDef
{
    public int Id; public string Name = ""; public string Type = ""; public string MiningFrom = "";
    public bool IsCollected => Type.Contains("Mining") || CollectedByRule.Contains(Id);
    public static readonly HashSet<int> CollectedByRule = new() { 1116, 1117, 1120, 1208, 1000 };
}

sealed class RecipeDef
{
    public int Id; public string Name = ""; public string Type = ""; public int TimeSpend;
    public List<(int id, int n)> Items = new();
    public List<(int id, int n)> Results = new();
    public int PrimaryResult => Results.Count > 0 ? Results[0].id : 0;
}

sealed class RecipeBook
{
    public readonly Dictionary<int, ItemDef> Items = new();
    public readonly Dictionary<int, RecipeDef> Recipes = new();
    public readonly Dictionary<int, List<RecipeDef>> ByResult = new();

    public static RecipeBook Load(string csvDir)
    {
        var b = new RecipeBook();
        foreach (var line in File.ReadLines(Path.Combine(csvDir, "items.csv")).Skip(1))
        {
            var f = line.Split(',');
            if (f.Length < 4) continue;
            var it = new ItemDef { Id = int.Parse(f[0]), Name = f[1], Type = f[2], MiningFrom = f[4] };
            b.Items[it.Id] = it;
        }
        foreach (var line in File.ReadLines(Path.Combine(csvDir, "recipes.csv")).Skip(1))
        {
            var f = line.Split(',');
            if (f.Length < 7) continue;
            var r = new RecipeDef
            {
                Id = int.Parse(f[0]), Name = f[1], Type = f[2],
                TimeSpend = int.Parse(f[3]),
                Items = ParsePairs(f[5]), Results = ParsePairs(f[6])
            };
            b.Recipes[r.Id] = r;
            foreach (var (id, _) in r.Results)
                (b.ByResult.TryGetValue(id, out var l) ? l : b.ByResult[id] = new()).Add(r);
        }
        return b;
    }

    static List<(int, int)> ParsePairs(string s)
    {
        var res = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(s)) return res;
        foreach (var tok in s.Split('+'))
        {
            var p = tok.Trim().Split(':');
            if (p.Length == 2) res.Add((int.Parse(p[0]), int.Parse(p[1])));
        }
        return res;
    }

    public string NameOf(int id) => Items.TryGetValue(id, out var it) ? it.Name : $"#{id}";
}

// ---------- 锁定路线（PLANNER-DESIGN A3b） ----------
static class Route
{
    // 物品 -> 配方列表（多个 = 混合配方，各占 1/份额）
    public static readonly Dictionary<int, int[]> Locked = new()
    {
        [1109] = new[] { 17 },        // 高能石墨 煤炼
        [1112] = new[] { 61 },        // 金刚石 金伯利
        [1123] = new[] { 32 },        // 石墨烯 可燃冰
        [1124] = new[] { 35, 33 },    // 碳纳米管 各半
        [1113] = new[] { 62 },        // 晶格硅 分形硅
        [1126] = new[] { 28, 29 },    // 卡西米尔 各半
        [1206] = new[] { 99 },        // 粒子容器 电磁涡轮
        [1114] = new[] { 16 },        // 精炼油 原油
        [1404] = new[] { 68 },        // 光子合并器 棱镜
    };
    public const int Deuterium = 1121;   // 重氢：分馏，净耗氢 1:1（特殊）
    public static readonly HashSet<int> DirectCollect = new() { 1116, 1117 };  // 硫酸/有机晶体 直接采集
}

// ---------- 推流 ----------
sealed class Flow
{
    readonly RecipeBook book;
    readonly Dictionary<int, double> spray = new();     // recipeId -> 喷涂百分比
    public readonly Dictionary<int, double> Demand = new();     // 物品总需求/min
    public readonly Dictionary<int, double> Runs = new();       // 配方次数/min
    public readonly Dictionary<int, double> Collected = new();  // 采集品需求/min
    readonly HashSet<int> visiting = new();

    public Flow(RecipeBook book, string matrixMd)
    {
        this.book = book;
        foreach (Match m in Regex.Matches(File.ReadAllText(matrixMd),
                     @"\|\s*R(\d+)\s+[^|]+\|\s*[^|]+\|\s*\*\*[\d,]+\*\*\s*\|\s*[\d,]+\s*\((\d+)%\)"))
            spray[int.Parse(m.Groups[1].Value)] = int.Parse(m.Groups[2].Value);
    }

    double SprayOf(int recipeId) => spray.TryGetValue(recipeId, out var p) ? p : 100.0;  // 默认全喷（3 级增产）

    public double SprayPercent(int recipeId) => SprayOf(recipeId);

    public void Expand(int item, double qty)
    {
        if (qty <= 0) return;
        Demand[item] = Demand.GetValueOrDefault(item) + qty;

        if (book.Items.TryGetValue(item, out var def) && (def.IsCollected || Route.DirectCollect.Contains(item)))
        {
            Collected[item] = Collected.GetValueOrDefault(item) + qty;
            return;
        }
        if (item == Route.Deuterium)
        {
            // 分馏 R115：净耗氢 1:1
            Demand[1120] = Demand.GetValueOrDefault(1120) + qty;
            Collected[1120] = Collected.GetValueOrDefault(1120) + qty;
            Runs[115] = Runs.GetValueOrDefault(115) + qty;
            return;
        }

        var recipes = ChooseRecipes(item);
        if (recipes.Count == 0)
        {
            Collected[item] = Collected.GetValueOrDefault(item) + qty;
            return;
        }
        if (!visiting.Add(item)) throw new Exception($"配方循环: {book.NameOf(item)}");
        double share = 1.0 / recipes.Count;
        foreach (var r in recipes)
        {
            double outPerRun = 0;
            foreach (var (rid, n) in r.Results) if (rid == item) outPerRun += n;
            if (outPerRun <= 0) continue;
            double runs = qty * share / (outPerRun * (1 + 0.25 * SprayOf(r.Id) / 100.0));
            Runs[r.Id] = Runs.GetValueOrDefault(r.Id) + runs;
            foreach (var (ing, n) in r.Items)
                Expand(ing, runs * n);
        }
        visiting.Remove(item);
    }

    public List<RecipeDef> ChooseRecipes(int item)
    {
        if (Route.Locked.TryGetValue(item, out var ids))
            return ids.Select(id => book.Recipes[id]).ToList();
        if (!book.ByResult.TryGetValue(item, out var list) || list.Count == 0)
            return new List<RecipeDef>();
        // 默认：唯一配方；多个时选非高效版（保守）并提示
        if (list.Count > 1)
            Console.WriteLine($"  [警告] {book.NameOf(item)} 有 {list.Count} 个配方，默认取第一个: R{list[0].Id}");
        return new List<RecipeDef> { list[0] };
    }
}

// ---------- 槽位 ----------
sealed class SlotGroup
{
    public string Name = "";
    public int Count;                 // 槽数
    public int[] Products = Array.Empty<int>();
    public int[] Attachments = Array.Empty<int>();  // 只在槽 0 生产
}

sealed class Slot
{
    public string Id = "";
    public string Group = "";
    public int Index;
    public bool IsMain;               // 槽 0（附属品宿主）
    public Dictionary<int, double> Output = new();   // 承担量
    public Dictionary<int, double> Consumption = new();
    /// <summary>净额 = 产出 − 本槽消耗（包含只消耗不产出的物品）。</summary>
    public Dictionary<int, double> Net()
    {
        var keys = new HashSet<int>(Output.Keys);
        keys.UnionWith(Consumption.Keys);
        var d = new Dictionary<int, double>(keys.Count);
        foreach (var k in keys) d[k] = Output.GetValueOrDefault(k) - Consumption.GetValueOrDefault(k);
        return d;
    }
}

static class Slots
{
    public static readonly SlotGroup[] Groups =
    {
        // 独立
        new() { Name = "铁块",   Count = 3, Products = new[]{1101}, Attachments = new[]{1103,1107} },
        new() { Name = "高纯硅", Count = 3, Products = new[]{1105} },
        new() { Name = "铜块",   Count = 2, Products = new[]{1104} },
        new() { Name = "钛块",   Count = 2, Products = new[]{1106} },
        new() { Name = "石墨烯", Count = 2, Products = new[]{1123} },
        new() { Name = "碳纳米管", Count = 2, Products = new[]{1124} },
        new() { Name = "精炼油", Count = 2, Products = new[]{1114} },
        new() { Name = "高能石墨", Count = 1, Products = new[]{1109} },
        new() { Name = "玻璃",   Count = 1, Products = new[]{1110}, Attachments = new[]{1111,1404} },
        new() { Name = "晶格硅", Count = 1, Products = new[]{1113} },
        new() { Name = "钛晶石", Count = 1, Products = new[]{1118} },
        new() { Name = "钛化玻璃", Count = 1, Products = new[]{1119} },
        new() { Name = "卡西米尔", Count = 1, Products = new[]{1126} },
        new() { Name = "重氢",   Count = 1, Products = new[]{1121} },
        new() { Name = "电路板", Count = 1, Products = new[]{1301} },
        new() { Name = "信息矩阵", Count = 1, Products = new[]{6004} },
        new() { Name = "引力矩阵", Count = 1, Products = new[]{6005} },
        new() { Name = "宇宙矩阵", Count = 1, Products = new[]{6006} },
        // 共星（槽 0 附属品）
        new() { Name = "三色矩阵", Count = 1, Products = new[]{6001,6002,6003} },
        new() { Name = "燃料棒+湮灭球", Count = 1, Products = new[]{1803,1403} },
        new() { Name = "涡轮组", Count = 2, Products = new[]{1204,1203,1201}, Attachments = new[]{1205} },
        new() { Name = "位面+量子芯片", Count = 1, Products = new[]{1304,1305} },
        new() { Name = "奇异物质组", Count = 2, Products = new[]{1127,1206} },
        new() { Name = "磁线圈组", Count = 2, Products = new[]{1202,1102} },
        new() { Name = "透镜+金刚石", Count = 1, Products = new[]{1209,1112} },
        new() { Name = "处理器组", Count = 2, Products = new[]{1303,1302} },
        new() { Name = "粒子宽带+塑料", Count = 1, Products = new[]{1402,1115} },
    };

    // 母星 G8（固定，不占槽位）
    public static readonly int[] Home = { 1125, 1501, 1502, 1503, 1802, 1122 };

    public static List<Slot> Build(Flow flow, RecipeBook book)
    {
        var slots = new List<Slot>();
        foreach (var g in Groups)
        {
            for (int i = 0; i < g.Count; i++)
            {
                var s = new Slot { Id = $"{g.Name}-{i}", Group = g.Name, Index = i, IsMain = i == 0 };
                foreach (var p in g.Products)
                    s.Output[p] = flow.Demand.GetValueOrDefault(p) / g.Count;
                if (s.IsMain)
                    foreach (var a in g.Attachments)
                        s.Output[a] = flow.Demand.GetValueOrDefault(a);  // 附属品不摊分

                // 本槽消耗（按配方，含喷涂）
                foreach (var (item, outQty) in s.Output)
                {
                    var recipes = Route.Locked.TryGetValue(item, out var ids)
                        ? ids.Select(id => book.Recipes[id]).ToList()
                        : (book.ByResult.TryGetValue(item, out var l) && l.Count > 0 ? new List<RecipeDef> { l[0] } : new List<RecipeDef>());
                    if (item == Route.Deuterium) continue;      // 重氢耗氢由氢物流单独处理
                    if (recipes.Count == 0) continue;
                    double share = 1.0 / recipes.Count;
                    foreach (var r in recipes)
                    {
                        double outPerRun = 0;
                        foreach (var (rid, n) in r.Results) if (rid == item) outPerRun += n;
                        if (outPerRun <= 0) continue;
                        double sprayPct = flow.SprayPercent(r.Id);
                        double runs = outQty * share / (outPerRun * (1 + 0.25 * sprayPct / 100.0));
                        foreach (var (ing, n) in r.Items)
                            s.Consumption[ing] = s.Consumption.GetValueOrDefault(ing) + runs * n;
                    }
                }
                slots.Add(s);
            }
        }
        return slots;
    }
}

// ---------- 入口 ----------
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "eval") return RunEval(args);
        if (args.Length > 0 && args[0] == "search") return RunSearch(args);
        if (args.Length > 0 && args[0] == "verify") return RunVerify(args);
        if (args.Length > 0 && args[0] == "cpsat") return RunCpSat(args);
        if (args.Length > 0 && args[0] == "verifyflow") return RunVerifyFlow(args);
        if (args.Length > 0 && args[0] == "lns") return RunLns(args);
        if (args.Length > 0 && args[0] == "apply") return RunApply(args);

        string dataDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "data");
        string outDir = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);

        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));

        Console.WriteLine($"物品 {book.Items.Count}，配方 {book.Recipes.Count}");

        // 1) 先展开火箭 + 反物质棒（它们可能消耗太阳帆）
        flow.Expand(1503, 150);          // 小型运载火箭
        flow.Expand(1803, 1.5);          // 反物质燃料棒
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        Console.WriteLine($"火箭/燃料棒链内部消耗太阳帆: {sailInternal:N1}/min");

        // 2) 太阳帆总需求 20,900（含内部）→ 补足外部
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));

        // 3) 白糖（宇宙矩阵）
        flow.Expand(6006, 100000);

        // 4) 槽位 + 净额
        var slots = Slots.Build(flow, book);

        // 5) 自检：制造品净额平衡
        var netSum = new Dictionary<int, double>();
        foreach (var s in slots)
            foreach (var (item, net) in s.Net())
                netSum[item] = netSum.GetValueOrDefault(item) + net;

        Console.WriteLine("\n===== 推流结果（关键物品）=====");
        foreach (var item in new[] { 1101, 1104, 1105, 1106, 1202, 1206, 6006, 1501, 1503, 1121 })
            Console.WriteLine($"  {book.NameOf(item),-10} 需求 {flow.Demand.GetValueOrDefault(item),12:N1}  " +
                              $"净额和 {netSum.GetValueOrDefault(item),12:N1}");

        Console.WriteLine("\n===== 配方次数（对照 matrix-100k.md）=====");
        foreach (var rid in new[] { 1, 3, 9, 75, 6, 51, 108 })
            Console.WriteLine($"  R{rid,-3} {book.Recipes.GetValueOrDefault(rid)?.Name,-12} {flow.Runs.GetValueOrDefault(rid),12:N0}");

        Console.WriteLine("\n===== 采集品需求 =====");
        foreach (var kv in flow.Collected.OrderByDescending(k => k.Value).Take(20))
            Console.WriteLine($"  {book.NameOf(kv.Key),-10} {kv.Value,12:N0}");

        // 6) 输出
        var demandJson = new
        {
            meta = new { generatedAt = DateTime.Now.ToString("s"), target = "宇宙矩阵 100000/min + 太阳帆 20900 + 火箭 150 + 反物质棒 1.5" },
            items = flow.Demand.OrderByDescending(k => k.Value).Select(k => new { id = k.Key, name = book.NameOf(k.Key), demand = Math.Round(k.Value, 3) }),
            recipeRuns = flow.Runs.OrderBy(k => k.Key).Select(k => new { id = k.Key, name = book.Recipes.TryGetValue(k.Key, out var r) ? r.Name : "", runs = Math.Round(k.Value, 3) }),
            collected = flow.Collected.OrderByDescending(k => k.Value).Select(k => new { id = k.Key, name = book.NameOf(k.Key), demand = Math.Round(k.Value, 3) })
        };
        File.WriteAllText(Path.Combine(outDir, "plan_demand.json"),
            JsonSerializer.Serialize(demandJson, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));

        var slotJson = new
        {
            meta = new { generatedAt = DateTime.Now.ToString("s"), slotCount = slots.Count, home = Slots.Home.Select(id => new { id, name = book.NameOf(id) }) },
            slots = slots.Select(s => new
            {
                id = s.Id, group = s.Group, index = s.Index, isMain = s.IsMain,
                products = s.Output.Select(kv => new { id = kv.Key, name = book.NameOf(kv.Key), output = Math.Round(kv.Value, 3), attachment = Array.IndexOf(Slots.Groups.First(g => g.Name == s.Group).Attachments, kv.Key) >= 0 }),
                net = s.Net().Where(kv => Math.Abs(kv.Value) > 1e-6).ToDictionary(kv => kv.Key.ToString(), kv => Math.Round(kv.Value, 3))
            })
        };
        File.WriteAllText(Path.Combine(outDir, "plan_slots.json"),
            JsonSerializer.Serialize(slotJson, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));

        Console.WriteLine($"\n输出: {Path.Combine(outDir, "plan_demand.json")}");
        Console.WriteLine($"输出: {Path.Combine(outDir, "plan_slots.json")}");
        Console.WriteLine($"槽位总数: {slots.Count}（应为 40）");
        return 0;
    }

    // ---------- C2 评估测试：DspPlan eval <galaxy.json> ----------
    static int RunEval(string[] args)
    {
        string galaxy = args.Length > 1 ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));

        // 推流（与 C1 相同）
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);

        // 候选池
        var (pool, birth, gas) = CandidatePool.Build(galaxy);
        var dist = CandidatePool.Distances(pool, gas);
        var usable = pool.Where(c => !c.IsBirth).ToList();
        Console.WriteLine($"候选池: {pool.Count} 颗（含母星 1，可用 {usable.Count}），氢源恒星 {gas.Count}");
        Console.WriteLine($"低开阔度: {usable.Count(c => c.LowOpen)} 颗");
        foreach (var s in usable.Take(5)) Console.WriteLine($"  {s.DistFromBirth,5:F2}ly {s.StarName} {s.PlanetName} {s.Theme}{(s.LowOpen ? " [低]" : "")}");

        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);
        var anneal = new Annealing(model, slots, pool);
        Console.WriteLine($"\n物流表: 中间品 {model.Sources.Count} 种，母星进口 {model.HomeImports.Count} 项，耗氢槽 {model.HydrogenBySlot.Count} 个");

        // 随机布局测试（性能 + 合理性）
        var rnd = new Random(1);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double best = double.MaxValue;
        int n = 200;
        for (int t = 0; t < n; t++)
        {
            int[] layout = anneal.RandomLayout(rnd);
            double c = model.Evaluate(layout);
            if (c < best) best = c;
        }
        sw.Stop();
        Console.WriteLine($"随机布局 {n} 次: 最佳成本 {best:N0}，平均 {sw.Elapsed.TotalMilliseconds / n:F2} ms/次");
        return 0;
    }

    // ---------- C4 搜索：DspPlan search [galaxy] [--seconds N] [--seed N] [--starts N] ----------
    static int RunSearch(string[] args)
    {
        string galaxy = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        double seconds = ArgDouble(args, "--seconds", 60);
        int seed = (int)ArgDouble(args, "--seed", 1);
        int threads = (int)ArgDouble(args, "--threads", Environment.ProcessorCount);
        bool resume = args.Contains("--resume");
        double stagnation = ArgDouble(args, "--stagnation", 300);
        string lockedPath = ArgStr(args, "--locked");
        int slotLimit = (int)ArgDouble(args, "--slot-limit", 0);
        int poolLimit = (int)ArgDouble(args, "--pool-limit", 0);

        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);
        if (slotLimit > 0) slots = slots.Take(slotLimit).ToList();

        var (pool0, birth, gas) = CandidatePool.Build(galaxy);
        var pool = poolLimit > 0 ? pool0.Take(poolLimit).ToList() : pool0;
        var dist = CandidatePool.Distances(pool, gas);
        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);
        var anneal = new Annealing(model, slots, pool);

        // 锁定布局（slotId → planetId）
        Dictionary<int, int> locked = null;
        if (lockedPath != null && File.Exists(lockedPath))
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(lockedPath));
            locked = new Dictionary<int, int>();
            foreach (var (slotId, planetId) in map)
            {
                int si = slots.FindIndex(s => s.Id == slotId);
                int pi = pool.FindIndex(c => c.PlanetId == planetId);
                if (si >= 0 && pi >= 0) locked[si] = pi;
                else Console.WriteLine($"  [锁定] 忽略 {slotId}→{planetId}（未找到）");
            }
            Console.WriteLine($"锁定 {locked.Count} 个槽（{lockedPath}）");
        }

        string outDir0 = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir0);
        string ckptPath = Path.Combine(outDir0, "plan-search.json");

        GlobalBest resumeBest = null;
        if (resume && File.Exists(ckptPath))
        {
            var cp = SearchCheckpoint.Load(ckptPath);
            resumeBest = new GlobalBest();
            resumeBest.Offer(cp.BestCost, cp.BestLayout);
            Console.WriteLine($"续跑: 从 {cp.BestCost:N0} 继续（已用 {cp.ElapsedSec:F0}s，步数 {cp.Steps:N0}）");
        }

        Console.WriteLine($"搜索: 槽 {slots.Count}，可用候选 {pool.Count(c => !c.IsBirth)}，预算 {seconds}s × {threads} 线程，早停 {stagnation:F0}s");

        var global = ParallelSearch.Run(model, slots, pool, threads, TimeSpan.FromSeconds(seconds),
            seed, m => Console.WriteLine(m), ckptPath, m => Console.WriteLine(m), 60, resumeBest,
            stagnation, locked);

        Console.WriteLine($"\n最优成本 {global.Cost:N0}  用时 {global.ElapsedSec:F1}s  步数 {global.Steps:N0}");
        if (global.Layout != null)
        {
            var (mid, home, h2) = model.Breakdown(global.Layout);
            Console.WriteLine($"成本分解: 中间品 {mid:N0} ({mid / (mid + home + h2) * 100:F1}%)  母星 {home:N0} ({home / (mid + home + h2) * 100:F1}%)  氢 {h2:N0} ({h2 / (mid + home + h2) * 100:F1}%)");
        }
        Console.WriteLine("\n布局:");
        for (int i = 0; i < slots.Count; i++)
        {
            var c = pool[global.Layout[i]];
            Console.WriteLine($"  {slots[i].Id,-16} → {c.StarName} {c.PlanetName} ({c.Theme}, {c.DistFromBirth:F2}ly){(c.LowOpen ? " [低]" : "")}");
        }

        // 写结果
        string outDir = Path.Combine(AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(outDir);

        // 精英池共识（每槽位置频率）
        Console.WriteLine("\n精英池共识（top-20 解中的位置）:");
        for (int si = 0; si < slots.Count; si++)
        {
            var counts = new Dictionary<int, int>();
            foreach (var (_, l) in global.Elite)
                counts[l[si]] = counts.GetValueOrDefault(l[si]) + 1;
            var top = counts.OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => $"{pool[kv.Key].StarName} {pool[kv.Key].PlanetName}({kv.Value}/{global.Elite.Count})");
            Console.WriteLine($"  {slots[si].Id,-16} {string.Join(" | ", top)}");
        }

        var (m, h, hy) = model.Breakdown(global.Layout);
        var result = new
        {
            meta = new
            {
                galaxy, seconds, seed, threads, bestCost = global.Cost,
                steps = global.Steps,
                breakdown = new { mid = m, home = h, hydrogen = hy },
                hydrogenWeight = model.HydrogenWeight
            },
            slots = slots.Select((s, i) => new
            {
                slot = s.Id, group = s.Group,
                planetId = pool[global.Layout[i]].PlanetId,
                star = pool[global.Layout[i]].StarName,
                planet = pool[global.Layout[i]].PlanetName,
                theme = pool[global.Layout[i]].Theme,
                dist = Math.Round(pool[global.Layout[i]].DistFromBirth, 3)
            })
        };
        string outPath = Path.Combine(outDir, "plan_result.json");
        File.WriteAllText(outPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));
        Console.WriteLine($"\n结果: {outPath}");
        return 0;
    }

    // ---------- 局部最优验证：DspPlan verify ----------
    static int RunVerify(string[] args)
    {
        string galaxy = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);
        var (pool, birth, gas) = CandidatePool.Build(galaxy);
        var dist = CandidatePool.Distances(pool, gas);
        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);

        string ckpt = Path.Combine(AppContext.BaseDirectory, "out", "plan-search.json");
        if (!File.Exists(ckpt)) { Console.WriteLine("无 checkpoint（先跑 search）"); return 1; }
        var cp = SearchCheckpoint.Load(ckpt);
        var layout = cp.BestLayout;
        double cur = model.Evaluate(layout);
        Console.WriteLine($"当前成本 {cur:N0}（checkpoint）");

        var groupCount = slots.Select(s => Slots.Groups.First(g => g.Name == s.Group).Count).ToArray();
        bool Allowed(int si, int pi) => !(groupCount[si] <= 1 && pool[pi].LowOpen);
        var occupied = new bool[pool.Count];
        foreach (var p in layout) occupied[p] = true;

        // 1-opt：单槽移到空星
        double best1 = 0; string d1 = "";
        for (int si = 0; si < slots.Count; si++)
        {
            int from = layout[si];
            for (int pi = 0; pi < pool.Count; pi++)
            {
                if (pool[pi].IsBirth || occupied[pi] || pi == from || !Allowed(si, pi)) continue;
                layout[si] = pi;
                double c = model.Evaluate(layout);
                layout[si] = from;
                if (c - cur < best1) { best1 = c - cur; d1 = $"{slots[si].Id} → {pool[pi].StarName} {pool[pi].PlanetName}"; }
            }
        }
        Console.WriteLine($"1-opt: 最佳改进 {best1:N0}{(best1 < -1e-6 ? $"  ⚠ 可改进: {d1}" : "  ✓ 无可改进")}");

        // 2-opt：两槽交换
        double best2 = 0; string d2 = "";
        for (int a = 0; a < slots.Count; a++)
            for (int b = a + 1; b < slots.Count; b++)
            {
                int pa = layout[a], pb = layout[b];
                if (!Allowed(a, pb) || !Allowed(b, pa)) continue;
                layout[a] = pb; layout[b] = pa;
                double c = model.Evaluate(layout);
                layout[a] = pa; layout[b] = pb;
                if (c - cur < best2) { best2 = c - cur; d2 = $"{slots[a].Id} ⇄ {slots[b].Id}"; }
            }
        Console.WriteLine($"2-opt: 最佳改进 {best2:N0}{(best2 < -1e-6 ? $"  ⚠ 可改进: {d2}" : "  ✓ 无可改进")}");

        Console.WriteLine(best1 > -1e-6 && best2 > -1e-6
            ? "结论: 当前解是 1-opt + 2-opt 局部最优"
            : "结论: 仍有改进空间，建议继续搜索");
        return 0;
    }

    // ---------- C5 CP-SAT：DspPlan cpsat [galaxy] [--seconds N] [--threads N] ----------
    static int RunCpSat(string[] args)
    {
        string galaxy = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        double seconds = ArgDouble(args, "--seconds", 600);
        int threads = (int)ArgDouble(args, "--threads", Environment.ProcessorCount);
        int slotLimit = (int)ArgDouble(args, "--slot-limit", 0);
        int poolLimit = (int)ArgDouble(args, "--pool-limit", 0);

        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);
        if (slotLimit > 0) slots = slots.Take(slotLimit).ToList();
        var (pool0, birth, gas) = CandidatePool.Build(galaxy);
        var pool = poolLimit > 0 ? pool0.Take(poolLimit).ToList() : pool0;
        var dist = CandidatePool.Distances(pool, gas);
        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);

        Console.WriteLine($"CP-SAT: 槽 {slots.Count}，候选 {pool.Count}，变量约 {slots.Count * pool.Count} 布尔，限时 {seconds:F0}s");
        int[] hint = null;
        string hintPath = ArgStr(args, "--hint");
        if (hintPath != null && File.Exists(hintPath))
        {
            var cp = SearchCheckpoint.Load(hintPath);
            hint = cp.BestLayout;
            Console.WriteLine($"hint: 退火解 {cp.BestCost:N0}");
        }
        Dictionary<int, int> fixedSlots = null;
        string lockPath = ArgStr(args, "--locked");
        if (lockPath != null && File.Exists(lockPath))
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(lockPath));
            fixedSlots = new Dictionary<int, int>();
            foreach (var (slotId, planetId) in map)
            {
                int si = slots.FindIndex(s => s.Id == slotId);
                int pi = pool.FindIndex(c => c.PlanetId == planetId);
                if (si >= 0 && pi >= 0) fixedSlots[si] = pi;
            }
            Console.WriteLine($"固定 {fixedSlots.Count} 个槽，自由 {slots.Count - fixedSlots.Count} 个");
        }
        var (status, obj, bound, layout, sec) = CpSatModel.Solve(model, slots, pool, seconds, threads, null, hint, fixedSlots);
        Console.WriteLine($"状态 {status}  目标 {obj:N0}  下界 {bound:N0}  用时 {sec:F0}s");
        if (layout != null)
        {
            double real = model.Evaluate(layout);
            Console.WriteLine($"布局重算成本 {real:N0}（应 ≈ 目标）");
            var (m, h, hy) = model.Breakdown(layout);
            Console.WriteLine($"成本分解: 中间品 {m:N0} / 母星 {h:N0} / 氢 {hy:N0}");
            for (int i = 0; i < slots.Count; i++)
            {
                var c = pool[layout[i]];
                Console.WriteLine($"  {slots[i].Id,-16} → {c.StarName} {c.PlanetName} ({c.Theme}, {c.DistFromBirth:F2}ly)");
            }
        }
        return 0;
    }

    // ---------- B：运输贪心 vs 精确最小费用流 ----------
    static int RunVerifyFlow(string[] args)
    {
        string galaxy = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);
        var (pool, birth, gas) = CandidatePool.Build(galaxy);
        var dist = CandidatePool.Distances(pool, gas);
        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);

        string ckpt = Path.Combine(AppContext.BaseDirectory, "out", "plan-search.json");
        if (!File.Exists(ckpt)) { Console.WriteLine("无 checkpoint"); return 1; }
        var layout = SearchCheckpoint.Load(ckpt).BestLayout;

        double tg = 0, te = 0;
        var diffs = new List<(int item, double g, double e, double sup)>();
        int n = 0;
        foreach (var (item, srcList) in model.Sources)
        {
            if (!model.Sinks.TryGetValue(item, out var sinkList) || sinkList.Count == 0) continue;
            var src = srcList.Select(z => (layout[z.slot], z.amt)).ToList();
            var snk = sinkList.Select(z => (z.slot == -1 ? model.BirthIdx : layout[z.slot], z.amt)).ToList();
            double g = Transport.Solve(src, snk, dist);
            double e = ExactTransport.Solve(src, snk, dist);
            tg += g; te += e; n++;
            if (double.IsNaN(e)) { Console.WriteLine($"  [无法求解] {book.NameOf(item)}"); continue; }
            if (Math.Abs(g - e) > 1) diffs.Add((item, g, e, srcList.Sum(z => z.amt)));
        }

        Console.WriteLine($"运输问题 {n} 个");
        Console.WriteLine($"贪心总成本 {tg:N0}");
        Console.WriteLine($"精确总成本 {te:N0}");
        Console.WriteLine($"差值 {tg - te:N0}（{(tg - te) / Math.Max(1, te) * 100:F3}%）");
        if (diffs.Count > 0)
        {
            Console.WriteLine("\n差异明细（贪心 - 精确）:");
            foreach (var (item, g, e, sup) in diffs.OrderByDescending(d => d.g - d.e).Take(15))
                Console.WriteLine($"  {book.NameOf(item),-10} 贪心 {g,12:N0}  精确 {e,12:N0}  差 {g - e,10:N0}  源总量 {sup:N0}");
        }
        else Console.WriteLine("\n✓ 贪心与精确完全一致");
        return 0;
    }

    // ---------- LNS：退火解 + CP-SAT 轮换窗口精修 ----------
    static int RunLns(string[] args)
    {
        string galaxy = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        int window = (int)ArgDouble(args, "--window", 12);
        int maxRounds = (int)ArgDouble(args, "--rounds", 10);
        double perSolve = ArgDouble(args, "--per-solve", 300);
        int threads = (int)ArgDouble(args, "--threads", Environment.ProcessorCount);
        int seed = (int)ArgDouble(args, "--seed", 1);

        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);
        var (pool, birth, gas) = CandidatePool.Build(galaxy);
        var dist = CandidatePool.Distances(pool, gas);
        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);

        string ckptPath = Path.Combine(AppContext.BaseDirectory, "out", "plan-search.json");
        int[] layout;
        double best;
        if (File.Exists(ckptPath))
        {
            var cp = SearchCheckpoint.Load(ckptPath);
            layout = cp.BestLayout;
            best = model.Evaluate(layout);
            Console.WriteLine($"LNS 起点: checkpoint {best:N0}（记录 {cp.BestCost:N0}）");
        }
        else
        {
            var an = new Annealing(model, slots, pool);
            layout = an.RandomLayout(new Random(seed));
            best = model.Evaluate(layout);
            Console.WriteLine($"LNS 起点: 随机 {best:N0}（无 checkpoint）");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int noImprove = 0;
        for (int round = 0; round < maxRounds && noImprove < 2; round++)
        {
            bool improvedThisRound = false;
            for (int start = 0; start < slots.Count; start += window)
            {
                int count = Math.Min(window, slots.Count - start);
                var free = new HashSet<int>(Enumerable.Range(start, count));
                var fixedSlots = new Dictionary<int, int>();
                for (int s = 0; s < slots.Count; s++)
                    if (!free.Contains(s)) fixedSlots[s] = layout[s];

                var (status, obj, bound, newLayout, sec) = CpSatModel.Solve(
                    model, slots, pool, perSolve, threads, null, layout, fixedSlots);
                if (newLayout == null)
                {
                    Console.WriteLine($"  窗口 {start}+{count}: {status}（无解）");
                    continue;
                }
                double c = model.Evaluate(newLayout);
                bool gain = c < best - 1;
                Console.WriteLine($"  窗口 {start}+{count}: CP-SAT {obj:N0}  重算 {c:N0}  当前 {best:N0}  {(gain ? "✓ 改进 " + (best - c).ToString("N0") : "—")}");
                if (gain) { best = c; layout = (int[])newLayout.Clone(); improvedThisRound = true; }
            }
            noImprove = improvedThisRound ? 0 : noImprove + 1;
            Console.WriteLine($"第 {round + 1} 轮结束: {best:N0}（{sw.Elapsed.TotalMinutes:F1} min）");
        }

        Console.WriteLine($"\nLNS 结果: {best:N0}  用时 {sw.Elapsed.TotalMinutes:F1} min");
        var (m, h, hy) = model.Breakdown(layout);
        Console.WriteLine($"成本分解: 中间品 {m:N0} / 母星 {h:N0} / 氢 {hy:N0}");

        // 保存
        string outDir = Path.Combine(AppContext.BaseDirectory, "out");
        new SearchCheckpoint { BestCost = best, BestLayout = layout, ElapsedSec = sw.Elapsed.TotalSeconds, Galaxy = galaxy }
            .Save(ckptPath);
        var result = new
        {
            meta = new { galaxy, lns = true, window, maxRounds, bestCost = best, minutes = Math.Round(sw.Elapsed.TotalMinutes, 2), breakdown = new { mid = m, home = h, hydrogen = hy } },
            slots = slots.Select((s, i) => new
            {
                slot = s.Id, group = s.Group,
                planetId = pool[layout[i]].PlanetId,
                star = pool[layout[i]].StarName,
                planet = pool[layout[i]].PlanetName,
                theme = pool[layout[i]].Theme,
                dist = Math.Round(pool[layout[i]].DistFromBirth, 3)
            })
        };
        File.WriteAllText(Path.Combine(outDir, "plan_result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new UTF8Encoding(false));
        Console.WriteLine($"结果: {Path.Combine(outDir, "plan_result.json")}");
        return 0;
    }

    // ---------- C6：plan 写回 galaxy.json + 报告 ----------
    static int RunApply(string[] args)
    {
        string galaxy = args.Length > 1 && !args[1].StartsWith("--") ? args[1] :
            throw new ArgumentException("缺少 galaxy.json 路径（用法见 docs/COMMANDS.md）");
        string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var book = RecipeBook.Load(dataDir);
        var flow = new Flow(book, Path.Combine(dataDir, "matrix-100k.md"));
        flow.Expand(1503, 150);
        flow.Expand(1803, 1.5);
        double sailInternal = flow.Demand.GetValueOrDefault(1501);
        flow.Expand(1501, Math.Max(0, 20900 - sailInternal));
        flow.Expand(6006, 100000);
        var slots = Slots.Build(flow, book);
        var (pool, birth, gas) = CandidatePool.Build(galaxy);
        var dist = CandidatePool.Distances(pool, gas);
        var model = new CostModel(slots, pool, dist, (birth[0], birth[1], birth[2]), gas, book, flow);

        string ckptPath = Path.Combine(AppContext.BaseDirectory, "out", "plan-search.json");
        if (!File.Exists(ckptPath)) { Console.WriteLine("无 checkpoint（先跑 search/lns）"); return 1; }
        var cp = SearchCheckpoint.Load(ckptPath);
        var layout = cp.BestLayout;
        double cost = model.Evaluate(layout);
        var (m, h, hy) = model.Breakdown(layout);
        Console.WriteLine($"应用布局: {cost:N0}（中间品 {m:N0} / 母星 {h:N0} / 氢 {hy:N0}）");

        // 备份
        string bak = galaxy + ".bak-plan";
        File.Copy(galaxy, bak, true);

        // 写回 plan 区块
        var g = JsonNode.Parse(File.ReadAllText(galaxy));
        var planSlots = new JsonArray();
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var c = pool[layout[i]];
            var products = new JsonArray();
            foreach (var (item, qty) in s.Output)
                products.Add(new JsonObject
                {
                    ["id"] = item, ["name"] = book.NameOf(item), ["output"] = Math.Round(qty, 3)
                });
            var net = new JsonObject();
            foreach (var (item, v) in s.Net())
                if (Math.Abs(v) > 1e-6) net[item.ToString()] = Math.Round(v, 3);
            planSlots.Add(new JsonObject
            {
                ["id"] = s.Id, ["group"] = s.Group, ["planetId"] = c.PlanetId,
                ["star"] = c.StarName, ["planet"] = c.PlanetName, ["theme"] = c.Theme,
                ["dist"] = Math.Round(c.DistFromBirth, 3),
                ["products"] = products, ["net"] = net
            });
        }
        g["plan"] = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["bestCost"] = Math.Round(cost, 1),
                ["solver"] = "anneal+lns",
                ["generatedAt"] = DateTime.Now.ToString("s"),
                ["breakdown"] = new JsonObject
                {
                    ["mid"] = Math.Round(m), ["home"] = Math.Round(h), ["hydrogen"] = Math.Round(hy)
                }
            },
            ["slots"] = planSlots
        };
        File.WriteAllText(galaxy,
            g.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }),
            new UTF8Encoding(false));
        Console.WriteLine($"已写回 plan 区块（备份 {bak}）");

        // plan-report.txt
        var sb = new StringBuilder();
        sb.AppendLine("===== 行星规划报告 =====");
        sb.AppendLine($"galaxy: {galaxy}");
        sb.AppendLine($"生成: {DateTime.Now:s}");
        sb.AppendLine($"最优成本: {cost:N0}  （中间品 {m:N0} / 母星 {h:N0} / 氢 {hy:N0}）");
        sb.AppendLine();
        sb.AppendLine("各槽位置:");
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            var c = pool[layout[i]];
            double outSum = s.Output.Values.Sum();
            sb.AppendLine($"  {s.Id,-16} → {c.StarName} {c.PlanetName,-10} ({c.Theme}, {c.DistFromBirth:F2}ly)  产出 {outSum:N0}" +
                          (c.LowOpen ? "  [低开阔度]" : ""));
        }
        sb.AppendLine();
        sb.AppendLine("物流明细:");
        foreach (var (item, srcList) in model.Sources)
        {
            if (!model.Sinks.TryGetValue(item, out var sinkList) || sinkList.Count == 0) continue;
            var src = srcList.Select(z => (idx: layout[z.slot], amt: z.amt)).ToList();
            var snk = sinkList.Select(z => (idx: z.slot == -1 ? model.BirthIdx : layout[z.slot], amt: z.amt)).ToList();
            var (c2, f) = Transport.SolveDetailed(src, snk, dist);
            if (c2 <= 0) continue;
            sb.AppendLine($"  {book.NameOf(item)}（成本 {c2:N0}）:");
            for (int i = 0; i < src.Count; i++)
                for (int j = 0; j < snk.Count; j++)
                {
                    if (f[i, j] < 1) continue;
                    string to = sinkList[j].slot == -1 ? "母星" : pool[snk[j].idx].PlanetName;
                    sb.AppendLine($"    {pool[src[i].idx].PlanetName,-10} → {to,-10}: {f[i, j],10:N0} × {dist[src[i].idx, snk[j].idx]:F2}ly = {f[i, j] * dist[src[i].idx, snk[j].idx],12:N0}");
                }
        }
        sb.AppendLine();
        sb.AppendLine("氢物流（偏好项）:");
        foreach (var (slot, amt) in model.HydrogenBySlot)
            sb.AppendLine($"  {slots[slot].Id,-16}: {amt:N0}/min → 最近气巨 {model.ToNearestGas[layout[slot]]:F2}ly");

        string outDir = Path.Combine(AppContext.BaseDirectory, "out");
        string reportPath = Path.Combine(outDir, "plan-report.txt");
        File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"报告: {reportPath}");
        return 0;
    }

    static string ArgStr(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    static double ArgDouble(string[] args, string name, double def)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
        return def;
    }
}

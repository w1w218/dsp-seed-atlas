using System.Diagnostics;
using System.IO;
using System.Text;

using DspSeedFinder.Data;
using DspSeedFinder.Gen;



namespace DspSearcher.Core;

/// <summary>搜索器：全局种子块队列 + 多线程快筛 → 精算 top 池 → 产出。
/// 线程安全前提：isRareResource 恒 false（PlanetGen.gasCoef 恒 1）；LDB 启动时预热（只读）。</summary>
public class Searcher
{
    public SearchConfig Cfg;
    public Balance Bal;
    public TopPool Pool;
    public SearchStats Stats = new();
    public string OutDir = "";

    // 事件（WPF 绑定）
    public Action<string> OnLog = _ => { };
    public Action<ProgressInfo> OnProgress = _ => { };
    public Action<List<Candidate>> OnTopChanged = _ => { };
    public Action<SearchResult> OnDone = _ => { };

    private volatile bool _cancel;
    private bool _resumePhaseDone;             // 恢复的检查点已含精算结果
    private long _startSeed;                   // 本轮起点（Resume=检查点 NextSeed；Cfg.From 保持原始配置不污染）
    private long _endSeed;                     // 本轮终点（Resume=检查点 SeedTo）
    private Stopwatch _sw = new();
    private TimeSpan _bias = TimeSpan.Zero;   // 恢复时累计历史时长
    private long _dispatched;                  // 已分发种子位置（checkpoint 断点）
    private int _refineDone;                   // 精算已完成候选数（断点续精算，跨 checkpoint 持久）
    private const int RefineCheckpointEvery = 25;   // 精算每完成 N 个候选存一次检查点（突然关闭最多丢 N 个）
    private readonly Stopwatch _refineSw = new();    // 精算阶段独立计时（速率/ETA）
    private StreamWriter _detailWriter;              // 精算流式明细：每完成一候选追加（池大防 StringBuilder OOM）
    private bool _detailStreaming;                   // 已流式写（Produce 跳过全量重写）
    private DateTime _startUtc;

    public Searcher(SearchConfig cfg)
    {
        Cfg = cfg;
        Bal = Balance.Load(Path.IsPathRooted(cfg.BalanceJson)
            ? cfg.BalanceJson
            : Path.Combine(AppContext.BaseDirectory, cfg.BalanceJson));   // 支持外部 cwd 启动 headless
        Pool = new TopPool(cfg.PoolSize);
        _startSeed = Cfg.From;
        _endSeed = Cfg.To;
    }

    public class ProgressInfo
    {
        public string Phase = "fast";          // fast / refine / done
        public long Scanned;
        public long Total;
        public long Hit;
        public double RatePerSec;
        public string Eta = "";
        public int RefineDone;
        public int RefineTotal;
    }

    public class SearchResult
    {
        public List<Candidate> Top20 = new();
        public string OutDir = "";
    }

    // ================= 入口 =================

    public void RunAsync()
    {
        _ = Task.Run(() => Run());
    }

    public void Cancel() => _cancel = true;

    /// <summary>主流程（后台线程执行）。</summary>
    public void Run()
    {
        try
        {
            Prepare();
            if (Cfg.GalaxySeed > 0) { SingleSeed(); return; }   // 单种子精算模式（不过滤）
            FastPhase();
            if (_cancel) { SaveCheckpoint(); OnLog("已停止（快筛阶段中断）"); return; }
            if (_resumePhaseDone)
            {
                OnLog("检查点已含精算结果（产出文件已存在），直接完成。");
                OnDone(new SearchResult { Top20 = Pool.Snapshot().Take(20).ToList(), OutDir = OutDir });
                return;
            }
            if (!Cfg.SkipRefine)
            {
                RefinePhase();
                if (_cancel) { SaveCheckpoint(); OnLog("已停止（精算阶段中断）"); return; }
            }
            Produce();
            // 产出后清精算明细（体积大、断点不需要），再存最终检查点
            foreach (var c in Pool.Snapshot()) c._details = null!;
            SaveCheckpoint(phase: Cfg.SkipRefine ? "fast" : "refine-done");   // skip-refine 只快筛 → Phase=fast（net48 resume 才会续精算）
            OnLog("搜索完成。");
        }
        catch (Exception e)
        {
            OnLog("ERROR: " + e);
            try { SaveCheckpoint(); OnLog("检查点已保存。"); } catch { }
        }
    }

    private void Prepare()
    {
        // LDB 预热（多线程只读安全）
        LDB.Theme(1);
        // 任务目录（Resume 已预置时保留）
        if (string.IsNullOrEmpty(OutDir))
        {
            OutDir = Cfg.OutDir;
            if (string.IsNullOrEmpty(OutDir))
            {
                OutDir = Path.Combine(AppContext.BaseDirectory, "search-out",
                    DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            }
        }
        Directory.CreateDirectory(OutDir);
        _sw.Start();
        _startUtc = DateTime.UtcNow;
        OnLog($"任务目录: {OutDir}");
        OnLog($"配比: {Bal.Name}");
        OnLog($"配置: seeds {Cfg.From}~{Cfg.To}, 线程 {Cfg.Threads}, O星≥{Cfg.MinOStar}, " +
              $"黑洞/中子星 近≤{Cfg.NearDist}/远≥{Cfg.FarDist}(优先), 母星系冰巨星={Cfg.NeedHomeIceGiant}, " +
              $"{Cfg.CoverageRadius}ly 覆盖(含火山灰硫酸), 气巨采集站×{SearchConfig.GAS_STATIONS}");
    }

    // ================= 快筛（全局种子队列 + 每 worker 本地批处理） =================

    /// <summary>worker 本地计数（批末合并到 Stats，避免每种子 Interlocked/锁竞争）。</summary>
    private struct SearchStatsLocal
    {
        public long Scanned, StarPass, FailOStar, PlanetPass, FailIceGiant, FailCoverage, SpecialPairOk;
    }

    private volatile bool _fastDone;   // 快筛分发完成标志（worker 据此退出，避免块间队列空时提前退）

    private void FastPhase()
    {
        const long BLOCK = 10_000;   // 块粒度：等本块消费完才发下一块 → 生产=消费、积压 ≤ BLOCK、checkpoint 水位=已消费
        _fastDone = false;
        var queue = new System.Collections.Concurrent.ConcurrentQueue<long>();
        var workers = new Task[Cfg.Threads];
        for (int w = 0; w < Cfg.Threads; w++)
            workers[w] = Task.Run(() => WorkerLoop(queue));

        long next = _startSeed;
        long lastCp = _startSeed;
        while (next < _endSeed && !_cancel)
        {
            // 块模型：等本块消费完再发下一块 → 生产=消费、积压 ≤ BLOCK、checkpoint 水位=已消费。
            // （回退自连续分发：Sleep(1) 限速后生产仍 ~66 万/s >> 消费 ~1 万/s → 队列积压千万级、
            //   checkpoint 按分发水位 10 万间隔每秒存 10 次 + GUI 轮询读同文件 → 文件锁冲突/UI 卡死，
            //   即当年 791a311b 积压 bug 复发；4f347cf 块模型实测 1.15 万/s 无损）
            for (long i = 0; i < BLOCK && next < _endSeed; i++) queue.Enqueue(next++);
            while (queue.Count > 0 && !_cancel) Thread.Sleep(20);
            if (next - lastCp >= Cfg.CheckpointEvery)
            {
                _dispatched = next;
                SaveCheckpoint(next);
                ReportProgress();
                OnTopChanged(Pool.Snapshot().Take(20).ToList());
                lastCp = next;
            }
        }
        _fastDone = true;   // 分发完成：worker 处理完剩余后退出
        _dispatched = next;
        ReportProgress();
        OnTopChanged(Pool.Snapshot().Take(20).ToList());
        SaveCheckpoint(_dispatched);
        Task.WaitAll(workers);
        OnTopChanged(Pool.Snapshot().Take(20).ToList());
        OnLog($"快筛完成: 扫描 {Stats.Scanned} 命中 {Stats.Hit} ({Stats.HitRate:P2})，top 池 {Pool.Count} 个");
    }

    private void WorkerLoop(System.Collections.Concurrent.ConcurrentQueue<long> queue)
    {
        var local = new SearchStatsLocal();
        var cands = new List<Candidate>(256);
        // 生产者连续分发（背压 52 万）→ 队列基本不空；但极端时序下 worker 可能先于生产者启动/生产完成
        // 看到空队列 → 必须等 _fastDone 才退出（否则 worker 早退 → 扫描不足，实测 0.2s 扫 0 踩到）
        while (!_fastDone || !queue.IsEmpty)
        {
            if (!queue.TryDequeue(out long seed)) { Thread.Sleep(1); continue; }
            var c = ProcessSeedLocal((int)seed, ref local);
            if (c != null) cands.Add(c);
            if (cands.Count >= 256) { Pool.AddRange(cands); cands.Clear(); }
            if (local.Scanned >= 8192) { MergeStats(local); local = new SearchStatsLocal(); }
        }
        Pool.AddRange(cands);
        MergeStats(local);
    }

    private void MergeStats(SearchStatsLocal s)
    {
        Interlocked.Add(ref Stats.Scanned, s.Scanned);
        Interlocked.Add(ref Stats.StarPass, s.StarPass);
        Interlocked.Add(ref Stats.FailOStar, s.FailOStar);
        Interlocked.Add(ref Stats.PlanetPass, s.PlanetPass);
        Interlocked.Add(ref Stats.FailIceGiant, s.FailIceGiant);
        Interlocked.Add(ref Stats.FailCoverage, s.FailCoverage);
        Interlocked.Add(ref Stats.SpecialPairOk, s.SpecialPairOk);
    }

    private Candidate? ProcessSeedLocal(int seed, ref SearchStatsLocal s)
    {
        try
        {
            var desc = MakeDesc(seed);
            var galaxy = UniverseGen.CreateGalaxyLight(desc);
            s.Scanned++;

            var sv = Filters.StarLayer(galaxy, Cfg);
            if (!sv.Pass) { s.FailOStar++; return null; }
            s.StarPass++;
            if (sv.SpecialPairOk) s.SpecialPairOk++;

            // 全量顺序生成（habitableCount 为跨行星全局累计状态，不可分）后：
            // 合一循环：组数一次计算，同时判 4.3(冰巨星)/4.4(覆盖+火山灰硫酸) 并累计理论储量
            UniverseGen.PopulatePlanets(galaxy, desc);
            var sc = TheoryScorer.ScoreAndCover(galaxy, Cfg.CoverageRadius);
            if (!sc.homeIce) { s.FailIceGiant++; return null; }
            if (sc.missing.Length > 0) { s.FailCoverage++; return null; }
            s.PlanetPass++;

            var pv = new Filters.PlanetVerdict
            {
                HomeIceGiant = true,
                IceGiantCount = sc.iceGiantCount,
                Coverage = sc.coverage,
                HasSulfuric = sc.hasSulfuric,
                Missing = sc.missing,
                PlanetsIn6Ly = sc.planetsIn6Ly,
                BirthOnIceGiant = sc.birthOnIce,
            };
            var c = BuildCandidate(seed, galaxy, sv, pv, sc.veins, sc.gases);
            c.MinOStarDist = sc.minOStarDist;
            c.HomeIceSatellites = sc.homeIceSatellites;
            return c;
        }
        catch (Exception e)
        {
            lock (_logLock) OnLog($"seed {seed} 异常: {e}");
            return null;
        }
    }

    private GameDesc MakeDesc(int seed)
    {
        // 0.1x 极难档：矿脉 ×0.7（coef>1 时 ^0.8）、气巨 gasCoef 0.8 —— 与游戏 GameDesc.isRareResource 同口径
        UniverseGen.CurrentResourceMultiplier = Cfg.ResourceMultiplier;
        return new GameDesc
        {
            galaxyAlgo = UniverseGen.algoVersion,
            galaxySeed = seed,
            starCount = Cfg.StarCount,
            savedThemeIds = null,
            resourceMultiplier = Cfg.ResourceMultiplier,   // 对拍存档时与存档 resourceMultiplier 同口径
            isRareResource = Cfg.ResourceMultiplier <= 0.1001f,
            isInfiniteResource = Cfg.ResourceMultiplier >= 99.5f,
        };
    }

    private readonly object _logLock = new();

    private Candidate BuildCandidate(int seed, GalaxyData galaxy, Filters.StarVerdict sv,
        Filters.PlanetVerdict pv, long[] veins, Dictionary<int, double> gases)
    {
        var c = new Candidate
        {
            Seed = seed,
            VeinSupply = veins,
            GasSupply = gases,
            OStarCount = sv.OStarCount,
            BlackHoleCount = sv.BlackHoleCount,
            NeutronCount = sv.NeutronCount,
            NearSpecialDist = sv.NearSpecialDist,
            FarSpecialDist = sv.FarSpecialDist,
            SpecialPairOk = sv.SpecialPairOk,
            HomeIceGiant = pv.HomeIceGiant,
            BirthOnIceGiant = pv.BirthOnIceGiant,
            IceGiantCount = pv.IceGiantCount,
            PlanetsIn6Ly = pv.PlanetsIn6Ly,
            Coverage = pv.Coverage,
            Missing = pv.Missing,
        };
        // 母星信息
        var birth = FindPlanet(galaxy, galaxy.birthPlanetId);
        if (birth != null)
        {
            c.BirthPlanetId = birth.id;
            var th = LDB.Theme(birth.theme);
            c.BirthTheme = th?.DisplayName ?? "?";
            c.BirthResourceCoef = birth.star.resourceCoef;
            c.BirthStarDist = birth.star.position.magnitude;
        }
        Score(c, veins, gases, Bal);
        return c;
    }

    /// <summary>评分：有限矿瓶颈（分钟）；气体供给仅记录（输出提示）。</summary>
    public static void Score(Candidate c, long[] veins, Dictionary<int, double> gases, Balance bal)
    {
        // 有限矿（固体矿脉，含可燃冰固体）：储量 / 需求每分钟
        double min = double.MaxValue, second = double.MaxValue;
        string bot = "", bot2 = "";
        foreach (var kv in bal.Veins)
        {
            if (kv.Value <= 0) continue;
            double minutes = veins[kv.Key] / kv.Value;
            if (minutes < min) { second = min; bot2 = bot; min = minutes; bot = VeinGen.VeinName(kv.Key); }
            else if (minutes < second) { second = minutes; bot2 = VeinGen.VeinName(kv.Key); }
        }
        c.ScoreMinutes = min >= double.MaxValue ? 0 : min;
        c.Bottleneck = bot;
        c.SecondBottleneckName = bot2;
        c.GasSupply = gases;
    }

    private static PlanetData FindPlanet(GalaxyData galaxy, int id)
    {
        foreach (var st in galaxy.stars)
            foreach (var pl in st.planets)
                if (pl.id == id) return pl;
        return null;
    }

    // ================= 精算 =================

    private void RefinePhase()
    {
        var top = Pool.Snapshot();
        if (top.Count == 0) { OnLog("快筛无命中，跳过精算。"); return; }
        int start = Math.Min(_refineDone, top.Count);
        if (start > 0) OnLog($"精算继续: 已有 {start}/{top.Count} 个精算完成，从第 {start + 1} 个继续");
        else OnLog($"精算开始: {top.Count} 个候选（全量地形+矿脉，线程 {Cfg.RefineThreads}）");
        _refineSw.Start();   // 精算阶段独立计时（断点续算从本轮起点算速率，略有偏高可接受）
        if (start == 0) ReportRefine(0, top.Count);   // 精算开始进度归零（断点续算从已有数继续）
        // 流式明细：新文件写表头；断点续算 Append（已精算候选的行已在文件里，不重复）
        var detailPath = Path.Combine(OutDir, "refined-detail.csv");
        bool detailExists = File.Exists(detailPath);
        _detailWriter = new StreamWriter(detailPath, append: detailExists, new UTF8Encoding(false));
        _detailStreaming = true;
        if (!detailExists) _detailWriter.WriteLine(DetailHeader());
        var result = new Candidate[top.Count];
        try
        {
            // 断点恢复：前 start 个候选的精算值已在 Pool（回写），此处只精算剩余部分
            Parallel.For(start, top.Count, new ParallelOptions { MaxDegreeOfParallelism = Cfg.RefineThreads }, i =>
            {
                var c = top[i];
                var refined = RefineOne(c);
                lock (_logLock)
                {
                    result[i] = refined;
                    if (refined != null) Pool.Update(refined);   // 精算值回写 Pool（断点/展示用）
                    _refineDone++;
                    if (refined != null) { WriteRefinedCandidate(refined); refined._details = null; }   // 流式追加后释放明细（已落盘；常驻内存与池大小解耦，防 1 万池 OOM）
                    ReportRefine(_refineDone, top.Count);
                    OnTopChanged(Pool.Snapshot().Take(20).ToList());   // 精算中每完成 1 个候选刷一次榜单（秒级）
                    if (_refineDone % RefineCheckpointEvery == 0)
                        SaveCheckpoint(phase: "refine");   // 定期断点：精算中途强杀最多丢 25 个
                }
            });
        }
        finally
        {
            _detailWriter?.Dispose();   // 关流（异常/中断也释放句柄）
            _detailWriter = null;
        }
        // 精算完成：Pool 已逐候选回写精算值（断点恢复时前段来自 checkpoint），无需 ReplaceAll
        OnTopChanged(Pool.Snapshot().Take(20).ToList());
        OnLog($"精算完成，最终 top 池 {Pool.Count} 个");
    }

    private Candidate RefineOne(Candidate orig)
    {
        try
        {
            int seed = orig.Seed;
            var desc = MakeDesc(seed);
            var galaxy = UniverseGen.CreateGalaxy(desc);
            var veins = new long[15];
            var gases = new Dictionary<int, double>();
            var sixLyRare = new long[15];
            var details = new List<(int starId, string starName, double starDist, int starType, int starSpectr, int planetId,
                string planetName, string theme, bool isGas, int orbitAround, bool in6ly, int[] cnts, long[] amts)>();

            foreach (var st in galaxy.stars)
            {
                bool in6 = st.position.magnitude <= Cfg.CoverageRadius;
                foreach (var pl in st.planets)
                {
                    if (pl.type == EPlanetType.Gas)
                    {
                        for (int i = 0; i < pl.gasItems.Length && i < pl.gasSpeeds.Length; i++)
                        {
                            int item = pl.gasItems[i];
                            double rate = pl.gasSpeeds[i] * SearchConfig.GAS_STATIONS * 60.0;
                            gases.TryGetValue(item, out double cur);
                            gases[item] = cur + rate;
                        }
                        details.Add((st.id, st.name, st.position.magnitude, (int)st.type, (int)st.spectr, pl.id,
                            pl.name, LDB.Theme(pl.theme)?.DisplayName ?? "?", true, pl.orbitAround, in6, null!, null!));
                        continue;
                    }
                    PlanetModelingManager.ModelingPlanet(pl);
                    var (cnts, amts) = VeinGen.GenerateVeins(pl);
                    for (int t = 1; t <= 14; t++)
                    {
                        veins[t] += amts[t];
                        if (in6 && t >= 9) sixLyRare[t] += amts[t];   // 6ly 稀有矿(9~14)储量，均衡度/丰富度用
                    }
                    details.Add((st.id, st.name, st.position.magnitude, (int)st.type, (int)st.spectr, pl.id,
                        pl.name, LDB.Theme(pl.theme)?.DisplayName ?? "?", false, pl.orbitAround, in6, cnts, amts));
                    pl.data = null;   // 释放建模数据（PlanetRawData 16 万顶点/矿脉池），防 x86 OOM：全星系保留会达数 GB（2026-10-21）
                }
            }
            var c = new Candidate
            {
                Seed = seed,
                VeinSupply = veins,
                GasSupply = gases,
                Refined = true,
                SixLyTotal = sixLyRare.Sum(x => x),
                SixLyRare = sixLyRare,
                // 结构字段沿用快筛值（同种子生成相同，无需精算重算）
                HomeIceSatellites = orig.HomeIceSatellites,
                MinOStarDist = orig.MinOStarDist,
                // 拷贝快筛过滤信息（展示用）
                OStarCount = orig.OStarCount,
                BlackHoleCount = orig.BlackHoleCount,
                NeutronCount = orig.NeutronCount,
                NearSpecialDist = orig.NearSpecialDist,
                FarSpecialDist = orig.FarSpecialDist,
                SpecialPairOk = orig.SpecialPairOk,
                HomeIceGiant = orig.HomeIceGiant,
                BirthOnIceGiant = orig.BirthOnIceGiant,
                IceGiantCount = orig.IceGiantCount,
                PlanetsIn6Ly = orig.PlanetsIn6Ly,
                Coverage = orig.Coverage,
                Missing = orig.Missing,
                BirthPlanetId = orig.BirthPlanetId,
                BirthTheme = orig.BirthTheme,
                BirthResourceCoef = orig.BirthResourceCoef,
                BirthStarDist = orig.BirthStarDist,
            };
            Score(c, veins, gases, Bal);
            c._details = details;
            return c;
        }
        catch (Exception e)
        {
            lock (_logLock) OnLog($"精算 seed {orig.Seed} 异常: {e.Message}");
            return null;
        }
    }

    /// <summary>单种子精算：跳过过滤，完整生成 + 全量地形 + 精确矿脉 → 单候选 results.csv/refined-detail.csv。</summary>
    private void SingleSeed()
    {
        int seed = Cfg.GalaxySeed;
        OnLog($"单种子精算开始: seed={seed}");
        var desc = MakeDesc(seed);
        var galaxy = UniverseGen.CreateGalaxyLight(desc);
        var sv = Filters.StarLayer(galaxy, Cfg);   // 星表统计（不过滤）
        UniverseGen.PopulatePlanets(galaxy, desc);
        var sc = TheoryScorer.ScoreAndCover(galaxy, Cfg.CoverageRadius);
        var pv = new Filters.PlanetVerdict
        {
            HomeIceGiant = sc.homeIce,
            IceGiantCount = sc.iceGiantCount,
            Coverage = sc.coverage,
            HasSulfuric = sc.hasSulfuric,
            Missing = sc.missing,
            PlanetsIn6Ly = sc.planetsIn6Ly,
            BirthOnIceGiant = sc.birthOnIce,
        };
        var cand = BuildCandidate(seed, galaxy, sv, pv, sc.veins, sc.gases);
        cand.MinOStarDist = sc.minOStarDist;
        cand.HomeIceSatellites = sc.homeIceSatellites;
        var refined = RefineOne(cand);
        if (refined == null) { OnLog("精算失败"); return; }
        Pool.Add(refined);
        Produce();
        OnLog($"单种子精算完成: {Path.Combine(OutDir, "results.csv")}");
    }

    // ================= 产出（精算明细 → 分析 → results.csv） =================

    private void Produce()
    {
        var pool = Pool.Snapshot();
        Analyze6Ly(pool);
        if (!_detailStreaming) WriteRefinedDetail(pool);   // 搜索精算已流式实时写明细，跳过全量重写（防池大内存峰值）
        var res = new SearchResult { Top20 = pool.Take(20).ToList(), OutDir = OutDir };
        WriteResultsCsv(pool);
        WriteReport(pool);
        OnDone(res);
        OnTopChanged(pool.Take(20).ToList());
        OnLog($"产出完成: results.csv（{pool.Count} 个种子）+ refined-detail.csv + search-report.txt");
    }

    /// <summary>分析：6ly 丰富度（稀有矿总储量三等分）+ 6ly 稀有矿均衡度（min 储量/需求，与支撑同口径）。</summary>
    private void Analyze6Ly(List<Candidate> pool)
    {
        var sorted = pool.OrderBy(c => c.SixLyTotal).ToList();
        int n = sorted.Count;
        long low = n > 0 ? sorted[Math.Max(0, n / 3 - 1)].SixLyTotal : 0;
        long high = n > 2 ? sorted[Math.Max(0, 2 * n / 3 - 1)].SixLyTotal : low;
        foreach (var c in pool)
        {
            // 单种子（n<3）无法三等分，丰富度显示 —
            c.RichLevel = n < 3 ? "—" : (c.SixLyTotal <= low ? "低" : c.SixLyTotal <= high ? "中" : "高");
            double min = double.MaxValue;
            for (int t = 9; t <= 14; t++)
            {
                if (!Bal.HasVeinDemand(t)) continue;
                double m = c.SixLyRare[t] / Bal.Veins[t];
                if (m < min) min = m;
            }
            c.SixLyBalance = min >= double.MaxValue ? 0 : min;
        }
        OnLog($"6ly 丰富度分档完成（稀有矿总储量口径：低≤{low / 1e8:F1}亿 / 高≥{high / 1e8:F1}亿）");
    }

    /// <summary>精算明细（程序读/重新分析用）：行星级，含恒星冗余字段。
    /// 搜索精算走流式（每候选完成即追加，见 RefinePhase）；本方法供单种子/非流式场景全量写。</summary>
    private void WriteRefinedDetail(List<Candidate> pool)
    {
        var sb = new StringBuilder();
        sb.AppendLine(DetailHeader());
        foreach (var c in pool)
        {
            var sw = new StringWriter(sb);
            WriteRefinedRows(c, sw);
        }
        File.WriteAllText(Path.Combine(OutDir, "refined-detail.csv"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>明细表头（种子 + 恒星 + 行星 + 14 矿 组/量）。</summary>
    private static string DetailHeader()
    {
        var hdr = new List<string> { "种子", "恒星id", "恒星名", "恒星距离", "恒星类型", "恒星光谱", "行星id", "行星名", "主题", "气巨", "卫星母", "6ly内" };
        for (int t = 1; t <= 14; t++) { hdr.Add(VeinGen.VeinName(t) + "组"); hdr.Add(VeinGen.VeinName(t) + "量"); }
        return string.Join(",", hdr);
    }

    /// <summary>单候选的行写入（调用方保证 _detailWriter 已开且线程安全）。</summary>
    private void WriteRefinedCandidate(Candidate c) => WriteRefinedRows(c, _detailWriter);

    private static void WriteRefinedRows(Candidate c, TextWriter w)
    {
        foreach (var d in c._details ?? new List<(int starId, string starName, double starDist, int starType, int starSpectr, int planetId,
            string planetName, string theme, bool isGas, int orbitAround, bool in6ly, int[] cnts, long[] amts)>())
        {
            var row = new List<string> { c.Seed.ToString(), d.starId.ToString(), d.starName, d.starDist.ToString("F2"),
                ((EStarType)d.starType).ToString(), ((ESpectrType)d.starSpectr).ToString(),
                d.planetId.ToString(), d.planetName, d.theme, d.isGas ? "1" : "0", d.orbitAround.ToString(), d.in6ly ? "1" : "0" };
            for (int t = 1; t <= 14; t++)
            {
                row.Add((d.cnts != null && d.cnts[t] > 0) ? d.cnts[t].ToString() : "0");
                row.Add((d.amts != null && d.amts[t] > 0) ? d.amts[t].ToString() : "0");
            }
            w.WriteLine(string.Join(",", row));
        }
    }

    /// <summary>最终结果（人读）：池内全部种子按精算支撑排序，易读单位（亿/小时），含分析列；矿物列置于末尾。</summary>
    private void WriteResultsCsv(List<Candidate> pool)
    {
        var sb = new StringBuilder();
        var hdr = new List<string>
        {
            "排名", "种子", "支撑小时", "瓶颈", "第二瓶颈",
            "6ly丰富度", "6ly均衡(h)", "冰巨卫星数", "最近O星(ly)", "黑洞/中子星(ly)", "4.2达标", "O星数", "6ly行星数",
            "氢/min", "重氢/min", "可燃冰气/min",
        };
        for (int t = 1; t <= 14; t++) hdr.Add(VeinGen.VeinName(t) + "(亿)");
        sb.AppendLine(string.Join(",", hdr));
        var sorted = pool.OrderByDescending(c => c.ScoreMinutes).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var c = sorted[i];
            var row = new List<string>
            {
                (i + 1).ToString(), c.Seed.ToString(), (c.ScoreMinutes / 60.0).ToString("F1"),
                c.Bottleneck, c.SecondBottleneckName,
                c.RichLevel, (c.SixLyBalance / 60.0).ToString("F1"), c.HomeIceSatellites.ToString(), c.MinOStarDist.ToString("F1"),
                $"{c.NearSpecialDist:F1}~{c.FarSpecialDist:F1}", c.SpecialPairOk ? "1" : "0", c.OStarCount.ToString(), c.PlanetsIn6Ly.ToString(),
                GasVal(c.GasSupply, 1120).ToString("F0"), GasVal(c.GasSupply, 1121).ToString("F0"), GasVal(c.GasSupply, 1011).ToString("F0"),
            };
            for (int t = 1; t <= 14; t++) row.Add((c.VeinSupply[t] / 1e8).ToString("F2"));
            sb.AppendLine(string.Join(",", row));
        }
        File.WriteAllText(Path.Combine(OutDir, "results.csv"), sb.ToString(), Encoding.UTF8);
    }

    private static string JoinVeins(long[] v) => string.Join(",", Enumerable.Range(1, 14).Select(i => v[i].ToString()));
    private static double GasVal(Dictionary<int, double> g, int item) => g.TryGetValue(item, out double v) ? v : 0;

    private void WriteReport(List<Candidate> top)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===== DSP 地图搜索器报告 =====");
        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"配比: {Bal.Name}");
        sb.AppendLine($"配置: seeds {Cfg.From}~{Cfg.To}, 线程 {Cfg.Threads}, O星≥{Cfg.MinOStar}, " +
                      $"黑洞/中子星 近≤{Cfg.NearDist}/远≥{Cfg.FarDist}(优先), 母星系冰巨星={Cfg.NeedHomeIceGiant}, " +
                      $"{Cfg.CoverageRadius}ly 覆盖(含火山灰硫酸), 气巨采集站×{SearchConfig.GAS_STATIONS}, 快筛top池={Cfg.PoolSize}");
        sb.AppendLine($"总耗时: {(_sw.Elapsed + _bias).TotalMinutes:F1} 分钟");
        sb.AppendLine($"快筛: 扫描 {Stats.Scanned}, O星淘汰 {Stats.FailOStar}, 冰巨星淘汰 {Stats.FailIceGiant}, " +
                      $"覆盖淘汰 {Stats.FailCoverage}, 命中 {Stats.Hit} ({Stats.HitRate:P3})");
        sb.AppendLine($"4.2 达标(优先): {Stats.SpecialPairOk}");
        sb.AppendLine();
        sb.AppendLine("===== TOP20 =====");
        var top20 = top.Take(20).ToList();
        for (int i = 0; i < top20.Count; i++)
        {
            var c = top20[i];
            sb.AppendLine($"{i + 1,2}. seed={c.Seed}  支撑 {c.ScoreMinutes / 60.0:F1} 小时 (瓶颈 {c.Bottleneck})  " +
                          $"O星{c.OStarCount} 黑洞{c.BlackHoleCount}/中子{c.NeutronCount} 距离{c.NearSpecialDist:F1}~{c.FarSpecialDist:F1}ly  " +
                          $"母星[{c.BirthTheme}]×{c.BirthResourceCoef:F2}{(c.BirthOnIceGiant ? " 冰巨星卫星" : "")}");
        }
        sb.AppendLine();
        sb.AppendLine("===== 气巨气体供给（每分钟，需>供给部分需分馏/采集补充）=====");
        if (top.Count > 0)
        {
            var c = top[0];
            sb.AppendLine($"氢 {GasVal(c.GasSupply, 1120):F0} / 需求 {Bal.Gas(1120):F0}");
            sb.AppendLine($"重氢 {GasVal(c.GasSupply, 1121):F0} / 需求 {Bal.Gas(1121):F0}");
        }
        File.WriteAllText(Path.Combine(OutDir, "search-report.txt"), sb.ToString(), Encoding.UTF8);
    }

    // ================= 进度 =================

    private void ReportProgress()
    {
        // 进度条用分发水位 _dispatched（块模型下生产=消费，差 ≤ BLOCK ≈1 万，平滑推进）；
        // 速率用 Stats.Scanned（已消费，真实不虚高——连续分发下 _dispatched 瞬时超前会虚高百万级，已回退块模型）。
        // Stats.Scanned 每 8192 合并一次，前 26 万种子基本为 0 → 纯用它进度条不动。
        // Total = 任务原始总范围（恢复后 Scanned 为累计 → 进度条显示整体进度）。
        long scanned = _dispatched > _startSeed ? Math.Max(Stats.Scanned, _dispatched) : Stats.Scanned;
        double secs = (_sw.Elapsed + _bias).TotalSeconds;
        double rate = secs > 0 ? scanned / secs : 0;
        long remain = Math.Max(0, _endSeed - scanned);
        string eta = rate > 0 ? TimeSpan.FromSeconds(remain / rate).ToString(@"hh\:mm\:ss") : "-";
        OnProgress(new ProgressInfo
        {
            Phase = "fast",
            Scanned = scanned,
            Total = Cfg.To - Cfg.From,
            Hit = Stats.Hit,
            RatePerSec = rate,
            Eta = eta,
        });
    }

    private void ReportRefine(int done, int total)
    {
        double secs = _refineSw.Elapsed.TotalSeconds;
        double rate = secs > 0 ? done / secs : 0;
        long remain = Math.Max(0, total - done);
        string eta = rate > 0 ? TimeSpan.FromSeconds(remain / rate).ToString(@"hh\:mm\:ss") : "-";
        OnProgress(new ProgressInfo { Phase = "refine", RefineDone = done, RefineTotal = total, RatePerSec = rate, Eta = eta });
    }

    private void SaveCheckpoint(long? nextSeed = null, string phase = null)
    {
        lock (_logLock)
        {
            var cp = new Checkpoint
            {
                Phase = phase ?? (_cancel ? "stopped" : "fast"),
                NextSeed = nextSeed ?? _dispatched,
                SeedTo = _endSeed,
                RefineDone = _refineDone,
                Stats = Stats,
                Pool = Pool.Snapshot(),
                Config = Cfg,
                StartUtc = _startUtc,
                ElapsedSec = (long)(_sw.Elapsed + _bias).TotalSeconds,
            };
            cp.Save(Path.Combine(OutDir, "search-checkpoint.json"));
            OnLog($"检查点已保存: 已扫 {_dispatched}, top 池 {Pool.Count}, 累计 {(_sw.Elapsed + _bias).TotalMinutes:F1} 分钟");
        }
    }

    /// <summary>从检查点恢复（新 Searcher 实例，填充池与统计）。</summary>
    public static Searcher Resume(string checkpointPath)
    {
        var cp = Checkpoint.Load(checkpointPath);
        var searcher = new Searcher(cp.Config);
        searcher.Cfg.SkipRefine = false;   // resume 目的 = 续算精算（net8 快筛的 checkpoint 会固化 SkipRefine=true，resume 时强制打开精算）
        // 恢复位置用 _startSeed/_endSeed（不再改写 Cfg → checkpoint 的 Config 保持原始配置，防脏数据）
        searcher._startSeed = cp.NextSeed;
        searcher._endSeed = cp.SeedTo;
        searcher.OutDir = Path.GetDirectoryName(checkpointPath) ?? "";
        searcher.Stats = cp.Stats;
        searcher.Pool.ReplaceAll(cp.Pool);
        searcher._startUtc = cp.StartUtc;
        searcher._bias = TimeSpan.FromSeconds(cp.ElapsedSec);
        searcher._dispatched = cp.NextSeed;
        searcher._refineDone = cp.RefineDone;
        searcher._resumePhaseDone = cp.Phase == "refine-done";
        searcher._sw.Restart();
        return searcher;
    }
}

// (Candidate._details 已移入 Candidate.cs)

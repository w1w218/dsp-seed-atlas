using Google.OrTools.Sat;

namespace DspPlan;

/// <summary>C5：CP-SAT 全规模精确求解（位置变量 + 距离表 Element + 流量线性化）。</summary>
sealed class CpSatModel
{
    const int FSCALE = 1000;    // 流量整数化倍数
    const int DSCALE = 1000;    // 距离整数化倍数

    public static (string status, double objective, double bound, int[] layout, double seconds) Solve(
        CostModel model, List<Slot> slots, List<Candidate> pool, double timeLimitSec, int threads,
        Action<string> log = null, int[] hintLayout = null, Dictionary<int, int> fixedSlots = null)
    {
        int ns = slots.Count, n = pool.Count;
        var mdl = new CpModel();

        // ---------- x[s][p] ----------
        var x = new BoolVar[ns][];
        for (int s = 0; s < ns; s++)
        {
            x[s] = new BoolVar[n];
            for (int p = 0; p < n; p++) x[s][p] = mdl.NewBoolVar($"x_{s}_{p}");
        }
        for (int s = 0; s < ns; s++) mdl.Add(LinearExpr.Sum(x[s]) == 1);
        for (int p = 0; p < n; p++)
        {
            var terms = new List<LinearExpr>();
            for (int s = 0; s < ns; s++) terms.Add(x[s][p]);
            mdl.Add(LinearExpr.Sum(terms) <= 1);
        }
        for (int p = 0; p < n; p++)
            if (pool[p].IsBirth)
                for (int s = 0; s < ns; s++) mdl.Add(x[s][p] == 0);
        var groupCount = slots.Select(sl => Slots.Groups.First(g => g.Name == sl.Group).Count).ToArray();
        for (int s = 0; s < ns; s++)
            if (groupCount[s] <= 1)
                for (int p = 0; p < n; p++)
                    if (pool[p].LowOpen) mdl.Add(x[s][p] == 0);
        // 固定槽（部分锁定，如 C 的小规模验证）
        if (fixedSlots != null)
            foreach (var (s, p) in fixedSlots)
                mdl.Add(x[s][p] == 1);

        // ---------- pos[s] ----------
        var pos = new IntVar[ns];
        for (int s = 0; s < ns; s++)
        {
            var terms = new List<LinearExpr>();
            for (int p = 0; p < n; p++) terms.Add(LinearExpr.Term(x[s][p], p));
            pos[s] = mdl.NewIntVar(0, n - 1, $"pos_{s}");
            mdl.Add(pos[s] == LinearExpr.Sum(terms));
        }

        // ---------- 距离表 ----------
        var dflat = new long[n * n];
        for (int p = 0; p < n; p++)
            for (int q = 0; q < n; q++)
                dflat[p * n + q] = (long)Math.Round(model.Dist[p, q] * DSCALE);
        long dUb = dflat.Max();
        var gasFlat = new long[n];
        for (int p = 0; p < n; p++) gasFlat[p] = (long)Math.Round(model.ToNearestGas[p] * DSCALE);
        int birthIdx = Enumerable.Range(0, n).First(p => pool[p].IsBirth);

        var costTerms = new List<LinearExpr>();
        // 源槽出流收集：item → 槽 → 变量列表
        var outFlows = new Dictionary<int, Dictionary<int, List<IntVar>>>();
        void AddOut(int item, int slot, IntVar v)
        {
            if (!outFlows.TryGetValue(item, out var m)) outFlows[item] = m = new Dictionary<int, List<IntVar>>();
            if (!m.TryGetValue(slot, out var l)) m[slot] = l = new List<IntVar>();
            l.Add(v);
        }

        IntVar Dist(int a, int b, string name)
        {
            var idx = mdl.NewIntVar(0, n * n - 1, "idx_" + name);
            mdl.Add(idx == LinearExpr.Term(pos[a], n) + pos[b]);
            var d = mdl.NewIntVar(0, dUb, "d_" + name);
            mdl.AddElement(idx, dflat, d);
            return d;
        }

        // ---------- 1) 中间品：源槽 → 净缺槽 ----------
        foreach (var (item, srcList) in model.Sources)
        {
            if (!model.Sinks.TryGetValue(item, out var sinkList) || sinkList.Count == 0) continue;
            var sup = new long[srcList.Count];
            for (int i = 0; i < srcList.Count; i++) sup[i] = (long)Math.Round(srcList[i].amt * FSCALE);
            var dem = new long[sinkList.Count];
            for (int j = 0; j < sinkList.Count; j++) dem[j] = (long)Math.Round(sinkList[j].amt * FSCALE);

            var f = new IntVar[srcList.Count, sinkList.Count];
            for (int i = 0; i < srcList.Count; i++)
                for (int j = 0; j < sinkList.Count; j++)
                {
                    f[i, j] = mdl.NewIntVar(0, Math.Min(sup[i], dem[j]), $"f_{item}_{i}_{j}");
                    // 成本（汇 slot=-1 表示母星）
                    var d = Dist(srcList[i].slot, sinkList[j].slot == -1 ? birthIdx : sinkList[j].slot, $"{item}_{i}_{j}");
                    var prod = mdl.NewIntVar(0, Math.Min(sup[i], dem[j]) * dUb, $"c_{item}_{i}_{j}");
                    mdl.AddMultiplicationEquality(prod, new[] { f[i, j], d });
                    costTerms.Add(prod);
                    AddOut(item, srcList[i].slot, f[i, j]);
                }
            // 汇约束：每个净缺槽的入流 = 其净缺量
            for (int j = 0; j < sinkList.Count; j++)
            {
                var terms = new List<LinearExpr>();
                for (int i = 0; i < srcList.Count; i++) terms.Add(f[i, j]);
                mdl.Add(LinearExpr.Sum(terms) == dem[j]);
            }
        }

        // ---------- 2) 母星已并入 Sinks（slot = -1），无需单独建模 ----------

        // ---------- 3) 源槽出流 ≤ 净余 ----------
        foreach (var (item, bySrc) in outFlows)
            foreach (var (slot, vars) in bySrc)
            {
                double sup = model.Sources[item].First(z => z.slot == slot).amt;
                var terms = new List<LinearExpr>();
                foreach (var v in vars) terms.Add(v);
                mdl.Add(LinearExpr.Sum(terms) <= (long)Math.Round(sup * FSCALE));
            }

        // ---------- 4) 氢：耗氢槽 → 最近气巨（偏好项） ----------
        foreach (var (slot, amt) in model.HydrogenBySlot)
        {
            long scaled = (long)Math.Round(amt * model.HydrogenWeight * FSCALE);
            if (scaled <= 0) continue;
            var d = mdl.NewIntVar(0, gasFlat.Max(), $"gd_{slot}");
            mdl.AddElement(pos[slot], gasFlat, d);
            var prod = mdl.NewIntVar(0, scaled * gasFlat.Max(), $"gc_{slot}");
            mdl.AddMultiplicationEquality(prod, new[] { mdl.NewConstant(scaled), d });
            costTerms.Add(prod);
        }

        mdl.Minimize(LinearExpr.Sum(costTerms));

        // hint：用退火最优布局作初始提示（位置 = pool 下标）
        if (hintLayout != null)
            for (int s = 0; s < ns; s++)
                mdl.AddHint(pos[s], hintLayout[s]);

        // ---------- 求解 ----------
        string validation = mdl.Validate();
        if (!string.IsNullOrEmpty(validation))
        {
            log?.Invoke("模型校验失败: " + validation);
            return ("ModelInvalid", double.NaN, 0, null, 0);
        }
        var solver = new CpSolver();
        solver.StringParameters =
            $"max_time_in_seconds:{timeLimitSec};num_search_workers:{threads}";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var status = solver.Solve(mdl);
        sw.Stop();

        bool ok = status == CpSolverStatus.Feasible || status == CpSolverStatus.Optimal;
        double obj = ok ? solver.ObjectiveValue / (FSCALE * (double)DSCALE) : double.NaN;
        double bound = solver.BestObjectiveBound / (FSCALE * (double)DSCALE);
        int[] layout = null;
        if (ok)
        {
            layout = new int[ns];
            for (int s = 0; s < ns; s++) layout[s] = (int)solver.Value(pos[s]);
        }
        return (status.ToString(), obj, bound, layout, sw.Elapsed.TotalSeconds);
    }
}

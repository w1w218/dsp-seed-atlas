using Google.OrTools.Sat;

namespace DspPlan;

/// <summary>B：用 CP-SAT（纯线性）精确求解运输问题，校验评估器贪心（Transport.Solve）的误差。</summary>
static class ExactTransport
{
    const long SCALE = 1000;

    /// <summary>精确最小费用分配。sources/sinks 为 (候选池下标, 数量)；返回总成本。</summary>
    public static double Solve(List<(int idx, double amt)> sources, List<(int idx, double amt)> sinks, double[,] dist)
    {
        int ns = sources.Count, nt = sinks.Count;
        if (ns == 0 || nt == 0) return 0;

        var sup = sources.Select(s => (long)Math.Round(s.amt * SCALE)).ToArray();
        var dem = sinks.Select(s => (long)Math.Round(s.amt * SCALE)).ToArray();

        var mdl = new CpModel();
        var f = new IntVar[ns, nt];
        for (int i = 0; i < ns; i++)
            for (int j = 0; j < nt; j++)
                f[i, j] = mdl.NewIntVar(0, Math.Min(sup[i], dem[j]), $"f_{i}_{j}");

        // 源：出流 ≤ 供给（多余供给 = 顶级消费，不强制运完）
        for (int i = 0; i < ns; i++)
        {
            var terms = new List<LinearExpr>();
            for (int j = 0; j < nt; j++) terms.Add(f[i, j]);
            mdl.Add(LinearExpr.Sum(terms) <= sup[i]);
        }
        // 汇：入流 = 需求
        for (int j = 0; j < nt; j++)
        {
            var terms = new List<LinearExpr>();
            for (int i = 0; i < ns; i++) terms.Add(f[i, j]);
            mdl.Add(LinearExpr.Sum(terms) == dem[j]);
        }

        var cost = new List<LinearExpr>();
        for (int i = 0; i < ns; i++)
            for (int j = 0; j < nt; j++)
                cost.Add(LinearExpr.Term(f[i, j], (long)Math.Round(dist[sources[i].idx, sinks[j].idx] * SCALE)));
        mdl.Minimize(LinearExpr.Sum(cost));

        var solver = new CpSolver();
        solver.StringParameters = "max_time_in_seconds:10;num_search_workers:1";
        var st = solver.Solve(mdl);
        if (st != CpSolverStatus.Optimal && st != CpSolverStatus.Feasible) return double.NaN;
        return solver.ObjectiveValue / (double)(SCALE * SCALE);
    }
}

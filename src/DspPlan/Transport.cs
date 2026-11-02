namespace DspPlan;

/// <summary>内层运输问题：多源→多汇，最小化 Σ 流量 × 距离。规模 ≤3 源 × ≤20 汇。</summary>
static class Transport
{
    /// <summary>最小费用分配（最小费用优先贪心 + 2×2 交换改进）。</summary>
    public static double Solve(List<(int idx, double amt)> sources, List<(int idx, double amt)> sinks, double[,] dist)
        => SolveDetailed(sources, sinks, dist).cost;

    /// <summary>返回成本 + 分配矩阵（供报告明细）。</summary>
    public static (double cost, double[,] f) SolveDetailed(List<(int idx, double amt)> sources, List<(int idx, double amt)> sinks, double[,] dist)
    {
        int ns = sources.Count, nt = sinks.Count;
        if (ns == 0 || nt == 0) return (0, new double[0, 0]);

        var sup = new double[ns];
        var dem = new double[nt];
        for (int i = 0; i < ns; i++) sup[i] = sources[i].amt;
        for (int j = 0; j < nt; j++) dem[j] = sinks[j].amt;

        // 贪心：所有 (源,汇) 对按距离升序
        var pairs = new List<(double d, int i, int j)>(ns * nt);
        for (int i = 0; i < ns; i++)
            for (int j = 0; j < nt; j++)
                pairs.Add((dist[sources[i].idx, sinks[j].idx], i, j));
        pairs.Sort((a, b) => a.d.CompareTo(b.d));

        var f = new double[ns, nt];
        double cost = 0;
        foreach (var (d, i, j) in pairs)
        {
            if (sup[i] <= 1e-9 || dem[j] <= 1e-9) continue;
            double q = Math.Min(sup[i], dem[j]);
            f[i, j] += q;
            cost += q * d;
            sup[i] -= q;
            dem[j] -= q;
        }

        // 2×2 交换改进（保持源总出流/汇总入流不变）
        for (int pass = 0; pass < 4; pass++)
        {
            bool improved = false;
            for (int j1 = 0; j1 < nt; j1++)
                for (int j2 = j1 + 1; j2 < nt; j2++)
                    for (int i1 = 0; i1 < ns; i1++)
                    {
                        if (f[i1, j1] <= 1e-9) continue;
                        for (int i2 = 0; i2 < ns; i2++)
                        {
                            if (i1 == i2 || f[i2, j2] <= 1e-9) continue;
                            double q = Math.Min(f[i1, j1], f[i2, j2]);
                            double oldC = q * (dist[sources[i1].idx, sinks[j1].idx] + dist[sources[i2].idx, sinks[j2].idx]);
                            double newC = q * (dist[sources[i1].idx, sinks[j2].idx] + dist[sources[i2].idx, sinks[j1].idx]);
                            if (newC < oldC - 1e-9)
                            {
                                f[i1, j1] -= q; f[i2, j2] -= q;
                                f[i1, j2] += q; f[i2, j1] += q;
                                cost += newC - oldC;
                                improved = true;
                            }
                        }
                    }
            if (!improved) break;
        }
        return (cost, f);
    }

    /// <summary>单源特例：Σ 流量 × 距离。</summary>
    public static double SumFlow(IEnumerable<(int idx, double amt)> flows, Func<int, double> distOf)
    {
        double s = 0;
        foreach (var (idx, amt) in flows) s += amt * distOf(idx);
        return s;
    }
}

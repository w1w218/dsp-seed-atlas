using System;
using System.Collections.Generic;
using UnityEngine;

namespace DspCore
{
    /// <summary>
    /// 光滑球面空间估算（粗估，性能优先）：存档矿组方向（VeinGroup.pos 权威真值）
    /// → 矿组聚类（相邻 3km 归簇）→ 每簇画禁建圈 → 低分辨率等经纬网格泛洪
    /// → 可建比例 → **1~10 档**（10 = 无矿/最空旷）。
    /// 仅用于跨行星初始可建设空间对比；水/坑可平整，不参与；地形不落盘，用光滑球面。
    /// </summary>
    public static class SpaceAnalyzer
    {
        public struct SpaceResult
        {
            public int Level;          // 1~10，10=无矿/最空旷
            public double BuildableRatio;   // 可建比例（内部参考）
        }

        // 网格：0.5°/格（行星半径 240km ≈ 2km/格；估算用，足够）
        public const int LON = 720;
        public const int LAT = 360;
        public const double ClusterDistance = 3000.0;   // 矿组聚类距离（m）
        public const double Margin = 500.0;             // 簇圈边距（m）

        /// <param name="veinDirs">矿组方向向量列表（存档 VeinGroup.pos）</param>
        /// <param name="planetRadius">行星物理半径（m = planet.radius × 1200）</param>
        public static SpaceResult Analyze(IReadOnlyList<Vector3> veinDirs, double planetRadius)
        {
            var result = new SpaceResult { Level = 10, BuildableRatio = 1.0 };
            if (planetRadius <= 0 || veinDirs.Count == 0) return result;

            // 1) 矿组聚类（贪心：与任一簇内成员距离 < ClusterDistance 归簇）
            var clusterCenters = new List<Vector3>();
            var clusterRadius = new List<double>();
            for (int i = 0; i < veinDirs.Count; i++)
            {
                Vector3 v = veinDirs[i].normalized;
                int best = -1; double bestD = double.MaxValue;
                for (int c = 0; c < clusterCenters.Count; c++)
                {
                    double d = 1.0 - Vector3.Dot(v, clusterCenters[c]);   // 距离平方近似（角距离）
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (best >= 0 && bestD * planetRadius < ClusterDistance)
                {
                    // 并入簇：更新中心（均值方向）与半径
                    Vector3 nc = (clusterCenters[best] * 3f + v).normalized;   // 近似均值
                    clusterCenters[best] = nc;
                    double r = Math.Acos(Math.Max(-1, Math.Min(1, Vector3.Dot(nc, v)))) * planetRadius;
                    if (r > clusterRadius[best]) clusterRadius[best] = r;
                }
                else
                {
                    clusterCenters.Add(v);
                    clusterRadius.Add(0);
                }
            }

            // 2) 低分辨率网格标记禁建圈（簇圈 = 簇半径 + 边距）
            bool[] blocked = new bool[LON * LAT];
            double totalW = 0;
            double[] rowWeight = new double[LAT];
            for (int la = 0; la < LAT; la++)
            {
                double la0 = (la / (double)(LAT - 1) - 0.5) * Math.PI;
                double la1 = ((la + 1) / (double)(LAT - 1) - 0.5) * Math.PI;
                rowWeight[la] = Math.Abs(Math.Sin(la1) - Math.Sin(la0));
                totalW += rowWeight[la];
            }
            for (int c = 0; c < clusterCenters.Count; c++)
            {
                Vector3 cv = clusterCenters[c].normalized;
                double r = clusterRadius[c] + Margin;
                double delta = r / planetRadius;
                if (delta > Math.PI) delta = Math.PI - 1e-6;
                int di = (int)(delta / Math.PI * (LAT - 1)) + 1;
                double lat = Math.Asin(Math.Max(-1.0, Math.Min(1.0, cv.y)));
                double lon = Math.Atan2(cv.z, cv.x);
                int latC = (int)Math.Round((lat + Math.PI / 2) / Math.PI * (LAT - 1));
                latC = Math.Max(0, Math.Min(LAT - 1, latC));
                int lonC = (int)Math.Round((lon + Math.PI) / (2 * Math.PI) * LON) % LON;
                double cosD = Math.Cos(delta);
                for (int la = Math.Max(0, latC - di); la <= Math.Min(LAT - 1, latC + di); la++)
                {
                    double laA = (la / (double)(LAT - 1) - 0.5) * Math.PI;
                    double sinLa = Math.Sin(laA), cosLa = Math.Cos(laA);
                    for (int dl = -di; dl <= di; dl++)
                    {
                        int l = (lonC + dl + LON) % LON;
                        double loA = l / (double)LON * 2 * Math.PI;
                        double dot = sinLa * cv.y + cosLa * (Math.Cos(loA) * cv.x + Math.Sin(loA) * cv.z);
                        if (dot > cosD) blocked[la * LON + l] = true;
                    }
                }
            }

            // 3) 泛洪求最大连通空白（8 邻域）+ 可建比例
            bool[] visited = new bool[LON * LAT];
            int bestCount = 0;
            double blockedW = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < LON * LAT; i++)
            {
                if (blocked[i]) { blockedW += rowWeight[i / LON] / LON; continue; }
                if (visited[i]) continue;
                int cnt = 0;
                stack.Clear(); stack.Push(i); visited[i] = true;
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    cnt++;
                    int la = cur / LON, lo = cur % LON;
                    for (int dlo = -1; dlo <= 1; dlo++)
                    {
                        int nlo = (lo + dlo + LON) % LON;
                        for (int dla = -1; dla <= 1; dla++)
                        {
                            if (dla == 0 && dlo == 0) continue;
                            int nla = la + dla;
                            if (nla < 0 || nla >= LAT) continue;
                            int ni = nla * LON + nlo;
                            if (!blocked[ni] && !visited[ni]) { visited[ni] = true; stack.Push(ni); }
                        }
                    }
                }
                if (cnt > bestCount) bestCount = cnt;
            }

            double blankW = totalW - blockedW;
            result.BuildableRatio = blankW / totalW;   // 内部参考：矿点占球面比例极小（<0.5%），可建比例天然≈1
            // 4) 1~10 档：按矿组数分级（10=无矿/最空旷；矿点越多越挤分越低）
            //    矿点占球面比例极小，按面积分档无区分度；组数档稳定可比，契合“10 代表没有矿”
            int lvl = 11 - (veinDirs.Count + 9) / 10;   // 每 10 矿组降 1 档：0-10→10、11-20→9、...91+→1
            result.Level = Math.Max(1, Math.Min(10, lvl));
            return result;
        }
    }
}

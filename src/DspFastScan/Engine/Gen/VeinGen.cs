using System;
using System.Collections.Generic;
using DspSeedFinder.Data;
using DspSeedFinder.MathLf;
using DspSeedFinder.Random;

namespace DspSeedFinder.Gen
{
    /// <summary>矿脉生成（PlanetAlgorithm.GenerateVeins 忠实移植之主逻辑）。
    /// 说明：矿点不分陆地/水下全部计入（游戏可改造海平面开采）；出生星第 1 组铁/铜特殊（6 点/opacity 0.2）。
    /// 组数 = 主题 VeinSpot（±1 抖动可选，默认不开以对总量）；组内点数 = Round(VeinCount × 20~24)。
    /// </summary>
    public static class VeinGen
    {
        private static readonly string[] veinNames = new string[15]
        { "", "铁矿", "铜矿", "硅石", "钛石", "石矿", "煤矿", "原油", "可燃冰",
          "金伯利矿石", "分形硅石", "有机晶体", "光栅石", "刺笋结晶", "单极磁石" };

        public static string VeinName(int type) => type >= 1 && type <= 14 ? veinNames[type] : "?" + type;


        /// <summary>计算每矿种组数（快速查表版：VeinSpot + 稀有矿概率，不跑地形/矿脉管线）。
        /// 注：47af515 曾改为完整管线（GenerateVeins → 16 万顶点建模），快筛 31% 种子全量建模 → 慢 + 内存爆；
        /// 2026-10-24 恢复为查表版（与 4f347cf 同实现），随机流用魔改 DotNet35Random（与 DspCore48 一致）。</summary>
        public static int[] GenerateGroupCounts(PlanetData planet)
        {
            ThemeProto theme = LDB.Theme(planet.theme);
            var spots = new int[15];
            if (theme == null) return spots;

            var rnd = new DotNet35Random(planet.seed);
            rnd.Next(); rnd.Next(); rnd.Next(); rnd.Next();
            rnd.Next();
            rnd.Next();

            for (int i = 0; i < Math.Min(theme.VeinSpot.Count, 14); i++) spots[i + 1] = theme.VeinSpot[i];

            float p = 1f;
            switch (planet.star.type)
            {
                case EStarType.MainSeqStar:
                    switch (planet.star.spectr)
                    {
                        case ESpectrType.M: p = 2.5f; break;
                        case ESpectrType.K: p = 1f; break;
                        case ESpectrType.G: p = 0.7f; break;
                        case ESpectrType.F: p = 0.6f; break;
                        case ESpectrType.A: p = 1f; break;
                        case ESpectrType.B: p = 0.4f; break;
                        case ESpectrType.O: p = 1.6f; break;
                    }
                    break;
                case EStarType.GiantStar: p = 2.5f; break;
                case EStarType.WhiteDwarf:
                {
                    p = 3.5f;
                    spots[9] += 2;
                    for (int j = 1; j < 12 && rnd.NextDouble() < 0.45; j++) spots[9]++;
                    spots[10] += 2;
                    for (int k = 1; k < 12 && rnd.NextDouble() < 0.45; k++) spots[10]++;
                    spots[12]++;
                    for (int l = 1; l < 12 && rnd.NextDouble() < 0.5; l++) spots[12]++;
                    break;
                }
                case EStarType.NeutronStar:
                {
                    p = 4.5f;
                    spots[14]++;
                    for (int m = 1; m < 12 && rnd.NextDouble() < 0.65; m++) spots[14]++;
                    break;
                }
                case EStarType.BlackHole:
                {
                    p = 5f;
                    spots[14]++;
                    for (int i = 1; i < 12 && rnd.NextDouble() < 0.65; i++) spots[14]++;
                    break;
                }
            }

            for (int n = 0; n < theme.RareVeins.Count; n++)
            {
                int type = theme.RareVeins[n];
                if (type < 1 || type > 14 || theme.RareSettings.Count < n * 4 + 4) continue;
                float prob = planet.star.index == 0 ? theme.RareSettings[n * 4] : theme.RareSettings[n * 4 + 1];
                float cascade = theme.RareSettings[n * 4 + 3];
                prob = 1f - Mathf.Pow(1f - prob, p);
                cascade = 1f - Mathf.Pow(1f - cascade, p);
                if (!(rnd.NextDouble() < prob)) continue;
                spots[type]++;
                for (int i = 1; i < 12 && rnd.NextDouble() < cascade; i++)
                    spots[type]++;
            }
            return spots;
        }

        /// <summary>计算矿脉总量（counts=组数, amounts=总储量）。完整管线：地形 + 基类 GenerateVeins（含落点/撒点/组数抖动）。</summary>
        public static (int[] counts, long[] amounts) GenerateVeins(PlanetData planet)
        {
            if (planet.data == null)
                PlanetModelingManager.ModelingPlanet(planet);
            PlanetModelingManager.Algorithm(planet).GenerateVeins();
            return (planet.veinCounts, planet.veinAmounts);
        }

    }
}

using System;
using UnityEngine;

namespace DspCore
{
    /// <summary>矿脉静态 API（搜索器用）：理论组数（快筛）/精算汇总（ModelPlanet 后）/矿名。</summary>
    public static class VeinGen
    {
        private static readonly string[] veinNames = new string[15]
        { "", "铁矿", "铜矿", "硅石", "钛石", "石矿", "煤矿", "原油", "可燃冰",
          "金伯利矿石", "分形硅石", "有机晶体", "光栅石", "刺笋结晶", "单极磁石" };

        public static string VeinName(int type) => type >= 1 && type <= 14 ? veinNames[type] : "?" + type;

        /// <summary>快筛理论组数（VeinSpot + 特殊星固定矿 + 稀有矿概率；无落点/无地形，毫秒级）。</summary>
        public static int[] GenerateGroupCounts(PlanetData planet)
        {
            ThemeProto theme = LDB.themes.Select(planet.theme);
            var spots = new int[15];
            if (theme == null) return spots;

            var rnd = new DotNet35Random(planet.seed);
            rnd.Next(); rnd.Next(); rnd.Next(); rnd.Next();
            rnd.Next();
            rnd.Next();

            if (theme.VeinSpot != null)
                for (int i = 0; i < Math.Min(theme.VeinSpot.Length, 14); i++) spots[i + 1] = theme.VeinSpot[i];

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

            for (int n = 0; n < theme.RareVeins.Length; n++)
            {
                int type = theme.RareVeins[n];
                if (type < 1 || type > 14 || theme.RareSettings == null || theme.RareSettings.Length < n * 4 + 4) continue;
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

        /// <summary>精算汇总（要求先 ModelPlanet 生成矿脉）：组数 + 总储量。</summary>
        public static (int[] counts, long[] amounts) GenerateVeins(PlanetData planet)
        {
            var counts = new int[15];
            var amounts = new long[15];
            if (planet.data == null) return (counts, amounts);
            for (int v = 1; v <= 14; v++)
            {
                counts[v] = (int)GenApi.PlanetVeinGroups(planet, v);
                amounts[v] = GenApi.PlanetVeinAmount(planet, v);
            }
            return (counts, amounts);
        }
    }
}
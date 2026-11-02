using System;
using System.Collections.Generic;
using System.Linq;
using DspCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DspSearcher
{
    /// <summary>galaxy.json 生成（S2：初始生成 → 嵌套 JSON 真相源）。
    /// 结构：meta + stars[]（含 planets[]，行星内嵌 veins[]）+ plan[] + save。
    /// 数据来自单种子精算（DspCore48 引擎）内存对象 + 行星级矿脉明细；S3 规划写 plan、S4 存档融合写 save。</summary>
    public static class GalaxyJson
    {
        public static string Build(GalaxyData galaxy, Dictionary<int, (int[] cnts, long[] amts)> veins,
            int seed, float resourceMultiplier, string generator)
        {
            var root = new JObject();
            root["meta"] = new JObject
            {
                ["seed"] = seed,
                ["starCount"] = galaxy.starCount,
                ["galaxyAlgo"] = DspCore.UniverseGen.algoVersion,
                ["resourceMultiplier"] = resourceMultiplier,
                ["starNameLCID"] = DspCore.StarGen.StarNameLCID,
                ["generatedAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["generator"] = generator,
                ["save"] = null,
            };
            var stars = new JArray();
            foreach (var st in galaxy.stars)
            {
                var jo = new JObject
                {
                    ["id"] = st.id,
                    ["name"] = st.name,
                    ["nameSource"] = "namegen",
                    ["type"] = st.type.ToString(),
                    ["spectr"] = st.spectr.ToString(),
                    ["mass"] = st.mass,
                    ["luminosity"] = st.luminosity,
                    ["position"] = new JObject { ["x"] = st.position.x, ["y"] = st.position.y, ["z"] = st.position.z },
                    ["distanceFromBirth"] = st.position.magnitude,
                    ["resourceCoef"] = st.resourceCoef,
                    ["planetCount"] = st.planetCount,
                    ["special"] = Special(st.type),
                };
                var planets = new JArray();
                foreach (var pl in st.planets)
                {
                    var pj = new JObject
                    {
                        ["id"] = pl.id,
                        ["number"] = pl.number,
                        ["name"] = pl.name,
                        ["nameSource"] = "namegen",
                        ["themeId"] = pl.theme,
                        ["themeName"] = ThemeName(pl.theme),
                        ["themeNameEn"] = "",   // 待补（游戏本地化表/LDB 无英文名源）
                        ["type"] = pl.type.ToString(),
                        ["isGasGiant"] = pl.type == EPlanetType.Gas,
                        ["radius"] = pl.radius,
                        ["waterItemId"] = pl.waterItemId,
                        ["orbitAround"] = pl.orbitAround,
                        ["orbitIndex"] = pl.orbitIndex,
                        ["orbit"] = new JObject
                        {
                            ["radius"] = pl.orbitRadius,
                            ["inclination"] = pl.orbitInclination,
                            ["longitude"] = pl.orbitLongitude,
                            ["phase"] = pl.orbitPhase,
                        },
                        ["landPercent"] = pl.landPercent,
                        ["temperature"] = pl.temperatureBias,   // 主题匹配用 bias；实际温度 = theme.Temperature
                        ["scanned"] = false,
                        ["singularities"] = Singularities(pl),
                        ["veins"] = Veins(pl.id, veins),
                    };
                    planets.Add(pj);
                }
                jo["planets"] = planets;
                stars.Add(jo);
            }
            root["stars"] = stars;
            root["plan"] = new JArray();
            root["save"] = null;
            return root.ToString(Formatting.Indented);
        }

        private static JArray Special(EStarType t)
        {
            var a = new JArray();
            if (t == EStarType.BlackHole) a.Add("blackhole");
            else if (t == EStarType.NeutronStar) a.Add("neutron");
            else if (t == EStarType.GiantStar) a.Add("giant");
            return a;
        }

        private static string ThemeName(int themeId)
        {
            var th = DspCore.LDB.themes.Select(themeId);
            return th?.DisplayName ?? "?";
        }

        private static JArray Singularities(PlanetData pl)
        {
            var a = new JArray();
            if (pl.orbitAround > 0) a.Add("卫星");
            if ((pl.singularity & EPlanetSingularity.TidalLocked) != EPlanetSingularity.None) a.Add("潮汐锁定永昼永夜");
            if ((pl.singularity & EPlanetSingularity.TidalLocked2) != EPlanetSingularity.None) a.Add("潮汐锁定1:2");
            if ((pl.singularity & EPlanetSingularity.TidalLocked4) != EPlanetSingularity.None) a.Add("潮汐锁定1:4");
            if ((pl.singularity & EPlanetSingularity.LaySide) != EPlanetSingularity.None) a.Add("横躺自转");
            if ((pl.singularity & EPlanetSingularity.ClockwiseRotate) != EPlanetSingularity.None) a.Add("反向自转");
            if ((pl.singularity & EPlanetSingularity.MultipleSatellites) != EPlanetSingularity.None) a.Add("多卫星");
            return a;
        }

        private static JArray Veins(int planetId, Dictionary<int, (int[] cnts, long[] amts)> veins)
        {
            var a = new JArray();
            if (veins.TryGetValue(planetId, out var v) && v.cnts != null && v.amts != null)
            {
                for (int t = 1; t <= 14; t++)
                {
                    a.Add(new JObject
                    {
                        ["type"] = t,
                        ["groups"] = v.cnts[t],
                        ["initialAmount"] = v.amts[t],
                        ["currentAmount"] = v.amts[t],
                        ["amountSource"] = "model",
                    });
                }
            }
            return a;
        }
    }
}

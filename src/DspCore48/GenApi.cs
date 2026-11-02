using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace DspCore
{
    /// <summary>权威生成 API（net48 + 游戏 DLL 数学，与 DspFindSeed-3.1 验证版同构）：
    /// 星系生成 / 行星建模 / 矿脉统计 / CSV / 存档解析。</summary>
    public static class GenApi
    {

        [System.Runtime.InteropServices.DllImport("msvcrt.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        static extern uint _controlfp(uint newValue, uint mask);
        public static GalaxyData CreateGalaxy(int seed, int starCount = 64)
        {
            PlanetModelingManager.Start();   // 12:23 验证时的原始初始化（无 themeIds 赋值）
            RandomTable.Init();
            var desc = new GameDesc();
            desc.SetForNewGame(UniverseGen.algoVersion, seed, starCount, 1, 1);
            return UniverseGen.CreateGalaxy(desc);
        }

        private static bool _inited;
        private static readonly object _initLock = new object();

        /// <summary>完整初始化（幂等 + 线程安全）：球面随机表 + LDB 全表预热 + ThemeProto.themeIds + 矿脉表（对照 DspFindSeed-3.1 Program 序列）。</summary>
        public static void Init()
        {
            if (_inited) return;
            lock (_initLock)
            {
                if (_inited) return;
                _controlfp(0x0009001F, 0xFFFFFFFF);
                RandomTable.Init();
                _ = LDB.themes; _ = LDB.items; _ = LDB.veins;   // 预热全部表（懒加载非线程安全，多线程首访会竞态）
                ThemeProto[] themeArr = LDB.themes.dataArray;
                DspCore.ThemeProto.themeIds = new int[themeArr.Length];
                for (int index = 0; index < themeArr.Length; ++index)
                    DspCore.ThemeProto.themeIds[index] = themeArr[index].ID;
                PlanetModelingManager.Start();
                _inited = true;
            }
        }

        /// <summary>供直连 UniverseGen 的调用方（Searcher 快筛）在生成前确保初始化。</summary>
        public static void EnsureInit() => Init();

        /// <summary>完整行星建模：顶点→地形→陆海比→矿脉。</summary>
        public static void ModelPlanet(PlanetData planet)
        {
            _controlfp(0x0009001F, 0xFFFFFFFF);
            if (planet.data == null)
                planet.data = new PlanetRawData(200);
            PlanetModelingManager.CalcVerts(planet.data);
            var algo = PlanetModelingManager.Algorithm(planet);
            algo.GenerateTerrain(planet.mod_x, planet.mod_y);
            algo.CalcWaterPercent();
            algo.GenerateVeins();
        }

        /// <summary>某矿种总储量（veinPool 汇总）。</summary>
        public static long PlanetVeinAmount(PlanetData planet, int veinType)
        {
            long sum = 0;
            var data = planet.data;
            if (data == null) return 0;
            for (int i = 1; i < data.veinCursor; i++)
            {
                var v = data.veinPool[i];
                if ((int)v.type == veinType) sum += v.amount;
            }
            return sum;
        }

        /// <summary>某矿种矿组数（groupIndex 去重，对应存档 VeinGroup 数量）。</summary>
        public static long PlanetVeinGroups(PlanetData planet, int veinType)
        {
            var seen = new HashSet<int>();
            var data = planet.data;
            if (data == null) return 0;
            for (int i = 1; i < data.veinCursor; i++)
            {
                var v = data.veinPool[i];
                if ((int)v.type == veinType) seen.Add(v.groupIndex);
            }
            return seen.Count;
        }

        public static string EarthVeinName(int v)
        {
            switch (v)
            {
                case 1: return "铁"; case 2: return "铜"; case 3: return "硅"; case 4: return "钛"; case 5: return "石";
                case 6: return "煤"; case 7: return "油"; case 8: return "燃冰"; case 9: return "金伯利"; case 10: return "分形硅";
                case 11: return "有机晶"; case 12: return "光栅石"; case 13: return "刺笋"; case 14: return "单极磁"; default: return "?";
            }
        }

        /// <summary>生成 + 全量建模，输出行星级矿脉 CSV（列对齐 dll-veins.csv：行星id,恒星,主题,轨道,铁组,铁储量,...）。</summary>
        public static void ExportVeinsCsv(GalaxyData galaxy, string csvPath)
        {
            var sb = new StringBuilder();
            sb.Append("行星id,恒星,主题,轨道,");
            for (int v = 1; v <= 14; v++) sb.Append(EarthVeinName(v)).Append("组,").Append(EarthVeinName(v)).Append("储量,");
            sb.Length--;
            sb.Append('\n');

            int done = 0;
            for (int si = 0; si < galaxy.starCount; si++)
            {
                var star = galaxy.stars[si];
                if (star == null || star.planets == null) continue;
                foreach (var planet in star.planets)
                {
                    if (planet == null || planet.type == EPlanetType.Gas) continue;
                    var th = LDB.themes.Select(planet.theme);
                    try
                    {
                        ModelPlanet(planet);
                        long[] groups = new long[32], total = new long[32];
                        for (int v = 1; v < 32; v++) total[v] = PlanetVeinAmount(planet, v);
                        for (int v = 1; v < 32; v++) groups[v] = PlanetVeinGroups(planet, v);
                        sb.Append(planet.id).Append(',').Append(star.name).Append(',').Append(th?.DisplayName).Append(',').Append(planet.orbitIndex).Append(',');
                        for (int v = 1; v <= 14; v++) sb.Append(groups[v]).Append(',').Append(total[v]).Append(',');
                        sb.Length--;
                        sb.Append('\n');
                        done++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  行星 {planet.id} 建模异常 [{ex.GetType().Name}]: {ex.Message}");
                        try { Console.WriteLine(ex.StackTrace); } catch { }
                    }
                }
            }
            File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"建模行星数：{done}  CSV: {csvPath}");
        }

        /// <summary>存档解析（SaveExport.Run 包装）：--save {dsv} [--space 0/1] → 恒星级 + 行星级 CSV。</summary>
        public static void ExportSave(string savePath, string outPath, bool computeSpace = true, bool computeMine = true) => SaveExport.Run(savePath, outPath, computeSpace, computeMine);
    }
}
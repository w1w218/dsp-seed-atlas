using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DspSeedFinder.Data
{
    /// <summary>主题原型（对应 ThemeProtoSet.json 的一行）。</summary>
    public class ThemeProto
    {
        public int ID { get; set; }
        public string Name { get; set; } = "";
        public string SID { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string BriefIntroduction { get; set; } = "";
        public int PlanetType { get; set; }
        public string MaterialPath { get; set; } = "";
        public float Temperature { get; set; }
        public int Distribute { get; set; }
        public List<int> Algos { get; set; } = new();
        public List<float> ModX { get; set; } = new();
        public List<float> ModY { get; set; } = new();
        public int EigenBit { get; set; }
        public List<int> Vegetables0 { get; set; } = new();
        public List<int> Vegetables1 { get; set; } = new();
        public List<int> Vegetables2 { get; set; } = new();
        public List<int> Vegetables3 { get; set; } = new();
        public List<int> Vegetables4 { get; set; } = new();
        public List<int> Vegetables5 { get; set; } = new();
        public List<int> VeinSpot { get; set; } = new();
        public List<float> VeinCount { get; set; } = new();
        public List<float> VeinOpacity { get; set; } = new();
        public List<int> RareVeins { get; set; } = new();
        public List<float> RareSettings { get; set; } = new();
        public List<int> GasItems { get; set; } = new();
        public List<float> GasSpeeds { get; set; } = new();
        public int UseHeightForBuild { get; set; }   // json 中为 0/1（导出为 int）
        public float Wind { get; set; }
        public float IonHeight { get; set; }
        public float WaterHeight { get; set; }
        public long WaterItemId { get; set; }   // json 中可能为 4294967295/4294967294（即 int -1/-2）
        public List<int> Musics { get; set; } = new();
        public string SFXPath { get; set; } = "";
        public float SFXVolume { get; set; }
        public float CullingRadius { get; set; }
        public int IceFlag { get; set; }

        public bool IsGas => PlanetType == (int)EPlanetType.Gas;
    }

    /// <summary>矿脉原型（对应 VeinProtoSet.json 的一行）。</summary>
    public class VeinProto
    {
        public int ID { get; set; }
        public string Name { get; set; } = "";
        public string SID { get; set; } = "";
        public string IconPath { get; set; } = "";
        public string IconTag { get; set; } = "";
        public string Description { get; set; } = "";
        public int ModelIndex { get; set; }
        public int ModelCount { get; set; }
        public float CircleRadius { get; set; }
        public int MiningItem { get; set; }
        public int MiningTime { get; set; }
        public int MiningAudio { get; set; }
        public int MiningEffect { get; set; }
        public int MinerBaseModelIndex { get; set; }
        public int MinerCircleModelIndex { get; set; }
        public string iconTagString { get; set; } = "";
        public string iconNameTagString { get; set; } = "";
    }

    /// <summary>Prototype 表（json 容器）。</summary>
    public class ProtoSet<T>
    {
        public string table { get; set; } = "";
        public int count { get; set; }
        public List<T> rows { get; set; } = new();
    }

    /// <summary>主题/矿脉/物品原型库（从 dsp\data\json 加载，P0 只用 themes；veins/items 供 P2）。</summary>
    public static class LDB
    {
        public static string DataDir { get; set; } = "data/json";

        private static List<ThemeProto>? _themes;
        private static List<VeinProto>? _veins;

        public static List<ThemeProto> themes => _themes ??= Load<ThemeProto>("ThemeProtoSet");
        public static List<VeinProto> veins => _veins ??= Load<VeinProto>("VeinProtoSet");

        public static List<T> Load<T>(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, DataDir, name + ".json");
            // 回退到相对工程路径
            if (!File.Exists(path))
            {
                string alt = Path.Combine(Directory.GetCurrentDirectory(), "data", "json", name + ".json");
                if (File.Exists(alt)) path = alt;
            }
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到原型数据: " + path);
            var set = JsonSerializer.Deserialize<ProtoSet<T>>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return set!.rows;
        }

        public static ThemeProto Theme(int id)
        {
            foreach (var t in themes)
                if (t.ID == id) return t;
            return null!;
        }
    }
}

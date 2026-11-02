using System.IO;
using System.Text.Json;

namespace DspSearcher.Core;

/// <summary>配比需求（读 data/balance.json）。veins: EVeinType→每分钟需求量；gases: 物品id→每分钟需求量。</summary>
public class Balance
{
    public string Name = "";
    public string Source = "";
    public Dictionary<int, double> Veins = new();   // type 1..14
    public Dictionary<int, double> Gases = new();   // itemId 1120 氢 / 1121 重氢

    public static Balance Load(string path)
    {
        return JsonSerializer.Deserialize<Balance>(File.ReadAllText(path),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true })
               ?? throw new Exception($"无法解析配比文件 {path}");
    }

    /// <summary>该矿种是否有需求（参与瓶颈）。</summary>
    public bool HasVeinDemand(int type) => Veins.TryGetValue(type, out double v) && v > 0;
    public bool HasGasDemand(int itemId) => Gases.TryGetValue(itemId, out double v) && v > 0;
    public double Gas(int itemId) => Gases.TryGetValue(itemId, out double v) ? v : 0;
}

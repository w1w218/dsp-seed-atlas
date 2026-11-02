using Newtonsoft.Json;

namespace DspSearcher.Core;

/// <summary>候选种子记录（快筛理论分；精算后换用精确储量重评）。
/// 公共字段一律用属性（WPF DataGrid 绑定/JSON 序列化均依赖属性）。</summary>
public partial class Candidate
{
    public int Seed { get; set; }
    public double ScoreMinutes { get; set; }           // 支撑产量（有限矿瓶颈：分钟）
    public string Bottleneck { get; set; } = "";       // 瓶颈矿名
    public string SecondBottleneckName { get; set; } = "";  // 第二瓶颈矿名

    // 评分明细（快筛理论；精算更新）
    public long[] VeinSupply { get; set; } = new long[15];          // 星系储量汇总
    public Dictionary<int, double> GasSupply { get; set; } = new(); // itemId → 每分钟速率

    // 过滤命中
    public int OStarCount { get; set; }
    public int BlackHoleCount { get; set; }
    public int NeutronCount { get; set; }
    public double NearSpecialDist { get; set; }
    public double FarSpecialDist { get; set; }
    public bool SpecialPairOk { get; set; }          // 4.2 达标（优先）
    public bool HomeIceGiant { get; set; }           // 4.3
    public bool BirthOnIceGiant { get; set; }        // 4.3b（优先）
    public int IceGiantCount { get; set; }
    public int PlanetsIn6Ly { get; set; }
    public string Missing { get; set; } = "";        // 4.4 缺失（应为空，快筛通过者）
    public bool[] Coverage { get; set; } = new bool[15];

    // 母星信息
    public int BirthPlanetId { get; set; }
    public string BirthTheme { get; set; } = "";
    public double BirthResourceCoef { get; set; }
    public double BirthStarDist { get; set; }

    public bool Refined { get; set; }                // 精算完成

    // 精算分析字段（RefineOne 收集）
    public long SixLyTotal { get; set; }             // 6ly 内稀有矿(9~14，不含可燃冰)总储量（丰富度）
    public long[] SixLyRare { get; set; } = new long[15];  // 6ly 内各稀有矿储量（索引 9~14，均衡度用）
    public double SixLyBalance { get; set; }         // 6ly 稀有矿均衡度（min 储量/需求，分钟，分析阶段算）
    public int HomeIceSatellites { get; set; }       // 母星所绕冰巨星的卫星总数（含母星）
    public double MinOStarDist { get; set; }         // 最近 O 星到母星（银河中心）距离
    public string RichLevel { get; set; } = "";      // 6ly 丰富度：高/中/低（池内三等分）

    // 精算明细（产出用）：每行星每矿组数/储量 + 恒星冗余字段；不进 checkpoint（体积大，产出后即弃）
    [JsonIgnore]
    public List<(int starId, string starName, double starDist, int starType, int starSpectr, int planetId,
        string planetName, string theme, bool isGas, int orbitAround, bool in6ly, int[] cnts, long[] amts)> _details { get; set; } = new();

    // 显示属性（UI）
    public int Rank { get; set; }
    public double Hours => ScoreMinutes / 60.0;
    public string SpecText => $"{BlackHoleCount}/{NeutronCount}";
    public string DistText => $"{NearSpecialDist:F1}~{FarSpecialDist:F1}";
    public string PairOkText => SpecialPairOk ? "✓" : "";
    public string IceMoonText => BirthOnIceGiant ? "✓" : "";
}
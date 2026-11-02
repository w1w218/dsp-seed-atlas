namespace DspSearcher.Core;

/// <summary>搜索器配置（与 UI 条件面板一一对应）。</summary>
public class SearchConfig
{
    // 搜索范围
    public long From = 0;
    public long To = 99_999_999;
    public int Threads = 32;
    /// <summary>精算线程数（独立于快筛；精算全量建模内存大，默认 8 防 x86 OOM，可调）。</summary>
    public int RefineThreads = 32;

    // 4.1 O 星 ≥ N（强制）
    public int MinOStar = 3;

    // 4.2 黑洞/中子星：近≤NearDist 且远≥FarDist（**优先加分，不淘汰**）
    public bool NeedSpecialStarPair = true;
    public double NearDist = 8.0;
    public double FarDist = 15.0;

    // 4.3 母星系含冰巨星（强制）；母星为冰巨星卫星（加分）
    public bool NeedHomeIceGiant = true;

    // 4.4 6ly 内 13 资源全覆盖（强制；单极磁石不算，可燃冰含冰巨星气体）
    public double CoverageRadius = 6.0;
    public bool ExcludeMag = true;

    // 评分
    public const int GAS_STATIONS = 40;  // 气巨轨道采集站上限（游戏固定，无需配置）
    public int PoolSize = 500;           // 快筛 top 池容量
    public long BlockSize = 1000;        // 每块种子数（checkpoint 按 100 块 ≈ 10 万）
    public long CheckpointEvery = 100_000;

    // 输出
    public string BalanceJson = "data/balance.json";
    public string OutDir = "";           // 空 = search-out/<时间戳>
    public int GalaxySeed = 0;           // 0=搜索；>0 = 单种子验证模式
    public int StarCount = 64;
    public float ResourceMultiplier = 1f; // 单种子/搜索资源倍率（与存档同口径对拍时传入，如 0.1）
    public bool RareResource = false;    // 兼容保留；实际由 ResourceMultiplier <= 0.1 自动推导（见 Searcher.MakeDesc）
    public bool SkipRefine = false;      // 调试：跳过精算，直接用理论分出 top
}

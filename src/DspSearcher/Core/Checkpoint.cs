using System.IO;
using System.Threading;
using Newtonsoft.Json;

namespace DspSearcher.Core;

/// <summary>运行统计（快筛各过滤条件淘汰计数）。</summary>
public class SearchStats
{
    public long Scanned;        // 已扫种子
    public long StarPass;       // 星表层通过（O星≥N）
    public long FailOStar;      // 4.1 淘汰
    public long PlanetPass;     // 行星层通过（4.3+4.4）
    public long FailIceGiant;   // 4.3 淘汰
    public long FailCoverage;   // 4.4 淘汰
    public long SpecialPairOk;  // 4.2 达标（优先项命中数）

    public long Hit => PlanetPass;
    public double HitRate => Scanned == 0 ? 0 : (double)PlanetPass / Scanned;
    public long OStarPassRate => Scanned == 0 ? 0 : StarPass;   // 星表通过率 = StarPass/Scanned
}

/// <summary>断点持久化：search-checkpoint.json（System.Text.Json）。</summary>
public class Checkpoint
{
    public string Version = "1";
    public string Phase = "fast";          // fast / refine / done
    public long NextSeed;
    public long SeedTo;                    // 快筛目标终点
    public int RefineDone;                 // 精算已完成候选数（断点续精算）
    public SearchStats Stats = new();
    public List<Candidate> Pool = new();
    public SearchConfig Config = new();
    public DateTime StartUtc;
    public long ElapsedSec;                // 累计运行时长（跨 checkpoint）
    public DateTime SavedUtc;

    public static Checkpoint Load(string path)
    {
        // FileShare.ReadWrite + Delete：写方 Save（Delete+Move 原子替换）期间 GUI 轮询读不产生文件锁冲突。
        // 注意 FileShare.ReadWrite 不含 Delete——读句柄未共享删除时写方 File.Delete 仍会 IOException。
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return JsonConvert.DeserializeObject<Checkpoint>(sr.ReadToEnd(), JsonSettings()) ?? throw new Exception($"无法解析检查点 {path}");
    }

    public void Save(string path)
    {
        SavedUtc = DateTime.UtcNow;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented, JsonSettings()));
        // 原子替换：写 tmp → Delete(path) → Move(tmp→path)。
        // 读方（GUI 轮询）已共享 ReadWrite|Delete → Delete 获准；Move 无 overwrite（目标已删）→ 永不触发
        // “替换已打开目标”的 Access denied（实测 File.Move(overwrite:true)/File.Replace 在目标被读句柄
        // 打开时抛 UnauthorizedAccessException——均不可用）。IOException 重试兑底。
        for (int i = 0; ; i++)
        {
            try { if (File.Exists(path)) File.Delete(path); File.Move(tmp, path); break; }
            catch (IOException) when (i < 5) { Thread.Sleep(100); }
        }
    }

    private static JsonSerializerSettings JsonSettings()
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };
        settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        return settings;
    }
}

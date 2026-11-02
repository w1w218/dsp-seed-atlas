using System;
using System.IO;
using System.Windows;
using DspSearcher.Core;

namespace DspSearcher;

public partial class App : Application
{
    /// <summary>入口：--headless [--from N] [--to N] [--threads N] [--resume checkpoint] 无窗口运行；
    /// 无参数启动 WPF 界面。headless 进程无任何窗口 → 无“失焦”事件 → 不会被 Win11 异构调度赶到 E 核。</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--headless")
        {
            RunHeadless(args);
            return;
        }
        if (args.Length > 0 && args[0] == "--debug-gd")
        {
            RunDebugGd(args[1]);
            return;
        }
        var app = new App();
        app.Run(new MainWindow());
    }

    /// <summary>调试：用游戏自身 GameData.Import 读档，打印 factoryCount 等（验证字节解析的权威参照）。</summary>
    private static void RunDebugGd(string savePath)
    {
        try
        {
            using (var fs = File.OpenRead(savePath))
            {
                var sd = DspCore.SaveParser.ParseHeader(fs);
                Console.WriteLine($"存档: {sd.gameName} seed={sd.galaxySeed}  GameData 起点 @{fs.Position}");
                var br = new BinaryReader(fs);
                var gd = new GameData();
                gd.Import(br);
                Console.WriteLine($"factoryCount={gd.factoryCount}");
                for (int i = 0; i < gd.factoryCount; i++)
                {
                    var pf = gd.factories[i];
                    int pid = pf != null && pf.planet != null ? pf.planet.id : -1;
                    int ec = pf != null ? pf.entityCursor : -1;
                    Console.WriteLine($"  factory[{i}]: planet={pid} entityCursor={ec}");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"ERROR: {e.GetType().Name}: {e.Message}");
            Console.WriteLine(e.StackTrace);
        }
    }

    private static void RunHeadless(string[] args)
    {
        // 日志写文件（无控制台窗口；进程全程无窗口）
        string logPath = Path.Combine(AppContext.BaseDirectory, "headless-search.log");
        void Log(string m)
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {m}{Environment.NewLine}");
        }

        // x86 进程地址空间 ~2GB：大存档解析会 OOM（931MB 实测），直接拒绝并指向 64 位 DspSave
        void EnforceSaveSizeLimit(string path)
        {
            if (Environment.Is64BitProcess) return;
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException($"存档不存在: {path}");
            const long limit = 256L * 1024 * 1024;
            if (fi.Length <= limit) return;
            string msg = $"存档 {fi.Length / 1048576.0:F0} MB 超过 x86 解析上限 {limit / 1048576} MB（x86 ~2GB 地址空间会 OOM）。" +
                $"请改用 64 位：DspSave.exe --merge \"{path}\" --galaxy <galaxy.json>（参见 maps\\COMMANDS.md）";
            Log("ERROR: " + msg);
            Console.Error.WriteLine(msg);
            Environment.Exit(2);
        }

        Searcher searcher = null!;
        string savePath = null;
        string mergeSavePath = null;
        string galaxyJsonPath = null;
        bool computeSpace = true;
        bool computeMine = true;
        try
        {
            var cfg = new SearchConfig();
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--from": cfg.From = long.Parse(args[++i]); break;
                    case "--to": cfg.To = long.Parse(args[++i]); break;
                    case "--threads": cfg.Threads = int.Parse(args[++i]); break;
                    case "--refine-threads": cfg.RefineThreads = int.Parse(args[++i]); break;
                    case "--skip-refine": cfg.SkipRefine = true; break;   // 只跑快筛不精算（测速/调试）
                    case "--out": cfg.OutDir = args[++i]; break;
                    case "--seed": cfg.GalaxySeed = int.Parse(args[++i]); break;   // 单种子精算
                    case "--json": cfg.JsonOut = args[++i]; break;                 // S2：附带输出 galaxy.json
                    case "--resource-multiplier": cfg.ResourceMultiplier = float.Parse(args[++i]); break;   // 资源倍率（默认 1）
                    case "--save": savePath = args[++i]; break;                     // 存档解析（--out 给输出 CSV 文件路径）
                    case "--merge": mergeSavePath = args[++i]; break;               // S4：存档融合到 --galaxy 指定的 galaxy.json
                    case "--galaxy": galaxyJsonPath = args[++i]; break;             // galaxy.json 路径（--merge 用）
                    case "--space": computeSpace = args[++i] != "0"; break;         // 是否计算空间档（默认算；0=跳过）
                    case "--mine": computeMine = args[++i] != "0"; break;          // 是否计算未开采矿组（默认算；0=跳过）
                    case "--resume": searcher = Searcher.Resume(args[++i]); break;
                    default: throw new Exception($"未知参数 {args[i]}");
                }
            }

            // S4 存档融合模式：--merge <dsv> --galaxy <galaxy.json>
            if (mergeSavePath != null)
            {
                EnforceSaveSizeLimit(mergeSavePath);
                DspCore.GenApi.EnsureInit();
                if (galaxyJsonPath == null) throw new Exception("--merge 需要 --galaxy <galaxy.json>");
                Log($"存档融合: {mergeSavePath} -> {galaxyJsonPath}");
                int rc = Merge.Run(mergeSavePath, galaxyJsonPath, Log);
                Log(rc == 0 ? "存档融合完成" : $"存档融合失败 rc={rc}");
                Log("进程退出");
                return;
            }

            // 存档解析模式：--save <dsv> [--out <csv 文件>]
            if (savePath != null)
            {
                EnforceSaveSizeLimit(savePath);
                DspCore.GenApi.EnsureInit();
                string outCsv = cfg.OutDir ?? Path.Combine(AppContext.BaseDirectory,
                    $"save-{Path.GetFileNameWithoutExtension(savePath)}.csv");
                Log($"存档解析: {savePath} -> {outCsv}");
                DspCore.GenApi.ExportSave(savePath, outCsv, computeSpace, computeMine);
                Log("存档解析完成（saveX.csv 恒星级 + saveX-planets.csv 行星级，字段对齐 refined-detail）");
                Log("进程退出");
                return;
            }

            searcher ??= new Searcher(cfg);
            searcher.OnLog += Log;
            searcher.OnProgress += p => Log($"进度: 已扫 {p.Scanned:N0}/{p.Total:N0} 命中 {p.Hit:N0} 速率 {p.RatePerSec:N0}/s 剩余 {p.Eta}");
            searcher.OnDone += r => Log($"完成: {r.OutDir}");
            Log($"headless 启动: seeds {searcher.Cfg.From}~{searcher.Cfg.To} 线程 {searcher.Cfg.Threads} 恢复={args.Contains("--resume")}");
            searcher.Run();
            Log("进程退出");
        }
        catch (Exception e)
        {
            Log("ERROR: " + e);
            throw;
        }
    }
}
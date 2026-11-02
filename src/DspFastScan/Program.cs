using System;
using System.IO;
using DspSearcher.Core;

/// <summary>net8 版搜索器 headless 入口（与 net48 DspSearcher --headless 同参数，用于结果一致性对比）。</summary>
public static class Program
{
    static void Log(string m) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "search-net8.log"), $"[{DateTime.Now:HH:mm:ss}] {m}{Environment.NewLine}");

    public static void Main(string[] args)
    {
        var cfg = new SearchConfig();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--from": cfg.From = long.Parse(args[++i]); break;
                case "--to": cfg.To = long.Parse(args[++i]); break;
                case "--threads": cfg.Threads = int.Parse(args[++i]); break;
                case "--out": cfg.OutDir = args[++i]; break;
                case "--seed": cfg.GalaxySeed = int.Parse(args[++i]); break;
                case "--resource-multiplier": cfg.ResourceMultiplier = float.Parse(args[++i]); break;
                case "--skip-refine": cfg.SkipRefine = true; break;
                case "--refine-threads": cfg.RefineThreads = int.Parse(args[++i]); break;
                case "--min-o": cfg.MinOStar = int.Parse(args[++i]); break;
                case "--near": cfg.NearDist = double.Parse(args[++i]); break;
                case "--far": cfg.FarDist = double.Parse(args[++i]); break;
                case "--radius": cfg.CoverageRadius = double.Parse(args[++i]); break;
                case "--ice-giant": cfg.NeedHomeIceGiant = args[++i] != "0"; break;
                case "--pool-size": cfg.PoolSize = int.Parse(args[++i]); break;
            }
        }
        var searcher = new Searcher(cfg);
        searcher.OnLog += m => Log(m);
        searcher.OnProgress += p => Log($"进度: {p.Scanned}/{p.Total} 命中 {p.Hit} 速率 {p.RatePerSec:N0}/s 剩余 {p.Eta}");
        try { searcher.Run(); }
        catch (Exception e) { Log("ERROR: " + e); throw; }
    }
}
using System;
using System.IO;
using DspCore;

namespace DspSave;

/// <summary>net8 x64 存档入口（大档专用：x86 的 DspSearcher 读 900MB+ 档会 OOM）。
/// 精算仍必须走 net48 x86（浮点与游戏对齐），但存档解析/融合不涉及地形生成，走 x64 即可。
/// 用法：
///   DspSave --save &lt;dsv&gt; [--out &lt;csv&gt;] [--space 0/1] [--mine 0/1]
///   DspSave --merge &lt;dsv&gt; --galaxy &lt;galaxy.json&gt;</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string save = null, galaxy = null, outCsv = null;
        bool space = true, mine = true, merge = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--save": save = args[++i]; break;
                case "--merge": save = args[++i]; merge = true; break;
                case "--galaxy": galaxy = args[++i]; break;
                case "--out": outCsv = args[++i]; break;
                case "--space": space = args[++i] != "0"; break;
                case "--mine": mine = args[++i] != "0"; break;
                case "-h":
                case "--help":
                    Console.WriteLine("用法:");
                    Console.WriteLine("  DspSave --save <dsv> [--out <csv>] [--space 0/1] [--mine 0/1]");
                    Console.WriteLine("  DspSave --merge <dsv> --galaxy <galaxy.json>");
                    return 0;
                default:
                    Console.WriteLine($"未知参数 {args[i]}");
                    return 2;
            }
        }

        if (save == null) { Console.WriteLine("缺少 --save <dsv> 或 --merge <dsv>"); return 2; }
        if (!File.Exists(save)) { Console.WriteLine($"存档不存在: {save}"); return 1; }

        GenApi.EnsureInit();

        if (merge)
        {
            if (galaxy == null) { Console.WriteLine("--merge 需要 --galaxy <galaxy.json>"); return 2; }
            int rc = DspSearcher.Merge.Run(save, galaxy, Console.WriteLine);
            Console.WriteLine(rc == 0 ? "存档融合完成" : $"存档融合失败 rc={rc}");
            return rc;
        }

        string outPath = outCsv ?? Path.Combine(AppContext.BaseDirectory,
            $"save-{Path.GetFileNameWithoutExtension(save)}.csv");
        Console.WriteLine($"存档解析: {save} -> {outPath}");
        GenApi.ExportSave(save, outPath, space, mine);
        Console.WriteLine("存档解析完成");
        return 0;
    }
}

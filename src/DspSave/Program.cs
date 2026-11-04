using System;
using System.IO;
using DspCore;
using Newtonsoft.Json;

namespace DspSave;

/// <summary>net8 x64 存档入口（大档专用：x86 的 DspSearcher 读 900MB+ 档会 OOM）。
/// 精算仍必须走 net48 x86（浮点与游戏对齐），但存档解析/融合不涉及地形生成，走 x64 即可。
/// 用法：
///   DspSave --save &lt;dsv&gt; [--out &lt;csv&gt;] [--space 0/1] [--mine 0/1]
///   DspSave --merge &lt;dsv&gt; --galaxy &lt;galaxy.json&gt;
///   DspSave --header &lt;dsv&gt;           只读存档头，输出单行 JSON（毫秒级）</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string save = null, galaxy = null, outCsv = null, header = null;
        bool space = true, mine = true, merge = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--save": save = args[++i]; break;
                case "--merge": save = args[++i]; merge = true; break;
                case "--galaxy": galaxy = args[++i]; break;
                case "--header": header = args[++i]; break;
                case "--out": outCsv = args[++i]; break;
                case "--space": space = args[++i] != "0"; break;
                case "--mine": mine = args[++i] != "0"; break;
                case "-h":
                case "--help":
                    Console.WriteLine("用法:");
                    Console.WriteLine("  DspSave --save <dsv> [--out <csv>] [--space 0/1] [--mine 0/1]");
                    Console.WriteLine("  DspSave --merge <dsv> --galaxy <galaxy.json>");
                    Console.WriteLine("  DspSave --header <dsv>");
                    return 0;
                default:
                    Console.WriteLine($"未知参数 {args[i]}");
                    return 2;
            }
        }

        // --header：只读头部（不加载原型表/不解析行星段），供脚本做“种子不一致”预校验
        if (header != null)
        {
            if (!File.Exists(header)) { Console.WriteLine($"存档不存在: {header}"); return 1; }
            try
            {
                using (var fs = File.OpenRead(header))
                {
                    var sd = SaveParser.ParseHeader(fs);
                    Console.WriteLine(JsonConvert.SerializeObject(new
                    {
                        gameName = sd.gameName,
                        seed = sd.galaxySeed,
                        starCount = sd.starCount,
                        resourceMultiplier = sd.resourceMultiplier,
                        gameTick = sd.gameTick,
                        starNameLCID = sd.starNameLCID,
                    }));
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"头部解析失败: {ex.Message}");
                return 1;
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

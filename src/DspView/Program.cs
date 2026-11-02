using System;
using System.IO;
using System.Threading;

namespace DspView;

/// <summary>S5 渲染器：galaxy.json → web\（index.html/app.js/style.css/galaxy-data.js/lib）。
/// 可选 --serve 起本地 HTTP（静态文件 + /api/galaxy + /api/notes 保存规划文本）。</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string galaxy = null; int port = 8765; bool serve = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--galaxy": galaxy = args[++i]; break;
                case "--serve": serve = true; break;
                case "--port": port = int.Parse(args[++i]); break;
                case "-h": case "--help":
                    Console.WriteLine("用法: DspView --galaxy <galaxy.json> [--serve] [--port 8765]");
                    return 0;
                default: Console.WriteLine($"未知参数 {args[i]}"); return 1;
            }
        }
        if (galaxy == null) { Console.WriteLine("用法: DspView --galaxy <galaxy.json> [--serve] [--port 8765]"); return 1; }
        if (!File.Exists(galaxy)) { Console.WriteLine($"不存在: {galaxy}"); return 1; }

        string webDir = Renderer.Build(galaxy);
        Console.WriteLine($"已生成: {webDir}");
        if (!serve) return 0;

        var api = new Api(webDir, galaxy, port);
        api.Start();
        Console.WriteLine($"服务: http://localhost:{port}/   (Ctrl+C 停止)");
        var quit = new ManualResetEvent(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
        quit.WaitOne();
        api.Stop();
        Console.WriteLine("已停止");
        return 0;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DspView;

/// <summary>本地 HTTP：静态文件 + GET /api/galaxy + POST /api/notes（保存行星规划文本）。</summary>
public sealed class Api
{
    readonly string _webDir;
    readonly string _galaxyPath;
    readonly HttpListener _listener = new();
    readonly object _writeLock = new();
    Thread _thread;

    static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
        { ".html", ".js", ".css", ".png", ".svg", ".ico", ".json" };
    static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
    };

    public Api(string webDir, string galaxyPath, int port)
    {
        _webDir = Path.GetFullPath(webDir);
        _galaxyPath = Path.GetFullPath(galaxyPath);
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _thread = new Thread(Loop) { IsBackground = true, Name = "dspview-http" };
        _thread.Start();
    }

    public void Stop()
    {
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    void Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
            ThreadPool.QueueUserWorkItem(_ => { try { Handle(ctx); } catch { } });
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url.AbsolutePath;
            if (ctx.Request.HttpMethod == "GET" && path == "/api/galaxy")
            {
                Send(ctx, File.ReadAllBytes(_galaxyPath), "application/json; charset=utf-8");
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && path == "/api/notes")
            {
                HandleNotes(ctx);
                return;
            }
            if (ctx.Request.HttpMethod == "GET")
            {
                ServeStatic(ctx, path);
                return;
            }
            ctx.Response.StatusCode = 405;
            ctx.Response.Close();
        }
        catch (Exception e)
        {
            try { SendText(ctx, 500, "ERROR: " + e.Message); } catch { }
        }
    }

    void ServeStatic(HttpListenerContext ctx, string path)
    {
        if (path == "/") path = "/index.html";
        string rel = Uri.UnescapeDataString(path).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(_webDir, rel));
        if (!full.StartsWith(_webDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            SendText(ctx, 404, "not found");
            return;
        }
        string ext = Path.GetExtension(full);
        if (!AllowedExt.Contains(ext))
        {
            SendText(ctx, 403, "forbidden");
            return;
        }
        Send(ctx, File.ReadAllBytes(full), Mime.TryGetValue(ext, out var m) ? m : "application/octet-stream");
    }

    void HandleNotes(HttpListenerContext ctx)
    {
        string body;
        using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
        JObject req;
        try { req = JObject.Parse(body); }
        catch { SendJson(ctx, new { ok = false, errors = new[] { "JSON 解析失败" } }); return; }

        if (req["notes"] is not JObject notes)
        {
            SendJson(ctx, new { ok = false, errors = new[] { "缺少 notes 对象" } });
            return;
        }

        var errors = new List<string>();
        lock (_writeLock)
        {
            var root = JObject.Parse(File.ReadAllText(_galaxyPath));
            var planets = new Dictionary<int, JObject>();
            foreach (var st in root["stars"])
                foreach (var pl in st["planets"])
                    planets[pl.Value<int>("id")] = (JObject)pl;

            int applied = 0;
            foreach (var p in notes.Properties())
            {
                if (!int.TryParse(p.Name, out int pid) || !planets.TryGetValue(pid, out var pl))
                {
                    errors.Add($"行星不存在: {p.Name}");
                    continue;
                }
                string text = p.Value?.ToString() ?? "";
                if (text.Length > 200) text = text.Substring(0, 200);
                pl["planNote"] = text;
                applied++;
            }
            if (errors.Count == 0)
            {
                File.Copy(_galaxyPath, _galaxyPath + ".bak-note", true);
                File.WriteAllText(_galaxyPath, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            }
            Console.WriteLine($"[api] notes: {applied} 条{(errors.Count > 0 ? "，失败 " + errors.Count : "")}");
        }
        SendJson(ctx, new { ok = errors.Count == 0, errors });
    }

    static void Send(HttpListenerContext ctx, byte[] data, string contentType)
    {
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data, 0, data.Length);
        ctx.Response.Close();
    }

    static void SendJson(HttpListenerContext ctx, object o) =>
        Send(ctx, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o)), "application/json; charset=utf-8");

    static void SendText(HttpListenerContext ctx, int code, string text)
    {
        ctx.Response.StatusCode = code;
        Send(ctx, Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8");
    }
}

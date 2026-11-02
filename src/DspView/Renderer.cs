using System;
using System.IO;
using System.Text;

namespace DspView;

/// <summary>把 galaxy.json 渲染为 web\ 目录（模板 + lib + galaxy-data.js）。</summary>
public static class Renderer
{
    public static string Build(string galaxyPath)
    {
        string jsonPath = Path.GetFullPath(galaxyPath);
        string dir = Path.Combine(Path.GetDirectoryName(jsonPath) ?? ".", "web");
        Directory.CreateDirectory(dir);
        string libOut = Path.Combine(dir, "lib");
        Directory.CreateDirectory(libOut);

        string tplDir = Path.Combine(AppContext.BaseDirectory, "Templates");
        foreach (var f in Directory.GetFiles(tplDir))
            File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);

        string libSrc = Path.Combine(AppContext.BaseDirectory, "lib");
        foreach (var f in Directory.GetFiles(libSrc))
            File.Copy(f, Path.Combine(libOut, Path.GetFileName(f)), true);

        // 数据：window.GALAXY = {...};  —— file:// 下用 <script src> 加载（fetch 会被 CORS 拦）
        string json = File.ReadAllText(jsonPath);
        File.WriteAllText(Path.Combine(dir, "galaxy-data.js"),
            "window.GALAXY = " + json + ";\n", new UTF8Encoding(false));

        return dir;
    }
}

using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DspSearcher.Core;

namespace DspSearcher;

public partial class MainWindow : Window
{
    private Searcher _searcher = null!;
    private Process _fsProc;                  // FastScan 子进程（net8 快筛）
    private System.Windows.Threading.DispatcherTimer _fsTimer;  // 轮询 checkpoint 刷新 Top20
    private string _fsOutDir = "";
    private DateTime _lastCpWrite;   // mtime 守卫：checkpoint 未变则跳过全量反序列化（~27ms→~9µs）
    private long _lastCpLen = -1;

    public MainWindow()
    {
        InitializeComponent();
    }

    private SearchConfig ReadConfig()
    {
        long from = ParseLong(TxtFrom.Text, 0);
        long to = ParseLong(TxtTo.Text, 99_999_999);
        if (to <= from) { Log("错误: 结束种子须大于起始种子"); throw new Exception("范围无效"); }
        return new SearchConfig
        {
            From = from,
            To = to,
            Threads = ParseInt(TxtThreads.Text, 16),
            RefineThreads = ParseInt(TxtRefineThreads.Text, 8),
            MinOStar = ParseInt(TxtMinO.Text, 3),
            NearDist = ParseDouble(TxtNear.Text, 8),
            FarDist = ParseDouble(TxtFar.Text, 15),
            NeedHomeIceGiant = ChkIceGiant.IsChecked == true,
            CoverageRadius = ParseDouble(TxtRadius.Text, 6),
            PoolSize = ParseInt(TxtPoolSize.Text, 500),
        };
    }

    private void Wire(Searcher s)
    {
        // BeginInvoke 异步投递：后台不阻塞；精算按秒计、每次完成即刷新，无需节流。
        s.OnLog += msg => Dispatcher.BeginInvoke(() => Log(msg));
        s.OnProgress += p => Dispatcher.BeginInvoke(() => UpdateProgress(p));
        s.OnTopChanged += list =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
                Grid.ItemsSource = list.ToList();
            });
        };
        s.OnDone += r =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                BtnStart.IsEnabled = true;
                BtnPause.IsEnabled = false;
                BtnStop.IsEnabled = false;
                TxtStatus.Text = "完成";
                var top = _searcher?.Pool.Snapshot();   // 补刷最终榜单
                if (top != null && top.Count > 0)
                {
                    for (int i = 0; i < top.Count; i++) top[i].Rank = i + 1;
                    Grid.ItemsSource = top.Take(20).ToList();
                }
                Log($"完成: 输出目录 {r.OutDir}");
            });
        };
    }

    private void OnFastScan(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = ReadConfig();
            _fsOutDir = Path.Combine(AppContext.BaseDirectory, "search-out", $"fast-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(_fsOutDir);
            var exe = FindFastScanExe();
            if (exe == null) { Log("未找到 DspFastScan.exe（先构建 DspFastScan 工程）"); return; }
            var psi = new ProcessStartInfo(exe,
                $"--from {cfg.From} --to {cfg.To} --threads {cfg.Threads} " +
                $"--refine-threads {cfg.RefineThreads} --min-o {cfg.MinOStar} " +
                $"--near {cfg.NearDist.ToString("0.##")} --far {cfg.FarDist.ToString("0.##")} " +
                $"--radius {cfg.CoverageRadius.ToString("0.##")} " +
                $"--ice-giant {(cfg.NeedHomeIceGiant ? 1 : 0)} --pool-size {cfg.PoolSize} " +
                $"--skip-refine --out \"{_fsOutDir}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            _fsProc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _fsProc.Exited += (_, __) => Dispatcher.BeginInvoke(() =>
            {
                _fsTimer?.Stop();
                var cp = Path.Combine(_fsOutDir, "search-checkpoint.json");
                if (File.Exists(cp))
                {
                    // FastScan 正常完成或中断（异常/IO 冲突都会触发 Exited，退出码恒 0）：
                    // 读 checkpoint 判断快筛是否已到终点——未到 = 中断，resume 会自动接力剩余快筛再精算。
                    try
                    {
                        var cpData = Checkpoint.Load(cp);
                        bool done = cpData.NextSeed >= cpData.SeedTo;
                        Log(done
                            ? $"FastScan 快筛完成（{_fsOutDir}），自动进入精算…"
                            : $"FastScan 快筛中断于 {cpData.NextSeed:N0}/{cpData.SeedTo:N0}，自动恢复继续…");
                    }
                    catch { Log($"FastScan 退出（{_fsOutDir}），尝试恢复…"); }
                    _searcher = Searcher.Resume(cp); Wire(_searcher); _searcher.RunAsync();
                }
                else Log("FastScan 未产出 checkpoint");
            });
            _fsProc.Start();
            Log($"FastScan 启动: seeds {cfg.From}~{cfg.To} 线程 {cfg.Threads}（net8 快筛，完成后自动精算）");
            // 轮询 checkpoint 实时刷新 Top20 / 进度
            _fsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _fsTimer.Tick += (_, __) => RefreshFromCheckpoint(cfg);
            _fsTimer.Start();
            BtnStart.IsEnabled = false; BtnPause.IsEnabled = false; BtnStop.IsEnabled = true;
            TxtStatus.Text = "FastScan 快筛中…";
        }
        catch (Exception ex) { Log("FastScan ERROR: " + ex.Message); }
    }

    private void RefreshFromCheckpoint(SearchConfig cfg)
    {
        var p = Path.Combine(_fsOutDir, "search-checkpoint.json");
        if (!File.Exists(p)) return;
        try
        {
            var fi = new FileInfo(p);
            if (fi.LastWriteTimeUtc == _lastCpWrite && fi.Length == _lastCpLen) return;   // 无变化：跳过反序列化
            _lastCpWrite = fi.LastWriteTimeUtc; _lastCpLen = fi.Length;
            var cp = Checkpoint.Load(p);
            var top = cp.Pool.Take(20).ToList();   // 池已按理论分有序
            for (int i = 0; i < top.Count; i++) top[i].Rank = i + 1;
            Grid.ItemsSource = top;
            long total = cfg.To - cfg.From;
            long scanned = cp.Stats.Scanned;
            double rate = cp.ElapsedSec > 0 ? scanned / (double)cp.ElapsedSec : 0;
            long remain = Math.Max(0, total - scanned);
            string eta = rate > 0 ? TimeSpan.FromSeconds(remain / rate).ToString(@"hh\:mm\:ss") : "-";
            Prog.Maximum = Math.Max(1, total);
            Prog.Value = Math.Min(total, Math.Max(scanned, cp.NextSeed - cfg.From));   // 分发水位平滑推进（Stats.Scanned 每 8192 合并有滞后）
            TxtProgress.Text = $"FastScan 快筛中：已扫 {scanned:N0}/{total:N0} 命中 {cp.Stats.Hit:N0} 候选 {cp.Pool.Count}  速率 {rate:N0}/s  预计剩余 {eta}";
        }
        catch { }
    }

    private string FindFastScanExe()
    {
        var b = AppContext.BaseDirectory;
        var cands = new[]
        {
            Path.Combine(b, "DspFastScan.exe"),
            Path.Combine(b, @"..\..\..\..\DspFastScan\bin\Debug\net8.0\DspFastScan.exe"),
        };
        return cands.FirstOrDefault(File.Exists) ?? "";
    }

    private void OnSingle(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!int.TryParse(TxtSeed.Text, out int seed)) { Log("种子无效"); return; }
            var cfg = new SearchConfig
            {
                GalaxySeed = seed,
                PoolSize = 1,
                OutDir = Path.Combine(AppContext.BaseDirectory, "search-out", $"single-{seed}"),
            };
            _searcher = new Searcher(cfg);
            Wire(_searcher);
            Log($"单种子精算: seed={seed}（完整生成+地形+矿脉，约 30 秒）");
            _searcher.RunAsync();
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        try
        {
            var cfg = ReadConfig();
            _searcher = new Searcher(cfg);
            Wire(_searcher);
            BtnStart.IsEnabled = false;
            BtnPause.IsEnabled = true;
            BtnStop.IsEnabled = true;
            TxtStatus.Text = "快筛运行中…";
            Log("开始搜索 " + cfg.From + " ~ " + cfg.To);
            _searcher.RunAsync();
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (_searcher == null) return;
        _searcher.Cancel();
        BtnPause.IsEnabled = false;
        TxtStatus.Text = "暂停中（保存检查点后退出）…";
        Log("请求暂停，等待当前块完成并保存检查点…");
    }

    private void OnResume(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "检查点|search-checkpoint.json", Title = "选择检查点文件" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _searcher = Searcher.Resume(dlg.FileName);
            Wire(_searcher);
            BtnStart.IsEnabled = false;
            BtnPause.IsEnabled = true;
            BtnStop.IsEnabled = true;
            TxtStatus.Text = "已恢复，快筛运行中…";
            Log($"从检查点恢复: 继续 {_searcher.Cfg.From}，累计已扫 {_searcher.Stats.Scanned}，池 {_searcher.Pool.Count}");
            _searcher.RunAsync();
        }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        if (_fsProc != null && !_fsProc.HasExited)   // FastScan 子进程
        {
            try { _fsProc.Kill(); } catch { }
            _fsTimer?.Stop();
            Log("FastScan 已停止");
            _fsProc = null;
            BtnStart.IsEnabled = true; BtnStop.IsEnabled = false;
            TxtStatus.Text = "已停止";
            return;
        }
        if (_searcher == null) return;
        _searcher.Cancel();
        BtnStop.IsEnabled = false;
        BtnPause.IsEnabled = false;
        TxtStatus.Text = "停止中…";
        Log("请求停止（检查点已由后台保存）。可稍后通过“恢复(读检查点)”继续。");
    }

    private void UpdateProgress(Searcher.ProgressInfo p)
    {
        if (p.Phase == "fast")
        {
            Prog.Maximum = Math.Max(1, p.Total);
            Prog.Value = Math.Min(p.Total, p.Scanned);
            TxtProgress.Text = $"已扫 {p.Scanned:N0} / {p.Total:N0}   命中 {p.Hit:N0}   速率 {p.RatePerSec:N0} seeds/s   预计剩余 {p.Eta}";
        }
        else if (p.Phase == "refine")
        {
            Prog.Maximum = Math.Max(1, p.RefineTotal);
            Prog.Value = p.RefineDone;
            TxtProgress.Text = $"精算中 {p.RefineDone}/{p.RefineTotal}（全量地形+矿脉）   速率 {p.RatePerSec * 60:N1} 个/min   预计剩余 {p.Eta}";
            TxtStatus.Text = "精算阶段";
        }
    }

    private void Log(string msg)
    {
        TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        TxtLog.ScrollToEnd();
    }

    private static long ParseLong(string s, long d) => long.TryParse(s, out long v) ? v : d;
    private static int ParseInt(string s, int d) => int.TryParse(s, out int v) ? v : d;
    private static double ParseDouble(string s, double d) => double.TryParse(s, out double v) ? v : d;
}

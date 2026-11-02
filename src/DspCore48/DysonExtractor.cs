using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DspCore
{
    /// <summary>戴森球层摘要：每层节点/框架/壳的游标与已建计数（净数 = cursor - recycle - 1）。</summary>
    public class DysonLayerSummary
    {
        public int LayerId;
        public double OrbitRadius;
        public int NodeCursor, NodeRecycle, NodeBuilt;
        public int FrameCursor, FrameRecycle, FrameBuilt;
        public int ShellCursor, ShellRecycle;
        public int NodeTotal => NodeCursor - NodeRecycle - 1;
        public int FrameTotal => FrameCursor - FrameRecycle - 1;
        public int ShellTotal => ShellCursor - ShellRecycle - 1;
    }

    /// <summary>戴森球摘要：DysonSphere.Import 后半（1212 魔法标记之后）流式解析结果。</summary>
    public class DysonSummary
    {
        public int StarIndex = -1;      // dysonSpheres 数组下标（恒星 index，0-based）
        public int LayerCount;
        public List<DysonLayerSummary> Layers = new List<DysonLayerSummary>();
        public int RocketCursor, RocketRecycle;
        public long Offset;             // 1212 标记偏移
        public long EndOffset;          // Import 结束偏移（球后下一个 0/1 位置）
        public int RocketInFlight => RocketCursor - RocketRecycle - 1;
        public bool StarMapped;
    }

    /// <summary>戴森球提取（C 项）：1212 魔法标记 → 流式解析 DysonSphere.Import 后半。
    /// 依据：DysonSphere.Import（decomp-full\DysonSphere.cs:1852）+ DysonSphereLayer.Import（:230）
    /// + DysonNode/Frame/Rocket/Shell Import；段布局：GameData.Import dysonSpheres 循环
    /// （count==starCount + 每星 0/1 → 1 时 Import，交替布局，GameData.cs:965）。
    /// 内存策略（x86 进程 2GB 限制）：全文件流式分块扫描只存候选偏移（每块 16MB，恒定内存），
    /// 再对候选逐个 FileStream 流式解析（失败即停）；星图衔接校验最后定恒星。
    /// 注意：球版本 num 在 1212 之前（无法从后回溯），nrd/mask 按"新档必有"解析，失败回退无 nrd。</summary>
    public static class DysonExtractor
    {
        public const int Magic = 1212;
        const long SCAN_BLOCK = 16L * 1024 * 1024;
        const int SCAN_OVERLAP = 64 * 1024;      // 只覆盖 1212 与粗筛 8 字节的跨块边界
        /// <summary>星图回溯窗口：段头可能在第一个球前很远（全 0 星 + 前球 swarm 大）。
        /// 64MB 平衡：buf 需覆盖全链（lastEnd），x86 2GB 下 128MB 窗口会导致 OOM（w1wold3 档实测）。</summary>
        const long WINDOW = 64L * 1024 * 1024;

        /// <summary>流式扫描 1212（粗筛 layerCount/num2）→ 逐候选流式解析 → 恒星对应。</summary>
        public static List<DysonSummary> Extract(string path, int starCount, GalaxyData galaxy = null)
        {
            long len;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                len = fs.Length;

            int threads = Math.Max(2, Math.Min(Environment.ProcessorCount, 16));
            long span = len;
            const long OVERLAP = 64 * 1024;   // 只覆盖 1212 与粗筛 8 字节的跨块边界
            const long BLOCK = 16L * 1024 * 1024;   // 固定块流式（x86 进程内存安全：1.7GB 档不再 OOM）
            int magicHits = 0, coarseOk = 0;
            var cand = new ConcurrentBag<long>();

            // 遍1：固定块流式扫描 1212 + 粗筛（只存偏移，不解析）
            Parallel.For(0, threads, ti =>
            {
                long start = ti * (span / threads);
                long end = Math.Min(len, start + span / threads);
                if (end <= start) return;
                byte[] buf = new byte[BLOCK + OVERLAP];
                long pos = start;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    while (pos < end)
                    {
                        int want = (int)Math.Min(BLOCK + OVERLAP, end - pos);
                        if (want <= 0) break;
                        fs.Seek(pos, SeekOrigin.Begin);
                        int got = 0;
                        while (got < want)
                        {
                            int n = fs.Read(buf, got, want - got);
                            if (n <= 0) break;
                            got += n;
                        }
                        if (got < 16) { pos += BLOCK; continue; }
                        for (int p = 0; p < got - 16; p++)
                        {
                            if (BitConverter.ToInt32(buf, p) != Magic) continue;
                            Interlocked.Increment(ref magicHits);
                            int layerCount = BitConverter.ToInt32(buf, p + 4);
                            int num2 = BitConverter.ToInt32(buf, p + 8);
                            if (layerCount < 0 || layerCount > 40 || num2 < 1 || num2 > 300) continue;
                            Interlocked.Increment(ref coarseOk);
                            cand.Add(pos + p);
                        }
                        pos += BLOCK;
                    }
                }
            });

            // 遍2：对候选逐一流式解析（BinaryReader 向后读，失败即停）
            var spheres = new List<DysonSummary>();
            foreach (long off in cand.OrderBy(x => x))
            {
                var s = ParseSphereAt(path, off);
                if (s != null) spheres.Add(s);
            }

            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "dyson-diag.txt"),
                $"{DateTime.Now:HH:mm:ss} magic={magicHits} coarse={coarseOk} ok={spheres.Count}\n");
            foreach (var spx in spheres)
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "dyson-diag.txt"),
                    $"  ball @{spx.Offset} end={spx.EndOffset} len={spx.EndOffset - spx.Offset} layers={spx.Layers.Count} star={spx.StarIndex}\n");

            if (spheres.Count > 0 && starCount > 0)
                MapToStars(path, spheres, starCount, galaxy?.stars);

            return spheres;
        }

        /// <summary>候选偏移处流式解析一个球（1212 后完整 Import 后半）。成功返回并填 Offset/EndOffset。</summary>
        static DysonSummary ParseSphereAt(string path, long off)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                try
                {
                    fs.Seek(off + 4, SeekOrigin.Begin);
                    var s = TryParseSphere(br);
                    if (s == null) return null;
                    s.Offset = off;
                    s.EndOffset = fs.Position;
                    return s;
                }
                catch { return null; }
            }
        }

        /// <summary>流式解析 DysonSphere.Import 后半（layerCount + 层 + rocket + autoNode + nrd + mask）。
        /// nrd 结构异常时回退"无 nrd"版本。任何 EOF/IO 异常 → null。</summary>
        static DysonSummary TryParseSphere(BinaryReader br)
        {
            try
            {
                int layerCount = R32(br);
                if (layerCount < 0 || layerCount > 40) return null;
                int num2 = R32(br);
                if (num2 < 1 || num2 > 300) return null;
                var s = new DysonSummary { LayerCount = layerCount };
                for (int i = 1; i < num2; i++)
                {
                    int layerId = R32(br);
                    if (layerId != 0)
                    {
                        var layer = ParseLayer(br);
                        if (layer == null) return null;
                        s.Layers.Add(layer);
                    }
                }
                // rocket 段
                R32(br);                            // rocketCapacity
                int rocketCursor = R32(br);
                int rocketRecycle = R32(br);
                if (rocketCursor < 0 || rocketRecycle < 0 || rocketRecycle > rocketCursor) return null;
                Skip(br, 92 * Math.Max(0, rocketCursor - 1));  // DysonRocket.Import 定长 92B/条（cursor 可为 0：空球/旧档）
                Skip(br, 4 * rocketRecycle);
                s.RocketCursor = rocketCursor;
                s.RocketRecycle = rocketRecycle;
                // autoNode
                R32(br);                            // autoNodeCount
                int an2 = R32(br);
                if (an2 < 0 || an2 > 100000) return null;
                for (int l = 0; l < an2; l++)
                {
                    if (R32(br) > 0) { R32(br); R32(br); }
                }
                // nrd 段（新档 num>=2 才有）：校验失败 = 旧版球（无 nrd/mask）→ 回退到 autoNode 后结束
                long posAfterAutoNode = br.BaseStream.Position;
                try
                {
                    int nrdCap = R32(br);
                    int nrdCursor = R32(br);
                    int nrdRecycle = R32(br);
                    if (nrdCursor < 1 || nrdRecycle < 0 || nrdRecycle > nrdCursor || nrdCap < 0 || nrdCap > 50000000)
                    {
                        br.BaseStream.Seek(posAfterAutoNode, SeekOrigin.Begin);
                        return s;   // 旧版：无 nrd/mask，球在 autoNode 后结束
                    }
                    Skip(br, 44 * Math.Max(0, nrdCursor - 1));
                    Skip(br, 4 * nrdRecycle);
                    Skip(br, 16);   // mask 段（num>=3）：4×Int32
                }
                catch
                {
                    br.BaseStream.Seek(posAfterAutoNode, SeekOrigin.Begin);
                    return s;   // 旧版回退
                }
                return s;
            }
            catch { return null; }
        }

        /// <summary>流式解析 DysonSphereLayer.Import：层头 + node/frame/shell 三池。</summary>
        static DysonLayerSummary ParseLayer(BinaryReader br)
        {
            try
            {
                var layer = new DysonLayerSummary();
                int num = R32(br);                  // 层版本
                layer.LayerId = R32(br);
                layer.OrbitRadius = RS(br);
                Skip(br, 16);                        // orbitRotation 4f
                if (num >= 1) Skip(br, 16);          // targetOrbitRotation 4f
                RS(br); RS(br);                      // orbitAngularSpeed / currentAngle
                Skip(br, 32);                        // currentRotation + nextRotation 各 4f
                R32(br);                             // drawingGridMode
                if (num >= 2)
                {
                    R32(br);                         // paintingGridMode
                    if (br.ReadByte() != 0)
                    {
                        int cnt = R32(br);
                        Skip(br, cnt * 4);           // Color32×4B
                    }
                }
                // node 段
                R32(br);                             // nodeCapacity
                layer.NodeCursor = R32(br);
                layer.NodeRecycle = R32(br);
                if (layer.NodeCursor < 1 || layer.NodeRecycle < 0 || layer.NodeRecycle > layer.NodeCursor) return null;
                for (int j = 1; j < layer.NodeCursor; j++)
                {
                    int num4 = R32(br);
                    if (num4 != 0)
                    {
                        int sp = ParseNode(br);
                        if (sp < 0) return null;
                        if (sp > 0) layer.NodeBuilt++;
                    }
                }
                Skip(br, 4 * layer.NodeRecycle);
                // frame 段
                R32(br);
                layer.FrameCursor = R32(br);
                layer.FrameRecycle = R32(br);
                if (layer.FrameCursor < 1 || layer.FrameRecycle < 0 || layer.FrameRecycle > layer.FrameCursor) return null;
                for (int l = 1; l < layer.FrameCursor; l++)
                {
                    int num5 = R32(br);
                    if (num5 != 0)
                    {
                        if (ParseFrame(br)) layer.FrameBuilt++;
                    }
                }
                Skip(br, 4 * layer.FrameRecycle);
                // shell 段
                R32(br);
                layer.ShellCursor = R32(br);
                layer.ShellRecycle = R32(br);
                if (layer.ShellCursor < 1 || layer.ShellRecycle < 0 || layer.ShellRecycle > layer.ShellCursor) return null;
                for (int n = 1; n < layer.ShellCursor; n++)
                {
                    int num6 = R32(br);
                    if (num6 != 0)
                    {
                        if (!SkipShell(br)) return null;
                    }
                }
                Skip(br, 4 * layer.ShellRecycle);
                return layer;
            }
            catch { return null; }
        }

        /// <summary>DysonNode.Import：返回 sp（已建 = sp>0），-1 = 异常。</summary>
        static int ParseNode(BinaryReader br)
        {
            try
            {
                int num = R32(br);
                Skip(br, 12);                        // id/protoId/layerId
                Skip(br, 2);                         // use/reserved
                Skip(br, 12);                        // pos 3f
                int sp = R32(br);
                Skip(br, 8);                         // spMax/spOrdered
                if (num >= 3) Skip(br, 4);
                if (num >= 2) Skip(br, 4);
                Skip(br, 4);                         // frameTurn
                if (num >= 1) Skip(br, 4);           // shellTurn
                Skip(br, 4);                         // _spReq
                if (num >= 4) Skip(br, 4);
                if (num >= 5) Skip(br, 4);           // color
                return sp;
            }
            catch { return -1; }
        }

        /// <summary>DysonFrame.Import：返回 true = 已建（spA+spB>0）。</summary>
        static bool ParseFrame(BinaryReader br)
        {
            int num = R32(br);
            Skip(br, 12);                            // id/protoId/layerId
            Skip(br, 1);                             // reserved
            Skip(br, 8);                             // nodeId/nodeId2
            Skip(br, 1);                             // euler
            int spA = R32(br);
            int spB = R32(br);
            Skip(br, 4);                             // spMax
            if (num >= 1) Skip(br, 4);               // color
            return spA + spB > 0;
        }

        /// <summary>DysonShell.Import 三分支完整跳过（num==0 / num==1 / num>=2）。false = 结构异常。</summary>
        static bool SkipShell(BinaryReader br)
        {
            try
            {
                int num = R32(br);
                if (num == 0)
                {
                    Skip(br, 16);                    // id/protoId/layerId/randSeed
                    int n = R32(br);
                    Skip(br, n * 12);                // polygon（3f/点）
                    n = R32(br);
                    Skip(br, n * 4);                 // nodes
                    Skip(br, 8);                     // 空读
                    n = R32(br);
                    Skip(br, n * 12);                // (1) 3f×n
                    n = R32(br);
                    Skip(br, n * 8);                 // (2) 2×Int32/条
                    n = R32(br);
                    Skip(br, n * 4);                 // (3) Int32×n
                    n = R32(br);
                    Skip(br, n * 4);                 // (4) Int32×n
                    n = R32(br);
                    Skip(br, n * 4);                 // (5) Int32×n
                    n = R32(br);
                    Skip(br, n * 4);                 // vertsqOffset
                    n = R32(br);
                    Skip(br, n * 4);                 // nodecps
                    return true;
                }
                if (num == 1)
                {
                    Skip(br, 16);
                    int n = R32(br);
                    Skip(br, n * 12);                // polygon
                    n = R32(br);
                    Skip(br, n * 4);                 // nodes
                    int vc = R32(br);                // vertexCount
                    R32(br);                         // 空读
                    n = R32(br);
                    Skip(br, 8);                     // 1×double
                    Skip(br, n * 8);                 // Int64×n
                    n = R32(br);
                    Skip(br, n * 4);                 // Int16×2/条
                    n = R32(br);
                    Skip(br, vc > 65500 ? n * 4 : n * 2);   // Int32 / UInt16
                    n = R32(br);
                    Skip(br, n * 2);                 // UInt16×n
                    n = R32(br);
                    Skip(br, n * 4);                 // UInt32×n
                    n = R32(br);
                    Skip(br, n * 4);                 // vertsqOffset
                    n = R32(br);
                    Skip(br, n * 4);                 // nodecps
                    return true;
                }
                if (num >= 2)
                {
                    Skip(br, 16);                    // id/protoId/layerId/randSeed
                    if (num >= 3) Skip(br, 4);       // color
                    int n = R32(br);
                    Skip(br, n * 4);                 // nodes
                    Skip(br, 8);                     // radius（double）
                    R32(br);                         // gridScale
                    n = R32(br);
                    Skip(br, n * 24);                // polygon（3×double/条）
                    n = R32(br);
                    Skip(br, n * 24);                // polyn
                    n = R32(br);
                    Skip(br, n * 8);                 // polynu（double）
                    Skip(br, 1);                     // clockwise
                    Skip(br, 3 * 8 * 10);            // 10 组 3×double 向量（center..raydir）
                    int vc = R32(br);                // vertexCount
                    if (vc < 0 || vc > 10000000) return false;
                    // verts(3f)+uvs(2f)+uv2s(2f)+vkeys(4B)+vAdjs(6×2B)+vertAttr(4B) = 48B/顶点
                    Skip(br, vc * 48);
                    n = R32(br);                     // vertsq（Int16）
                    Skip(br, n * 2);
                    n = R32(br);                     // vertsqOffset（Int16 读入 int[]）
                    Skip(br, n * 2);
                    n = R32(br);                     // nodecps（Int32）
                    Skip(br, n * 4);
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>恒星对应（v2，2026-10-21）：只用真球（LayerCount>=3），从段头（count==starCount）顺序走 0/1 链，
        /// 遇 1 按序匹配真球（Offset ≥ importStart 且 swarm 距离合理），用真球 EndOffset 精确跳过 Import。
        /// 已验证：真球 EndOffset 后即下一星 0/1（w1wold3 三球链自洽）。</summary>
        static void MapToStars(string path, List<DysonSummary> spheres, int starCount, StarData[] stars)
        {
            // v3：dysonSpheres 段 0/1=1 = 打开过戴森球控制面板的星系（含只建空层）→ 链上 1 数 = 全部解析球（含空球）
            var reals = spheres.OrderBy(x => x.Offset).ToList();
            if (reals.Count == 0) return;
            long first = reals[0].Offset;
            long lastEnd = reals[reals.Count - 1].EndOffset;   // 链要走到最后一个球结束，buf 必须覆盖全链 + 段尾 0/1
            long wStart = Math.Max(0, first - WINDOW);
            byte[] buf = new byte[lastEnd - wStart + 64];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(wStart, SeekOrigin.Begin);
                int got = 0;
                while (got < buf.Length)
                {
                    int n = fs.Read(buf, got, buf.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
            }
            for (int h = 0; h + 4 <= buf.Length - 4; h += 4)
            {
                if (BitConverter.ToInt32(buf, h) != starCount) continue;
                if (!WalkChain(buf, wStart, h, starCount, reals, stars)) continue;
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "dyson-diag.txt"),
                    $"{DateTime.Now:HH:mm:ss} starMap v3 ok at file {wStart + h} 球数={reals.Count}\n");
                return;
            }
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "dyson-diag.txt"),
                $"{DateTime.Now:HH:mm:ss} starMap v3 FAIL（段头未命中）\n");
        }

        /// <summary>从候选段头顺序走 64 星：0/1 + Import（用真球 EndOffset 精确跳）。全部真球按序命中即成功。</summary>
        static bool WalkChain(byte[] buf, long wStart, int h, int starCount, List<DysonSummary> reals, StarData[] stars)
        {
            int o = h + 4;
            int k = 0;
            for (int si = 0; si < starCount; si++)
            {
                if (o + 4 > buf.Length) return false;
                int v = BitConverter.ToInt32(buf, o);
                o += 4;
                if (v == 0) continue;
                if (v != 1) return false;
                if (k >= reals.Count) return false;
                var sp = reals[k];
                long importStart = wStart + o;
                if (sp.Offset < importStart || sp.Offset - importStart > WINDOW) return false;   // swarm 距离合理
                // swarm 头强校验（淘汰误报段头：importStart 必须是 DysonSphere.Import 起点：num+randSeed+swarm 头）
                if (importStart - wStart + 28 <= buf.Length)
                {
                    int i0 = (int)(importStart - wStart);
                    int num = BitConverter.ToInt32(buf, i0);          // DysonSphere.Import num
                    int seed = BitConverter.ToInt32(buf, i0 + 4);     // randSeed
                    int snum = BitConverter.ToInt32(buf, i0 + 8);     // DysonSwarm.Import num
                    int srand = BitConverter.ToInt32(buf, i0 + 12);   // swarm randSeed
                    int scap = BitConverter.ToInt32(buf, i0 + 16);    // sailCapacity
                    int scur = BitConverter.ToInt32(buf, i0 + 20);    // sailCursor
                    int srec = BitConverter.ToInt32(buf, i0 + 24);    // sailRecycleCursor
                    if (num < 1 || num > 30 || seed == 0) return false;
                    if (snum < 5 || snum > 12 || srand == 0) return false;
                    if (scap < 0 || scap > 5000000 || scur < 0 || scur > scap + 500 || srec < 0 || srec > scur) return false;
                }
                // 可建区间校验：层轨道 ∈ [max(4000, physics×1.5), Round(dysonR×80000/100)×100]（母星 8001 层比 0.71 也能过）
                if (stars != null && si < stars.Length)
                {
                    double defOrbit = stars[si].dysonRadius * 40000.0;
                    double minOrbit = Math.Max(4000.0, stars[si].physicsRadius * 1.5);
                    double maxOrbit = Math.Round(defOrbit * 2 / 100.0) * 100.0;
                    if (!sp.Layers.Any(l => l.OrbitRadius >= minOrbit && l.OrbitRadius <= maxOrbit))
                        return false;
                }
                sp.StarIndex = si;
                sp.StarMapped = true;
                long end = sp.EndOffset;
                if (end - wStart < 0 || end - wStart > buf.Length) return false;
                o = (int)(end - wStart);   // 精确跳 Import → 下一星 0/1
                k++;
            }
            return k == reals.Count;
        }

        // ---- 流式读取原语 ----
        static int R32(BinaryReader br) { return br.ReadInt32(); }
        static float RS(BinaryReader br) { return br.ReadSingle(); }
        static void Skip(BinaryReader br, int n) { if (n < 0) throw new InvalidOperationException("负跳过"); br.BaseStream.Seek(n, SeekOrigin.Current); }
    }
}

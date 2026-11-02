using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DspCore
{
    /// <summary>
    /// DSP 存档解析器 v2（按游戏 GameSave.Load + GameData.Import + ImportScannedData 权威序列）：
    /// 流式读取（不整文件入内存，x86 进程可解析 888MB 大档）+ 多线程分块扫描已扫描行星段。
    /// 头部字段：VFSAVE 魔法 / fileSize / headerVersion / sandbox / peace / 游戏版本 / tick / 时间 /
    /// 缩略图 / account / GameData(gdVersion/patch/account/gameName/gameDesc/tick)。
    /// ScannedPlanet：landPercent + veinBias + birthPoint + birthRP0/1 + VeinGroup[]。
    /// </summary>
    public static class SaveParser
    {
        public static readonly string[] VeinNames = {
            "?", "铁矿", "铜矿", "硅石", "钛石", "石矿", "煤矿", "原油",
            "可燃冰", "金伯利矿石", "分形硅石", "有机晶体", "光栅石", "刺笋结晶", "单极磁石"
        };

        public class SaveDesc
        {
            public string gameName;
            public int galaxyAlgo;
            public int galaxySeed;
            public int starCount;
            public int starNameLCID;      // 0=英文 2052=中文
            public float resourceMultiplier = 1f;
            public int[] savedThemeIds;
            public int headerVersion;
            public int gdVersion;         // GameData.Import 版本
            public int patch;
            public bool isSandboxMode;
            public bool isPeaceMode;
            public int gameMajor, gameMinor, gameRelease, gameBuild;
            public long gameTick;
            public long saveTimeTicks;
            public int screenshotLen;
            public float combatAggressiveness, combatInitialLevel, combatInitialGrowth,
                combatInitialColonize, combatMaxDensity;
            public long searchFrom;      // GameData 段内 gameTick 之后（scanned 段可能出现的起点）
            public bool headerOk;
        }

        public class ScannedPlanet
        {
            public int astroId;
            public float landPercent;
            public float veinBiasX, veinBiasY, veinBiasZ;
            public float birthPointX, birthPointY, birthPointZ;
            public float birthRP0X, birthRP0Y, birthRP0Z;
            public float birthRP1X, birthRP1Y, birthRP1Z;
            public readonly List<VeinGroupRec> groups = new List<VeinGroupRec>();
        }

        public class VeinGroupRec
        {
            public int veinType;
            public float x, y, z;
            public int count;
            public long amount;
        }

        /// <summary>流式解析头部 + GameData 前段（权威序列，含黑雾设置）。失败返回 null（headerOk=false 时为版本不兼容）。</summary>
        public static SaveDesc ParseHeader(Stream s)
        {
            var sd = new SaveDesc { headerOk = false };
            BinaryReader br = null;
            try
            {
                br = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
                // --- GameSave.Load 头部 ---
                char[] magic = br.ReadChars(6);
                if (magic.Length != 6 || magic[0] != 'V' || magic[1] != 'F' || magic[2] != 'S' || magic[3] != 'A' || magic[4] != 'V' || magic[5] != 'E')
                    return null;
                long fileSize = br.ReadInt64();
                sd.headerVersion = br.ReadInt32();
                if (sd.headerVersion < 4) return null;   // 太旧，游戏同样拒绝
                if (sd.headerVersion >= 7)
                {
                    sd.isSandboxMode = br.ReadBoolean();
                    sd.isPeaceMode = br.ReadBoolean();
                }
                sd.gameMajor = br.ReadInt32();
                sd.gameMinor = br.ReadInt32();
                sd.gameRelease = br.ReadInt32();
                if (sd.headerVersion >= 6) sd.gameBuild = br.ReadInt32();
                sd.gameTick = br.ReadInt64();
                sd.saveTimeTicks = br.ReadInt64();
                sd.screenshotLen = br.ReadInt32();
                if (sd.screenshotLen > 0) s.Seek(sd.screenshotLen, SeekOrigin.Current);
                if (sd.headerVersion >= 5)
                {
                    ReadAccount(br);
                    br.ReadUInt64();   // clusterGeneration
                }
                // --- GameData.Import 前段 ---
                sd.gdVersion = br.ReadInt32();
                if (sd.gdVersion >= 4) sd.patch = br.ReadInt32();
                if (sd.gdVersion >= 3) ReadAccount(br);
                sd.gameName = br.ReadString();
                // --- GameDesc.Import ---
                int gd = br.ReadInt32();
                if (gd >= 3) br.ReadInt64();               // creationTime
                if (gd >= 5)
                {
                    if (gd >= 6) { br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); br.ReadInt32(); }
                    else br.ReadInt32();
                }
                sd.galaxyAlgo = br.ReadInt32();
                sd.galaxySeed = br.ReadInt32();
                sd.starCount = br.ReadInt32();
                if (gd >= 10) sd.starNameLCID = br.ReadInt32();
                else sd.starNameLCID = 0;
                br.ReadInt32();                            // playerProto
                if (gd >= 2) sd.resourceMultiplier = br.ReadSingle();
                if (gd >= 1)
                {
                    int n = br.ReadInt32();
                    sd.savedThemeIds = new int[n];
                    for (int i = 0; i < n; i++) sd.savedThemeIds[i] = br.ReadInt32();
                }
                if (gd >= 4) br.ReadBoolean();             // achievementEnable
                if (gd >= 7)
                {
                    br.ReadBoolean();                      // isPeaceMode
                    br.ReadBoolean();                      // isSandboxMode
                }
                if (gd >= 8)
                {
                    br.ReadInt32();                        // combatSettings ver
                    sd.combatAggressiveness = br.ReadSingle();
                    sd.combatInitialLevel = br.ReadSingle();
                    sd.combatInitialGrowth = br.ReadSingle();
                    sd.combatInitialColonize = br.ReadSingle();
                    sd.combatMaxDensity = br.ReadSingle();
                    br.ReadSingle(); br.ReadSingle(); br.ReadSingle(); br.ReadSingle();
                }
                if (gd >= 9) br.ReadInt32();               // goalLevel
                sd.gameTick = br.ReadInt64();              // GameData 内 tick（与头部一致）
                sd.searchFrom = s.Position;
                sd.headerOk = true;
                return sd;
            }
            catch
            {
                return sd.headerOk ? sd : null;
            }
            finally
            {
                if (br != null) br.Dispose();
            }
        }

        static void ReadAccount(BinaryReader br)
        {
            br.ReadInt32();   // 0
            br.ReadInt32();   // platform
            br.ReadUInt64();  // userId
            br.ReadString();  // userName
        }

        /// <summary>多线程分块扫描已扫描行星段（ImportScannedData 布局，组合校验）。线程内独立 FileStream。</summary>
        public static List<ScannedPlanet> FindScannedPlanets(string path, long searchFrom)
        {
            long len;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                len = fs.Length;
            int threads = Math.Max(2, Math.Min(Environment.ProcessorCount, 16));
            long from = 0;   // 全文件扫描（scanned 段布局存在变体，searchFrom 起点会漏；v1 验证过 198/200 全扫）
            long span = len - from;
            const long OVERLAP = 10240;   // 行星段最长 64+300×32≈9664B，重叠必须覆盖（跨块段不丢）
            const long BLOCK = 16L * 1024 * 1024;   // 固定块流式（x86 进程内存安全：1.7GB 档不再 OOM）
            var bag = new ConcurrentBag<ScannedPlanet>();
            Parallel.For(0, threads, ti =>
            {
                long start = from + ti * (span / threads);
                long end = Math.Min(len, from + (ti + 1) * (span / threads));
                if (end <= start) return;
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                try
                {
                    byte[] buf = new byte[BLOCK + OVERLAP];
                    long pos = start;
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
                        if (got > 0) ScanChunk(buf, pos, bag);
                        pos += BLOCK;   // 重叠区供下一块补跨块记录
                    }
                }
                finally
                {
                    fs.Dispose();
                }
            });
            return bag
                .OrderBy(p => p.astroId)
                .GroupBy(p => p.astroId)
                .Select(g => g.First())
                .ToList();
        }

        static void ScanChunk(byte[] d, long baseOff, ConcurrentBag<ScannedPlanet> bag)
        {
            for (int p = 0; p < d.Length - 96; p++)
            {
                if (d[p + 4] != 0 || d[p + 5] != 0 || d[p + 6] != 0 || d[p + 7] != 0) continue;
                int astro = BitConverter.ToInt32(d, p);
                if (astro < 100 || astro >= 10000 || astro % 100 == 0) continue;
                float land = BitConverter.ToSingle(d, p + 8);
                if (!(land > 0f)) continue;   // land<=0 非法；全陆=1.0、水世界≈0.003 均合法（勿加上限）
                int gc = BitConverter.ToInt32(d, p + 60);
                if (gc < 2 || gc > 300) continue;
                var pl = new ScannedPlanet
                {
                    astroId = astro, landPercent = land,
                    veinBiasX = BitConverter.ToSingle(d, p + 12), veinBiasY = BitConverter.ToSingle(d, p + 16), veinBiasZ = BitConverter.ToSingle(d, p + 20),
                    birthPointX = BitConverter.ToSingle(d, p + 24), birthPointY = BitConverter.ToSingle(d, p + 28), birthPointZ = BitConverter.ToSingle(d, p + 32),
                    birthRP0X = BitConverter.ToSingle(d, p + 36), birthRP0Y = BitConverter.ToSingle(d, p + 40), birthRP0Z = BitConverter.ToSingle(d, p + 44),
                    birthRP1X = BitConverter.ToSingle(d, p + 48), birthRP1Y = BitConverter.ToSingle(d, p + 52), birthRP1Z = BitConverter.ToSingle(d, p + 56),
                };
                int off = p + 64;
                bool ok = true;
                for (int i = 0; i < gc - 1; i++)
                {
                    if (off + 32 > d.Length) { ok = false; break; }
                    int z = BitConverter.ToInt32(d, off);
                    int vt = BitConverter.ToInt32(d, off + 4);
                    if (z != 0 || vt < 1 || vt > 14) { ok = false; break; }
                    int cnt = BitConverter.ToInt32(d, off + 20);
                    long amt = BitConverter.ToInt64(d, off + 24);
                    if (cnt < 1 || amt <= 0) { ok = false; break; }
                    pl.groups.Add(new VeinGroupRec
                    {
                        veinType = vt,
                        x = BitConverter.ToSingle(d, off + 8),
                        y = BitConverter.ToSingle(d, off + 12),
                        z = BitConverter.ToSingle(d, off + 16),
                        count = cnt,
                        amount = amt,
                    });
                    off += 32;
                }
                if (!ok || pl.groups.Count == 0) continue;
                bag.Add(pl);
                // 不跳过：段尾布局存在变体，跳过会漏段；分块全扫 + astroId 去重足够
            }
        }

        /// <summary>玩家自定义的星球/恒星名（GamePrefsData.astroNameOverride：Dictionary&lt;int,string&gt;）。
        /// 序列化：count(int32) + (astroId int32, name = 7-bit 变长长度 + UTF8)×N；
        /// 恒星 key = starId×100，行星 key = planetId（GamePrefsData.cs:307-434：displayName 优先 overrideName）。
        /// 定位：全文件扫候选头 + 严格校验（每个 astroId 合法且名字为可打印 UTF8），首个通过即返回。</summary>
        public static Dictionary<int, string> FindAstroNameOverrides(string path, ISet<int> validAstro = null)
        {
            var result = new Dictionary<int, string>();
            byte[] data = File.ReadAllBytes(path);
            int n = data.Length;
            for (int off = 0; off + 8 < n; off++)
            {
                int count = BitConverter.ToInt32(data, off);
                if (count < 1 || count > 500) continue;
                int o = off + 4;
                var dict = new Dictionary<int, string>();
                bool ok = true;
                for (int i = 0; i < count; i++)
                {
                    if (o + 5 > n) { ok = false; break; }
                    int key = BitConverter.ToInt32(data, o); o += 4;
                    if (key <= 0 || key > 99999) { ok = false; break; }
                    int len = 0, shift = 0; bool bad = false;
                    while (true)
                    {
                        if (o >= n) { bad = true; break; }
                        byte b = data[o++];
                        len |= (b & 0x7F) << shift;
                        if ((b & 0x80) == 0) break;
                        shift += 7;
                        if (shift > 14) { bad = true; break; }
                    }
                    if (bad || len < 1 || len > 64 || o + len > n) { ok = false; break; }
                    string name = Encoding.UTF8.GetString(data, o, len);
                    o += len;
                    bool printable = name.Length > 0;
                    foreach (char c in name)
                        if (char.IsControl(c)) { printable = false; break; }
                    if (!printable) { ok = false; break; }
                    dict[key] = name;
                }
                if (!ok || dict.Count != count) continue;
                if (validAstro != null && validAstro.Count > 0)
                    foreach (var kv in dict)
                        if (!validAstro.Contains(kv.Key))
                        {
                            bool isStar = kv.Key % 100 == 0 && validAstro.Contains(kv.Key / 100 * 100);
                            if (!isStar) { ok = false; break; }
                        }
                if (!ok) continue;
                return dict;
            }
            return result;
        }
    }
}

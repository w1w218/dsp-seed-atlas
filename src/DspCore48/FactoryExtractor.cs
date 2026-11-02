using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DspCore
{
    /// <summary>工厂段解析结果（一个行星工厂）。</summary>
    public class FactorySummary
    {
        public int PlanetId;
        public int Version;
        public int EntityCapacity;
        public int EntityCursor;
        public int EntityRecycleCursor;
        /// <summary>建筑检测（B）：entityCursor - entityRecycleCursor - 1，>0 即建过建筑。</summary>
        public int EntityCount => Math.Max(0, EntityCursor - EntityRecycleCursor - 1);
        /// <summary>地面基地数（C）：enemySystem.bases.cursor - 1。</summary>
        public int GroundBases;
        /// <summary>敌对单位数（units.cursor - 1，游戏内"敌对单位"计数）。</summary>
        public int GroundUnits;
        public int HashBucketCount;
        public long Offset;
        public long EndOffset;
        public Dictionary<short, int> ProtoIdCount = new Dictionary<short, int>();

        // 矿脉（工厂 veinPool = 运行时当前值，已开采会减少；与 galaxy 已扫描段的「初始储量」不同）
        public long[] VeinAmount = new long[15];        // 当前剩余
        public long[] VeinPlanned = new long[15];       // 有采矿机覆盖（已规划采集）
        public long[] VeinUnplanned = new long[15];     // 无采矿机（未规划采集，有开采潜力）
        public int[] VeinGroupCount = new int[15];      // 组数（按 groupIndex 去重）
        public HashSet<(int, int)> VeinGroupKeys = new HashSet<(int, int)>();
        public string Error;
        public override string ToString() =>
            $"factory@{Offset}: planet={PlanetId} ver={Version} entities={EntityCount}({EntityCursor}/{EntityRecycleCursor}) bases={GroundBases}";
    }

    /// <summary>
    /// 工厂段解析（§7.6 A 项：建筑检测 B + 采集站 D + 地面基地 C 一次拿）。
    /// 依据 PlanetFactory.Import（decomp-full\PlanetFactory.cs:8495）顺序结构：
    /// 前缀 → 实体池 → prebuild/craft/enemy/vege/vein/ruin 池 → cargoContainer/cargoTraffic/factoryStorage
    /// → powerSystem → factorySystem → enemySystem（目标）→ 后略。
    /// 段起点特征扫描：factoryCount + 前缀链 + 深度校验（§7.6 结论）。
    /// 组件 Import 逐字段对照 decomp-full 源码翻译（只读字节不建对象）。
    /// </summary>
    public static class FactoryExtractor
    {
        private const int MAX_FACTORIES = 64;
        private static readonly int[] BucketCandidates = { 362, 140, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072 };
        /// <summary>定位阶段：误报候选前缀探测预算。TryParsePrefix 含 hash 段探测——大工厂 hash 段可达 1MB+（103=1.09MB），8MB 安全；误报候选 num 巨大时照样快速耗尽被拒。</summary>
        private const long LOCATE_BUDGET = 8L << 20;
        /// <summary>顺序解析：真工厂全链预算（含实体池/六池/工厂系统；大工厂几十 MB，120MB 安全；顺序解析无误报，预算不浪费）。</summary>
        private const long PARSE_BUDGET = 120L << 20;

        // ============ 读取原语 ============

        private sealed class R
        {
            private readonly byte[] b;
            public int o;
            /// <summary>读取预算（字节）：超限抛 EndOfStreamException，防误报候选巨循环。</summary>
            public long Budget = long.MaxValue;
            private long _read;
            public R(byte[] data, int start) { b = data; o = start; }
            public bool Has(int n) => o >= 0 && o + n <= b.Length;
            private void Charge(int n)
            {
                _read += n;
                if (_read > Budget) throw new EndOfStreamException();
            }
            public int I32()
            {
                if (!Has(4)) throw new EndOfStreamException();
                Charge(4);
                int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
                o += 4; return v;
            }
            public short I16()
            {
                if (!Has(2)) throw new EndOfStreamException();
                Charge(2);
                short v = (short)(b[o] | (b[o + 1] << 8));
                o += 2; return v;
            }
            public byte U8() { if (!Has(1)) throw new EndOfStreamException(); Charge(1); return b[o++]; }
            public bool Bool() => U8() != 0;
            public uint U32() => (uint)I32();
            public long I64()
            {
                if (!Has(8)) throw new EndOfStreamException();
                Charge(8);
                long v = (long)BitConverter.ToInt64(b, o); o += 8; return v;
            }
            public float F32()
            {
                if (!Has(4)) throw new EndOfStreamException();
                Charge(4);
                float v = BitConverter.ToSingle(b, o); o += 4; return v;
            }
            public double F64()
            {
                if (!Has(8)) throw new EndOfStreamException();
                Charge(8);
                double v = BitConverter.ToDouble(b, o); o += 8; return v;
            }
            public int U16()
            {
                if (!Has(2)) throw new EndOfStreamException();
                Charge(2);
                int v = b[o] | (b[o + 1] << 8); o += 2; return v;
            }
            /// <summary>ReadFlexibleInt（IOHelper.cs:169）。</summary>
            public int Flex()
            {
                byte x = U8();
                if (x == 0 || x > 4) return x;
                switch (x)
                {
                    case 2: return U16();
                    case 3: return U8() | (U8() << 8) | (U8() << 16);
                    case 1: return U8();
                    default: return I32();
                }
            }
            public void Skip(int n) { if (n < 0 || !Has(n)) throw new EndOfStreamException(); Charge(n); o += n; }
            /// <summary>C# BinaryReader.ReadString（7-bit 变长长度 + UTF8）。</summary>
            public void S7()
            {
                int len = 0; int shift = 0; byte bb;
                do { bb = U8(); len |= (bb & 0x7F) << shift; shift += 7; if (shift > 35) throw new EndOfStreamException(); }
                while ((bb & 0x80) != 0);
                Skip(len);
            }
        }

        /// <summary>ReadCId（EntityData.cs:197）：flag 字节 + [非0: 24bit id]，校验和式递减。</summary>
        private static bool ReadCId(R r, ref int sum)
        {
            if (r.U8() == 0) return true;
            int cid = r.U8() | (r.U8() << 8) | (r.U8() << 16);
            sum -= cid;
            return sum > 0;
        }

        // ============ 前缀（PlanetFactory.Import 8495-8543） ============

        private static bool TryParsePrefix(R r, ISet<int> validPlanetIds, out FactorySummary fs, StringBuilder fail = null)
        {
            fs = new FactorySummary();
            int ver = r.I32();
            if (ver < 1 || ver > 100) { fail?.Append("ver"); return false; }
            fs.Version = ver;
            int pid = r.I32();
            if (pid < 100 || pid > 100000) { fail?.Append("pid"); return false; }
            if (validPlanetIds != null && validPlanetIds.Count > 0 && !validPlanetIds.Contains(pid)) { fail?.Append("pidv"); return false; }
            fs.PlanetId = pid;
            if (ver >= 5)
            {
                int s1 = r.I32(), s2 = r.I32();
                if (s1 < -1_000_000 || s1 > 1_000_000 || s2 < -1_000_000 || s2 > 1_000_000) { fail?.Append("style"); return false; }
                r.I32();
            }
            // planet.ImportRuntime（PlanetData.cs:1713）
            int modLen = r.I32();
            if (modLen < 0 || modLen > 2_000_000) { fail?.Append("modlen"); return false; }
            r.Skip(modLen);
            int n1 = r.I32();
            if (n1 < 0 || n1 > 100000) { fail?.Append("n1"); return false; }
            r.Skip(n1 * 8);
            int n2 = r.I32();
            if (n2 < 0 || n2 > 1_000_000) { fail?.Append("n2"); return false; }
            // PlanetData.ImportRuntime（:1713）num2 循环 = Int32 + 3×Single + Int32 + Int64 = 28B/项
            r.Skip(n2 * 28);
            if (ver >= 3)
            {
                if (r.U8() > 1) { fail?.Append("landed"); return false; } // landed
            }
            // hashSystem × 2（HashSystem.Import：bucketCount 来自游戏静态资源，枚举候选）
            int bc = -1; int after = 0;
            foreach (int cand in BucketCandidates)
            {
                if (TryHashProbe(r, cand, out after)) { bc = cand; break; }
            }
            if (bc < 0) { fail?.Append("hash"); return false; }
            fs.HashBucketCount = bc;
            r.o = after;
            if (ver >= 4)
            {
                int cap = r.I32();
                int cur = r.I32();
                int rec = r.I32();
                if (cap < 0 || cap > 50_000_000) { fail?.Append("cap"); return false; }
                if (cur < 0 || cur > cap + 1) { fail?.Append("cur"); return false; }
                if (rec < 0 || rec > cap + 1) { fail?.Append("rec"); return false; }
                fs.EntityCapacity = cap; fs.EntityCursor = cur; fs.EntityRecycleCursor = rec;
                // 注：r.o 停在 cursor 后 = 实体池数据起点；SkipEntityPool 直接使用 fs 三 cursor，不重读
            }
            return true;
        }

        private static bool TryHashProbe(R r, int bucketCount, out int after)
        {
            after = 0;
            int save = r.o;
            try
            {
                int num = r.I32();
                // 上限从 10 万放宽到 500 万：巨型基地 hashPool 可达 18.5 万条（6001 现场 185344），
                // 旧上限直接判失败 → 回退到错误 bc（140）→ 三 cursor 读成垃圾（cur=18）。
                if (num < 0 || num > 5_000_000) { r.o = save; return false; }
                r.Skip(num * 8);
                r.Skip((bucketCount + 1) * 4 + bucketCount * 8);
                int num2 = r.I32();
                if (num2 < 0 || num2 > 5_000_000) { r.o = save; return false; }
                r.Skip(num2 * 8);
                r.Skip((bucketCount + 1) * 4 + bucketCount * 8);
                after = r.o;
                return true;
            }
            catch (EndOfStreamException)
            {
                r.o = save;
                return false;
            }
        }

        // ============ 实体池（EntityData.Import 两分支） ============

        private static short SkipEntity(R r, out bool alive)
        {
            alive = false;
            byte ver = r.U8();
            if (ver == 0)
            {
                int id = r.I32();
                short proto = r.I16();
                r.I16();
                r.Skip(7 * 4);
                r.Skip(18 * 4);
                alive = id > 0;
                return proto;
            }
            int pid = r.I32();
            if (pid <= 0) return 0;
            alive = true;
            short protoId = (short)(r.U8() | (r.U8() << 8));
            r.Skip(30);
            if (ver >= 10) r.F32();
            if (ver >= 11) r.U8();
            else if (ver >= 8) r.Bool();
            if (ver >= 7) { r.U32(); r.I32(); }
            int sum = r.I32();
            if (sum > 0)
            {
                if (!ReadCId(r, ref sum)) return protoId;      // beltId
                if (!ReadCId(r, ref sum)) return protoId;      // powerConId
                if (!ReadCId(r, ref sum)) return protoId;      // inserterId
                if (!ReadCId(r, ref sum)) return protoId;      // assemblerId
                if (!ReadCId(r, ref sum)) return protoId;      // labId
                if (!ReadCId(r, ref sum)) return protoId;      // powerNodeId
                if (!ReadCId(r, ref sum)) return protoId;      // powerGenId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // combatStatId
                if (ver >= 9) { if (!ReadCId(r, ref sum)) return protoId; } // constructStatId
                if (!ReadCId(r, ref sum)) return protoId;      // fractionatorId
                if (!ReadCId(r, ref sum)) return protoId;      // storageId
                if (!ReadCId(r, ref sum)) return protoId;      // tankId
                if (!ReadCId(r, ref sum)) return protoId;      // splitterId
                if (!ReadCId(r, ref sum)) return protoId;      // ejectorId
                if (!ReadCId(r, ref sum)) return protoId;      // minerId
                if (!ReadCId(r, ref sum)) return protoId;      // siloId
                if (!ReadCId(r, ref sum)) return protoId;      // stationId
                if (ver >= 6) { if (!ReadCId(r, ref sum)) return protoId; } // dispenserId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // turretId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // beaconId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // fieldGenId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // battleBaseId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // constructionModuleId
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // combatModuleId
                if (!ReadCId(r, ref sum)) return protoId;      // powerAccId
                if (!ReadCId(r, ref sum)) return protoId;      // powerExcId
                if (ver >= 4) { if (!ReadCId(r, ref sum)) return protoId; } // warningId
                if (ver >= 2) { if (!ReadCId(r, ref sum)) return protoId; } // monitorId
                if (ver >= 3) { if (!ReadCId(r, ref sum)) return protoId; } // speakerId
                if (ver >= 5)
                {
                    if (!ReadCId(r, ref sum)) return protoId;  // spraycoaterId
                    if (!ReadCId(r, ref sum)) return protoId;  // pilerId
                }
                if (ver >= 7) { if (!ReadCId(r, ref sum)) return protoId; } // extraInfoId
                if (ver >= 12) { if (!ReadCId(r, ref sum)) return protoId; } // markerId
            }
            return protoId;
        }

        private static void CountProto(FactorySummary fs, short proto)
        {
            if (proto <= 0) return;
            if (fs.ProtoIdCount.TryGetValue(proto, out int c)) fs.ProtoIdCount[proto] = c + 1;
            else fs.ProtoIdCount[proto] = 1;
        }

        private static bool SkipEntityPool(R r, FactorySummary fs)
        {
            int ver = fs.Version;
            // 三 cursor 由 TryParsePrefix 已读（r.o 已停在实体池数据起点），直接用
            int cap = fs.EntityCapacity;
            int cur = fs.EntityCursor;
            int rec = fs.EntityRecycleCursor;
            var alive = new bool[cur];
            if (ver <= 8)
            {
                for (int i = 1; i < cur; i++)
                {
                    short proto = SkipEntity(r, out bool a);
                    if (a) CountProto(fs, proto);
                    alive[i] = a;
                    if (a)
                    {
                        // entityAnimPool[i]：time/prepare/working/state/power
                        r.F32(); r.F32(); r.F32(); r.U32(); r.F32();
                        // entitySignPool[i]
                        int signType = r.U8(); int iconType = r.U8();
                        int iconId0 = r.U16();
                        if (iconType >= 128) r.F32();
                        // entityConnPool[j]（16 条 flag）
                        for (int j = 0; j < 16; j++) { if (r.U8() != 0) r.Skip(4); }
                    }
                }
            }
            else
            {
                for (int i = 1; i < cur; i++)
                {
                    short proto = SkipEntity(r, out bool a);
                    if (a) CountProto(fs, proto);
                    alive[i] = a;
                }
                // entityAnimPool：ReadMassive（5 字段 × cur）
                r.Skip(20 * cur);
                for (int l = 1; l < cur; l++)
                {
                    if (!alive[l]) continue;
                    int signType = r.U8(); int iconType = r.U8();
                    int iconId0 = r.U16();
                    if (iconType >= 128) r.F32();
                    r.Skip(16); // UnsafeIO.Read(ref entitySignPool[l], 40, 16)
                }
                for (int m = 16; m < cur * 16; m++)
                {
                    if (!alive[m / 16]) continue;
                    if (r.U8() != 0) r.Skip(4);
                }
            }
            r.Skip(rec * 4);
            return true;
        }

        // ============ 六池（prebuild/craft/enemy/vege/vein/ruin） ============

        private static bool SkipPrebuild(R r)
        {
            int ver = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            if (cur < 0 || cur > 2_000_000 || rec < 0 || rec > 2_000_000) return false;
            for (int i = 1; i < cur; i++)
            {
                int start = r.o;
                try
                {
                    int num = r.U8();
                    int id = r.I32();
                    if (num >= 1 && id == 0) continue;
                    r.I16(); r.I16();
                    if (num >= 4) r.I32();
                    r.Skip(7 * 4);
                    r.Skip(7 * 4);
                    if (num >= 6) r.F32();
                    if (num >= 4) { r.U32(); r.I32(); }
                    if (num >= 3) r.Bool();
                    r.I32();
                    if (num >= 5) { r.I32(); r.F32(); r.Bool(); }
                    r.I16(); r.I16();
                    r.I32(); r.I32();
                    if (num >= 2) r.I32();
                    if (num >= 4) { r.I32(); r.Bool(); }
                    int paramCount = r.I32();
                    if (paramCount < 0 || paramCount > 1000) { Console.WriteLine($"  [prebuild] i={i}/{cur} paramCount={paramCount} num={num} id={id} @0x{r.o - 4:X8}"); return false; }
                    for (int j = 0; j < paramCount; j++) r.I32();
                    if (num >= 7)
                    {
                        // content 字符串：PrebuildData.Export 在 content 为空时直接 return，后面无字段
                        if (r.I32() > 0) r.S7();
                    }
                    // 非空元素之后还有 16 个连接池标志（PlanetFactory.Export 8349-8363）：
                    // prebuildConnPool[m*16 .. m*16+16]：0 → 1 字节；非 0 → 1 字节 1 + int32。
                    // 漏跳会让下一个元素错位（6002 现场：第 7019 个非空元素后差 24 字节）。
                    for (int c = 0; c < 16; c++) { if (r.U8() != 0) r.Skip(4); }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [prebuild] i={i}/{cur} @0x{start:X8} {ex.GetType().Name}");
                    throw;
                }
            }
            r.Skip(rec * 4);
            return true;
        }

        private static bool SkipCraft(R r)
        {
            int num = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            if (cur < 0 || cur > 2_000_000 || rec < 0 || rec > 2_000_000) return false;
            for (int i = 1; i < cur; i++)
            {
                int start = r.o;
                try
                {
                    int v = r.U8();
                    int id = r.I32();
                    if (id > 0)
                    {
                        r.I16(); r.I16(); r.I32();
                        if (v >= 1) r.I32(); else r.I16();
                        r.I16();
                        r.U8();
                        bool dyn = r.Bool();
                        bool isSpace = r.Bool();
                        r.U8();
                        if (isSpace) r.Skip(24); else r.Skip(12);
                        r.Skip(16);
                        if (dyn) r.Skip(12);
                        r.U32(); r.I32();
                        r.I32(); r.I32(); r.I32(); // combatStatId/droneId/fleetId
                        r.Flex();                  // unitId
                        if (v >= 2)
                        {
                            for (int k = 0; k < 22; k++) r.Flex();
                        }
                        if (v >= 3)
                        {
                            r.Flex(); r.Flex();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [craft] i={i}/{cur} @0x{start:X8} {ex.GetType().Name}");
                    throw;
                }
            }
            r.Skip(rec * 4);
            r.Skip(20 * (cur - 1));   // craftAnimPool（循环 1..cursor-1）
            return true;
        }

        private static bool SkipEnemy(R r)
        {
            int num = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            if (cur < 0 || cur > 2_000_000 || rec < 0 || rec > 2_000_000) return false;
            for (int i = 1; i < cur; i++)
            {
                r.U8();
                int id = r.I32();
                if (id <= 0) continue;
                r.I16(); r.I16(); r.I32(); r.I32();
                r.I16(); r.I16();
                bool dyn = r.Bool();
                bool isSpace = r.Bool();
                r.Bool();
                r.U8();
                if (isSpace) r.Skip(24); else r.Skip(12);
                r.Skip(16);
                if (dyn) r.Skip(12);
                r.U32(); r.I32(); r.I32();
                r.U16();
                if (!isSpace)
                {
                    if (!dyn) { for (int k = 0; k < 5; k++) r.U16(); }
                }
                else if (!dyn)
                {
                    for (int k = 0; k < 6; k++) r.U16();
                }
                else
                {
                    r.U16(); r.U16();
                }
                r.Flex();
            }
            r.Skip(rec * 4);
            r.Skip(20 * (cur - 1));   // enemyAnimPool
            return true;
        }

        private static bool SkipVege(R r)
        {
            int num = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            if (cur < 0 || cur > 2_000_000 || rec < 0 || rec > 2_000_000) return false;
            for (int i = 1; i < cur; i++)
            {
                int v = r.U8();
                r.I32();
                r.I16(); r.I16();
                if (v >= 1) { r.U32(); r.I32(); r.I32(); }
                else { r.I16(); }
                r.Skip(10 * 4); // pos3 + rot4 + scl3
                if (v >= 2) r.U8();
            }
            r.Skip(rec * 4);
            return true;
        }

        private static bool SkipVein(R r, FactorySummary fs = null)
        {
            int num = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            if (cur < 0 || cur > 2_000_000 || rec < 0 || rec > 2_000_000) return false;
            for (int i = 1; i < cur; i++)
            {
                int v = r.U8();
                r.I32();                       // id
                int vt = r.I16();              // type（EVeinType 1~14）
                r.I16();                       // modelIndex
                int gi = r.I16();              // groupIndex
                int amount = r.I32();          // 当前剩余
                r.I32();                       // productId
                r.Skip(12);                    // pos
                if (v >= 1) r.I32();           // combatStatId
                int minerCount = r.I32();      // 采矿机数（0 = 未规划采集）
                r.I32(); r.I32(); r.I32(); r.I32();   // minerId0-3
                if (v >= 1) r.I32();           // hashAddress
                if (fs != null && vt >= 1 && vt <= 14)
                {
                    fs.VeinAmount[vt] += amount;
                    if (minerCount == 0) fs.VeinUnplanned[vt] += amount;
                    else fs.VeinPlanned[vt] += amount;
                    if (fs.VeinGroupKeys.Add((vt, gi))) fs.VeinGroupCount[vt]++;
                }
            }
            r.Skip(rec * 4);
            r.Skip(20 * (cur - 1));   // veinAnimPool
            return true;
        }

        private static bool SkipRuin(R r)
        {
            int num = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            if (cur < 0 || cur > 2_000_000 || rec < 0 || rec > 2_000_000) return false;
            for (int i = 1; i < cur; i++)
            {
                int v = r.U8();
                int id = r.I32();
                if (id > 0)
                {
                    r.I16(); r.I32();
                    r.Skip(28);
                    if (v >= 1) r.I32();
                }
            }
            r.Skip(rec * 4);
            return true;
        }

        // ============ CargoContainer / CargoTraffic / FactoryStorage ============

        private static bool SkipCargoContainer(R r)
        {
            int num = r.I32();
            int cap = r.I32();
            int cur = r.I32();
            int rec = r.I32();
            r.I32();
            if (cap < 0 || cap > 20_000_000 || cur < 0 || cur > cap + 1) return false;
            if (num == 0)
            {
                for (int i = 0; i < cur; i++) r.Skip(7 * 4);
            }
            else
            {
                for (int j = 0; j < cur; j++)
                {
                    r.I16();
                    if (num >= 2) { r.U8(); r.U8(); }
                }
            }
            r.Skip(cap * 4); // recycleIds（ReadInt32Array(poolCapacity)）
            return true;
        }

        private static void SkipBelt(R r)
        {
            // BeltComponent.Import（decomp-full\BeltComponent.cs:153）：
            //   新分支：ReadByte>=1 → id=FlexibleInt；id!=0 → entityId Flex + speed Byte + 8×Flex
            //   老分支：ReadByte + ReadInt16 + 12×Int32（id..rightInputId）
            // 旧实现误把 id 读成 U8()+Flex()+I32()，每条错多个字节 → 大量传送带工厂必错位。
            if (r.U8() >= 1)
            {
                if (r.Flex() != 0)
                {
                    r.Flex();            // entityId
                    r.U8();              // speed
                    for (int i = 0; i < 8; i++) r.Flex();   // segPathId,segIndex,segPivotOffset,segLength,outputId,backInputId,leftInputId,rightInputId
                }
            }
            else
            {
                r.U8(); r.I16();
                r.Skip(12 * 4);          // id..rightInputId
            }
        }

        private static void SkipSplitter(R r)
        {
            int num = r.I32();   // 版本（SplitterComponent.cs:98 第一字段）
            r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32();
            r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); // id..output3（14 个）
            r.Bool(); r.Bool();
            r.I32();
            if (num >= 1) { r.U8(); r.I16(); }
            if (num >= 2) { r.I32(); r.I32(); }
        }

        private static void SkipCargoPath(R r)
        {
            int num = r.I32();
            r.I32();
            int cap = r.I32();
            int bufferLength = r.I32();
            r.I32();
            int chunkCount = r.I32();
            r.I32();
            r.Bool();
            r.I32(); r.I32();
            int num2 = r.I32();
            int num3 = r.I32();
            r.Skip(bufferLength);
            r.Skip(chunkCount * 3 * 4);
            if (num >= 1)
            {
                r.Skip(bufferLength * 12);
                r.Skip(bufferLength * 16);
            }
            else
            {
                r.Skip(bufferLength * 28);
            }
            r.Skip(num2 * 4);
            r.Skip(num3 * 4);
        }

        private static void SkipMonitor(R r)
        {
            // MonitorComponent.Import（decomp-full\MonitorComponent.cs:151）逐字段。
            // 旧实现缺 offset/数组段、多读 U16，有 monitor 的工厂必错位。
            int num = r.I32();
            r.I32(); r.I32(); r.I32(); r.I32(); r.I32();   // id, entityId, pcId, speakerId, targetBeltId
            r.U8();                                        // offset (SByte)
            if (num >= 1) r.I32(); else r.U16();           // targetCargoBytes
            int periodTickCount = r.U16();                 // periodTickCount
            r.U16();                                       // prewarmSampleTick
            r.Flex(); r.Flex(); r.Flex();                  // lastCargoId, formerCargoId, lastCargoOffset
            if (num >= 1) r.U8();                          // lastCargoStack
            r.Skip(periodTickCount);                       // cargoBytesArray (sbyte[periodTickCount])
            for (int i = 0; i < periodTickCount; i++) r.Flex();  // periodCargoBytesArray
            r.Flex();                                      // totalCargoBytes
            r.U8(); r.U8(); r.U8();                        // passColorId, failColorId, alarmMode
            r.I32();                                       // cargoFilter
            r.U8();                                        // passOperator
            r.Flex(); r.Flex();                            // systemWarningMode, systemWarningSignalId
            if (num >= 2) { r.U8(); r.F32(); }             // spawnItemOperator, spawnItemAccumulator
            if (num >= 3) r.I32();                         // digitalSignalId
        }

        private static void SkipSpraycoater(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32();
            r.Flex(); r.Flex(); r.Flex(); r.Flex(); r.Flex(); r.Flex();
            if (num >= 1) r.Flex();
            r.Flex();
            if (num >= 2) r.Bool();
            r.Flex();
        }

        private static void SkipPiler(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32();
            r.U8();
            r.Flex(); r.Flex();
            r.I16(); r.U8(); r.U8();
            if (num >= 1) { r.I16(); r.U8(); r.U8(); }
            r.U8();
            r.I32();
        }

        private static bool SkipCargoTraffic(R r)
        {
            int num = r.I32();
            int beltCur = r.I32();
            int beltCap = r.I32();
            int beltRec = r.I32();
            int splitCur = r.I32();
            int splitCap = r.I32();
            int splitRec = r.I32();
            int pathCur = r.I32();
            int pathCap = r.I32();
            int pathRec = r.I32();
            if (beltCap < 0 || beltCap > 20_000_000 || splitCap < 0 || splitCap > 20_000_000 || pathCap < 0 || pathCap > 20_000_000 ||
                beltCur < 1 || beltCur > beltCap + 1 || splitCur < 1 || splitCur > splitCap + 1 || pathCur < 1 || pathCur > pathCap + 1)
            {
                return false;
            }
            if (num >= 4)
            {
                r.Skip(44 * (beltCur - 1)); // BeltComponent 44B 批量
            }
            else
            {
                for (int i = 1; i < beltCur; i++) SkipBelt(r);
            }
            r.Skip(beltRec * 4);
            for (int m = 1; m < splitCur; m++) SkipSplitter(r);
            r.Skip(splitRec * 4);
            for (int p = 1; p < pathCur; p++)
            {
                if (r.I32() != 0) SkipCargoPath(r);
            }
            r.Skip(pathRec * 4);
            if (num >= 1)
            {
                int monCur = r.I32();
                int monCap = r.I32();
                int monRec = r.I32();
                if (monCap < 0 || monCap > 5_000_000 || monCur < 0 || monCur > monCap + 1) return false;
                for (int m = 1; m < monCur; m++) SkipMonitor(r);
                r.Skip(monRec * 4);
            }
            if (num >= 2)
            {
                int scCur = r.I32();
                int scCap = r.I32();
                int scRec = r.I32();
                if (scCap < 0 || scCap > 5_000_000 || scCur < 0 || scCur > scCap + 1) return false;
                for (int m = 1; m < scCur; m++) SkipSpraycoater(r);
                r.Skip(scRec * 4);
            }
            if (num >= 3)
            {
                int plCur = r.I32();
                int plCap = r.I32();
                int plRec = r.I32();
                if (plCap < 0 || plCap > 5_000_000 || plCur < 0 || plCur > plCap + 1) return false;
                for (int m = 1; m < plCur; m++) SkipPiler(r);
                r.Skip(plRec * 4);
            }
            return true;
        }

        private static void SkipStorageComponent(R r)
        {
            int num = r.I32();
            r.I32(); r.I32();
            if (num >= 1) { r.I32(); r.I32(); r.I32(); r.I32(); }
            r.I32();
            int size = r.I32();
            if (num >= 1) r.I32();
            if (size < 0 || size > 10000) throw new EndOfStreamException();
            for (int i = 0; i < size; i++)
            {
                r.I32(); r.I32(); r.I32(); r.I32();
                if (num >= 2) r.I32();
            }
        }

        private static void SkipTank(R r)
        {
            int num = r.I32();
            r.I32(); r.I32();
            r.I32(); r.I32();
            r.I32(); r.I32(); r.I32(); r.I32();
            r.Bool(); r.Bool(); r.Bool(); r.Bool();
            r.I32(); r.I32();
            if (num >= 1) r.I32();
            r.I32();
            r.Bool(); r.Bool(); r.Bool();
        }

        private static bool SkipFactoryStorage(R r)
        {
            int num = r.I32();
            int stCur = r.I32();
            int stCap = r.I32();
            int stRec = r.I32();
            if (stCap < 0 || stCap > 20_000_000 || stCur < 0 || stCur > stCap + 1) return false;
            for (int i = 1; i < stCur; i++)
            {
                int sid = r.I32();          // storage id（FactoryStorage.Import: 空槽只写/读 4B）
                if (sid != 0)
                {
                    int hsz = r.I32();      // size 头（Export: w.Write(size)）
                    if (hsz < 0 || hsz > 1000000) return false;
                    SkipStorageComponent(r);
                }
            }
            r.Skip(stRec * 4);
            int tnCap = r.I32();
            int tnCur = r.I32();
            int tnRec = r.I32();
            if (tnCap < 0 || tnCap > 5_000_000 || tnCur < 0 || tnCur > tnCap + 1) return false;
            for (int l = 1; l < tnCur; l++)
                SkipTank(r);        // tank 槽无条件写全（TankComponent.Export：空槽也写，无 sid 判断，勿像 storage 那样跳空）
            r.Skip(tnRec * 4);
            return true;
        }

        // ============ PowerSystem（gen/node/consumer/acc/exc/net 六池） ============

        private static void SkipGen(R r)
        {
            // PowerGeneratorComponent.Import（decomp-full\PowerGeneratorComponent.cs:188）逐字段：
            // 旧实现把 networkId/photo·wind·gamma/2×I64+I16/I64/3×I16 等无条件字段塞进版本分支，
            // num 非最大值时每条约少读几字节，发电机多的工厂（164+）累计错位 → 后续池读到 0 区。
            int num = r.I32();
            r.I32(); r.I32();                       // id, entityId
            if (num >= 1) r.U8();                   // subId
            r.I32();                                // networkId（无条件）
            if (num >= 6) r.I32();                  // baseRuinId
            r.Bool(); r.Bool(); r.Bool();           // photovoltaic, wind, gamma（无条件）
            if (num >= 3) r.Bool();                 // geothermal
            r.I64(); r.I64();                       // genEnergyPerTick, useFuelPerTick（无条件）
            r.I16();                                // fuelMask（无条件）
            if (num >= 8) r.I16();                  // catalystMask
            r.I64();                                // fuelEnergy（无条件）
            r.I16(); r.I16(); r.I16();              // curFuelId, fuelId, fuelCount（无条件）
            if (num >= 2)
            {
                r.I16();                            // fuelInc
                r.Bool();                           // productive
                if (num >= 7) r.Bool();             // incUsed
                r.U8();                             // fuelIncLevel
            }
            if (num >= 8) r.U8();                   // catalystIncLevel
            r.I64(); r.I32();                       // fuelHeat, catalystId（无条件）
            if (num >= 8) { r.I32(); r.I16(); r.I16(); }   // curCatalystId, catalystCount, catalystInc
            r.I32();                                // catalystPoint（无条件）
            if (num >= 2) r.I32();                  // oldIncPoint
            r.I32();                                // productId（无条件）
            r.F32();                                // productCount（无条件）
            r.I64();                                // productHeat（无条件）
            r.F32(); r.F32();                       // warmup, ionEnhance（无条件）
            if (num >= 3) r.F32();                  // gthStrength
            if (num >= 5) r.F32();                  // gthAffectStrength
            r.F32(); r.F32(); r.F32();              // x, y, z（无条件）
            if (num >= 4) r.Bool();                 // boost
        }

        private static void SkipPowerNode(R r)
        {
            r.I32(); r.I32(); r.I32(); r.I32();
            r.Bool();
            r.I32(); r.I32(); r.I32();
            r.F32(); r.F32(); r.F32(); r.F32(); r.F32();
        }

        private static void SkipConsumer(R r)
        {
            // PowerConsumerComponent.Import（PowerConsumerComponent.cs:51）：networkId 无条件（旧实现包进 num>=1）。
            int num = r.I32();
            r.I32(); r.I32(); r.I32();              // id, entityId, networkId
            r.F32(); r.F32(); r.F32();              // plugPos
            if (num >= 1) r.F32();                  // plugAlt
            r.I64(); r.I64(); r.I64(); r.I64();     // requiredEnergy, 占位, workEnergyPerTick, idleEnergyPerTick
        }

        private static void SkipAcc(R r)
        {
            // PowerAccumulatorComponent.Import（PowerAccumulatorComponent.cs:48）：networkId 无条件（旧实现包进 num>=1）。
            int num = r.I32();
            r.I32(); r.I32();                       // id, entityId
            if (num >= 1) r.U8();                   // subId
            r.I32();                                // networkId（无条件）
            r.I64(); r.I64(); r.I64(); r.I64();     // inputEnergyPerTick, outputEnergyPerTick, curEnergy, maxEnergy
        }

        private static void SkipExc(R r)
        {
            int num = r.I32();
            r.I32(); r.I32();
            if (num >= 2) { r.U8(); r.I32(); r.I16(); r.I16(); }
            if (num >= 3)
            {
                r.I16(); r.I16();
                r.F32(); r.F32();
            }
            r.I64(); r.I64(); r.I64();
            if (num >= 4) { r.U8(); r.I32(); r.I32(); }
            if (num >= 1)
            {
                r.I32(); r.I32(); r.I32(); r.I32();
                r.Bool(); r.Bool(); r.Bool(); r.Bool();
                r.I32(); r.I32(); r.I32(); r.I32();
            }
        }

        private static void SkipNode(R r)
        {
            r.I32(); r.I32();
            r.F32(); r.F32(); r.F32();
            r.F32(); r.F32();
            r.I32(); r.I32(); r.I32();
            int n1 = r.I32();
            int n2 = r.I32();
            int n3 = r.I32();
            r.Skip((n1 + n2 + n3) * 4);
        }

        private static void SkipPowerNetwork(R r)
        {
            int num = r.I32();
            r.I32();
            if (num >= 1) { r.F64(); r.F64(); }
            int nNodes = r.I32();
            int nCons = r.I32();
            int nGens = r.I32();
            int nAccs = r.I32();
            int nExcs = r.I32();
            for (int i = 0; i < nNodes; i++) SkipNode(r);
            r.Skip((nCons + nGens + nAccs + nExcs) * 4);
        }

        private static bool SkipPowerSystem(R r)
        {
            r.I32(); // 版本
            // gen
            int cap = r.I32(); int cur = r.I32(); int rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipGen(r);
            r.Skip(rec * 4);
            // node
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipPowerNode(r);
            r.Skip(rec * 4);
            // consumer
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipConsumer(r);
            r.Skip(rec * 4);
            // acc
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipAcc(r);
            r.Skip(rec * 4);
            // exc
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipExc(r);
            r.Skip(rec * 4);
            // net（PowerNetwork）
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 0; i < cur; i++)
            {
                if (r.I32() == 1) SkipPowerNetwork(r);
            }
            r.Skip(rec * 4);
            return true;
        }

        // ============ FactorySystem（miner/inserter/assembler/fractionator/ejector/silo/lab） ============

        private static void SkipMiner(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32();
            r.I32(); r.I32();
            if (num >= 1) r.F32();
            r.I32(); r.I32(); r.I32(); r.I32();
            int veinCount = r.I32();
            if (veinCount < 0 || veinCount > 100000) throw new EndOfStreamException();
            for (int i = 0; i < veinCount; i++) r.I32();
            r.I32(); r.I32(); r.I32(); r.I32();
            if (num >= 2) { r.F64(); return; }
            r.U32();
        }

        private static void SkipInserter(R r)
        {
            int num = r.I32();
            bool flag = num >= 1;
            r.I32(); r.I32(); r.I32();
            if (flag)
            {
                r.U8();
                if (num < 4) r.U8();
            }
            else
            {
                r.I32(); r.I32();
            }
            r.I32(); r.I32(); r.I32(); r.I32(); r.I32();
            r.Bool(); r.Bool();
            if (num >= 3)
            {
                r.U8(); r.U8(); r.U8(); r.Bool();
            }
            r.I16(); r.I16();
            if (flag)
            {
                r.I16(); r.I16(); r.U8();
                if (num < 3) r.U8();
            }
            else
            {
                r.I32(); r.I32(); r.I32(); r.I32();
            }
            if (num < 4)
            {
                r.Skip(7 * 4); // pose.pos2 7×Single
                if (flag) { r.U8(); r.U8(); }
                else { r.I16(); r.I16(); }
            }
            if (num >= 2)
            {
                r.I16(); r.I16();
            }
        }

        private static void SkipInserterPose(R r)
        {
            r.U8();            // InserterPose.Import：byte + 7×Single + byte + byte（31B）
            r.Skip(7 * 4);
            r.U8(); r.U8();
        }

        private static void SkipAssembler(R r)
        {
            int num = r.I32();
            bool flag = num >= 1;
            r.I32(); r.I32(); r.I32();
            r.Bool();
            if (num < 6) r.Bool();
            r.I32(); r.I32();
            if (num >= 2)
            {
                r.I32(); r.I32(); r.I32(); r.I32();
                if (num < 6) r.Bool();
                if (num >= 3) r.Bool();
                if (num >= 4) r.Bool();
            }
            if (num >= 5) { r.I32(); r.I32(); }
            int recipeId = flag ? r.I16() : r.I32();
            if (recipeId <= 0) return;
            if (flag)
            {
                r.U8(); // recipeType
                if (num < 6)
                {
                    r.I32();
                    if (num >= 2) r.I32();
                    int nn = r.U8();
                    for (int j = 0; j < nn; j++) r.I16();
                    nn = r.U8();
                    for (int k = 0; k < nn; k++) r.I16();
                }
                else
                {
                    int nn = r.U8();
                    for (int l = 0; l < nn; l++) r.I16();
                }
                int n = r.U8();
                for (int m = 0; m < n; m++) r.I32();
                if (num >= 2)
                {
                    n = r.U8();
                    for (int n2 = 0; n2 < n; n2++) r.I32();
                }
                n = r.U8();
                for (int n3 = 0; n3 < n; n3++) r.I16();
                if (num < 6)
                {
                    n = r.U8();
                    for (int n4 = 0; n4 < n; n4++) r.I16();
                    n = r.U8();
                    for (int n5 = 0; n5 < n; n5++) r.I16();
                }
                else
                {
                    n = r.U8();
                    for (int n6 = 0; n6 < n; n6++) r.I16();
                }
                n = r.U8();
                for (int n7 = 0; n7 < n; n7++) r.I32();
                return;
            }
            // 老分支（!flag）
            r.I32(); r.I32(); // recipeType + 占位
            for (int seg = 0; seg < 7; seg++)
            {
                int nn = r.I32();
                if (nn < 0 || nn > 100000) throw new EndOfStreamException();
                for (int j = 0; j < nn; j++) r.I32();
            }
        }

        private static void SkipFractionator(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32();
            r.I32(); r.I32(); r.I32();
            r.Bool(); r.Bool(); r.Bool(); r.Bool();
            r.F32();
            r.I32(); r.I32(); r.I32();
            if (num >= 2)
            {
                r.I32();
                if (num >= 3) r.F32(); else r.I32();
            }
            r.I32(); r.I32();
            if (num >= 2) { r.I32(); r.I32(); }
            r.I32();
            r.Bool();
            r.Bool();
            if (num >= 4) r.Bool();
            r.I32(); r.I32(); r.I32();                 // fluidInputMax, productOutputMax, fluidOutputMax
            if (num >= 1) { r.I32(); r.I32(); }        // fluidOutputTotal, productOutputTotal
            r.U32();                                   // seed
        }

        private static void SkipEjector(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32(); r.I32();
            r.I32(); r.I32();
            r.Bool();
            r.I32(); r.I32(); r.I32(); r.I32();
            if (num >= 1) r.I32();
            r.I32();
            if (num >= 4) { r.I32(); r.I32(); }
            r.F32(); r.F32();
            if (num >= 2) r.Bool();
            if (num >= 3) r.Bool();
            if (num >= 4) r.Bool();
            r.F32(); r.F32(); r.F32();
            r.F32();
            r.F32(); r.F32(); r.F32(); r.F32();
            r.F32(); r.F32(); r.F32();
        }

        private static void SkipSilo(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32(); r.I32();
            r.I32(); r.I32();
            r.Bool();
            r.I32(); r.I32(); r.I32(); r.I32();
            if (num >= 1) { r.I32(); r.I32(); r.Bool(); }
            if (num >= 2) r.Bool();
            if (num >= 3) r.Bool();
            r.F32(); r.F32(); r.F32(); r.F32();
            r.F32(); r.F32(); r.F32();
        }

        private static void SkipLab(R r)
        {
            int num = r.I32();
            r.I32(); r.I32(); r.I32(); r.I32();
            r.Bool();
            if (num < 5) r.Bool();
            r.I32(); r.I32();
            if (num >= 1)
            {
                r.I32(); r.I32(); r.I32(); r.I32();
                if (num < 5) r.Bool();
                if (num >= 2)
                {
                    r.Bool();
                    if (num >= 3) r.Bool();
                    r.I32(); r.I32();
                }
            }
            bool researchMode = r.Bool();
            int recipeId = r.I32();
            r.I32(); // techId
            bool matrixMode = !researchMode && recipeId > 0;
            if (matrixMode)
            {
                if (num < 5)
                {
                    r.I32();
                    if (num >= 1) r.I32();
                }
                if (num >= 4) { r.I32(); r.I32(); }
                int n = r.I32();
                for (int i = 0; i < n; i++) r.I32();
                if (num < 5)
                {
                    n = r.I32();
                    for (int j = 0; j < n; j++) r.I32();
                }
                n = r.I32();
                for (int k = 0; k < n; k++) r.I32();
                if (num >= 1)
                {
                    n = r.I32();
                    for (int l = 0; l < n; l++) r.I32();
                }
                n = r.I32();
                for (int m = 0; m < n; m++) r.I32();
                n = r.I32();
                for (int n2 = 0; n2 < n; n2++) r.I32();
                if (num < 5)
                {
                    n = r.I32();
                    for (int n3 = 0; n3 < n; n3++) r.I32();
                }
                n = r.I32();
                for (int n4 = 0; n4 < n; n4++) r.I32();
            }
            if (researchMode)
            {
                if (num < 5)
                {
                    int n = r.I32();
                    for (int i = 0; i < n; i++) r.I32();
                }
                int nn = r.I32();
                for (int j = 0; j < nn; j++) r.I32();
                nn = r.I32();
                for (int k = 0; k < nn; k++) r.I32();
                if (num >= 1)
                {
                    nn = r.I32();
                    for (int l = 0; l < nn; l++) r.I32();
                }
            }
        }

        private static bool SkipFactorySystem(R r)
        {
            int ver = r.I32(); // 版本
            int cap, cur, rec;
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipMiner(r);
            r.Skip(rec * 4);
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++)
            {
                SkipInserter(r);
                if (ver >= 1) SkipInserterPose(r);   // inserterPosePool（每 inserter 后写 pose）
            }
            r.Skip(rec * 4);
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipAssembler(r);
            r.Skip(rec * 4);
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipFractionator(r);
            r.Skip(rec * 4);
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipEjector(r);
            r.Skip(rec * 4);
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipSilo(r);
            r.Skip(rec * 4);
            cap = r.I32(); cur = r.I32(); rec = r.I32();
            if (cap < 0 || cap > 5_000_000 || cur < 0 || cur > cap + 1) return false;
            for (int i = 1; i < cur; i++) SkipLab(r);
            r.Skip(rec * 4);
            return true;
        }

        /// <summary>调试：扫描 enemySystem 头，只解析 builders+bases 池就记录 bases cursor，找 bases>0 候选。</summary>
        public static List<FactorySummary> DebugGroundBases(string savePath)
        {
            byte[] data = File.ReadAllBytes(savePath);
            var result = new List<FactorySummary>();
            int n = data.Length;
            for (int off = 0; off + 16 < n; off++)
            {
                int ver = BitConverter.ToInt32(data, off);
                if (ver < 0 || ver > 100) continue;
                int waves = BitConverter.ToInt32(data, off + 12);
                if (waves < 1 || waves > 1000000) continue;
                var r = new R(data, off);
                r.Budget = 120_000_000;
                var fs = new FactorySummary { Offset = off };
                try
                {
                    int v2 = r.I32();
                    if (v2 < 0 || v2 > 100) continue;
                    r.I32(); r.U32(); r.I32();  // rtseed/lehmer/waves
                    // builders 池（只读头+粗略跳）
                    int bcap = r.I32(), bcur = r.I32(), brc = r.I32();
                    if (bcap < 0 || bcap > 100000 || bcur < 0 || bcur > bcap + 1 || brc < 0 || brc > bcap + 1) continue;
                    for (int i = 1; i < bcur; i++) if (!SkipBuilderComp(r)) throw new Exception();
                    r.Skip(brc * 4);
                    // bases 池头
                    int pcap = r.I32(), pcur = r.I32(), prc = r.I32();
                    if (pcap < 0 || pcap > 100000 || pcur < 0 || pcur > pcap + 1 || prc < 0 || prc > pcap + 1) continue;
                    fs.GroundBases = pcur - 1;
                    if (fs.GroundBases > 0)
                        result.Add(fs);
                }
                catch (Exception) { }
            }
            return result;
        }

        /// <summary>调试：前缀 + 分阶段完整解析，返回每阶段字节位置/结果。</summary>
        public static string TestFull(string savePath, int off)
        {
            byte[] data = System.IO.File.ReadAllBytes(savePath);
            var r = new R(data, off);
            r.Budget = 120_000_000;
            var fail = new StringBuilder();
            FactorySummary fs;
            string stage = "prefix";
            try
            {
                if (!TryParsePrefix(r, null, out fs, fail)) return $"@{off:X8} PREFIX FAIL[{fail}]";
                bool ok;
                stage = "entity"; ok = SkipEntityPool(r, fs); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "prebuild"; ok = SkipPrebuild(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "craft"; ok = SkipCraft(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "enemy"; ok = SkipEnemy(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "vege"; ok = SkipVege(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "vein"; ok = SkipVein(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "ruin"; ok = SkipRuin(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "cargoC"; ok = SkipCargoContainer(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "cargoT"; ok = SkipCargoTraffic(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "storage"; ok = SkipFactoryStorage(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "power"; ok = SkipPowerSystem(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "factorySys"; ok = SkipFactorySystem(r); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                stage = "groundSys"; ok = SkipGroundSystem(r, fs); if (!ok) return $"@{off:X8} FAIL[{stage}] @{r.o:X8}";
                return $"@{off:X8} FULL-OK planet={fs.PlanetId} bases={fs.GroundBases} units={fs.GroundUnits} end={r.o:X8} protoIdCount={fs.ProtoIdCount.Count}";
            }
            catch (Exception ex)
            {
                return $"@{off:X8} EX[{stage}/{fail}] {ex.GetType().Name}: {ex.Message} @{r.o:X8}";
            }
        }

        /// <summary>调试：只执行前缀解析（不跑 ParseOne），快速确认 factory[0] 前缀。</summary>
        /// <summary>诊断：指定 hash bucketCount 解析工厂前缀（定位 bc 误判）。</summary>
        public static string TestPrefixWithBucket(byte[] data, int off, int bc)
        {
            var r = new R(data, off);
            r.Budget = 8L << 20;
            try
            {
                int ver = r.I32();
                int pid = r.I32();
                if (ver < 1 || ver > 100) return $"bc={bc} ver非法={ver}";
                if (ver >= 5) { r.I32(); r.I32(); r.I32(); }
                int modLen = r.I32();
                if (modLen < 0 || modLen > 2_000_000) return $"bc={bc} modLen={modLen}";
                r.Skip(modLen);
                int n1 = r.I32();
                if (n1 < 0 || n1 > 100000) return $"bc={bc} n1={n1}";
                r.Skip(n1 * 8);
                int n2 = r.I32();
                if (n2 < 0 || n2 > 1_000_000) return $"bc={bc} n2={n2}";
                r.Skip(n2 * 28);
                if (ver >= 3) r.U8();
                for (int h = 0; h < 2; h++)
                {
                    int np = r.I32();
                    if (np < 0 || np > 5_000_000) return $"bc={bc} hashpool={np} @0x{r.o - 4:X8}";
                    r.Skip(np * 8);
                    r.Skip((bc + 1) * 4 + bc * 8);
                }
                int cap = r.I32(), cur = r.I32(), rec = r.I32();
                return $"bc={bc} OK ver={ver} pid={pid} cap={cap} cur={cur} rec={rec} @0x{r.o:X8}";
            }
            catch (Exception ex)
            {
                return $"bc={bc} EX {ex.GetType().Name} @0x{r.o:X8}";
            }
        }

        public static string TestPrefixOnly(string savePath, int off)
        {
            byte[] data = System.IO.File.ReadAllBytes(savePath);
            return TestPrefixOnly(data, off);
        }

        /// <summary>同 TestPrefixOnly，但复用已读取的字节数组（批量诊断用，避免每次重读大档）。</summary>
        public static string TestPrefixOnly(byte[] data, int off)
        {
            var r = new R(data, off);
            r.Budget = 120_000_000;
            var fail = new StringBuilder();
            FactorySummary fs;
            try
            {
                bool ok = TryParsePrefix(r, null, out fs, fail);
                if (!ok) return $"@{off:X8} FAIL[{fail}] (读至 {r.o:X8})";
                return $"@{off:X8} PREFIX-OK planet={fs.PlanetId} ver={fs.Version} bc={fs.HashBucketCount} cur={fs.EntityCursor}";
            }
            catch (Exception ex)
            {
                return $"@{off:X8} EX[{fail}] {ex.GetType().Name}: {ex.Message} (读至 {r.o:X8})";
            }
        }

        /// <summary>调试：对指定偏移执行前缀+完整解析并打印每步失败点。</summary>
        public static string TestPrefix(string savePath, int off)
        {
            byte[] data = System.IO.File.ReadAllBytes(savePath);
            var r = new R(data, off);
            r.Budget = 120_000_000;   // 与 Extract 一致：防御误报候选巨循环
            var fail = new StringBuilder();
            FactorySummary fs;
            try
            {
                bool ok = TryParsePrefix(r, null, out fs, fail);
                if (!ok) return $"@{off:X8} FAIL[{fail}] (读至 {r.o:X8})";
                bool full = ParseOne(r, fs);
                if (!full) return $"@{off:X8} PREFIX-OK planet={fs.PlanetId} ver={fs.Version} bc={fs.HashBucketCount} cur={fs.EntityCursor} 但 ParseOne FAIL[{fs.Error}] (读至 {r.o:X8})";
                return $"@{off:X8} FULL-OK planet={fs.PlanetId} ver={fs.Version} bc={fs.HashBucketCount} entities={fs.EntityCount} bases={fs.GroundBases} end={r.o:X8}";
            }
            catch (Exception ex)
            {
                return $"@{off:X8} EX[{fail}] {ex.GetType().Name}: {ex.Message} (读至 {r.o:X8})";
            }
        }

        /// <summary>全文件扫描 EnemyDFGroundSystem 头（version+rtseed×2+maxAssaultWaves+7 池深度校验），
        /// 直接提取地面基地数（C 项）。工厂段内部该模式唯一，误报率低。</summary>
        public static List<FactorySummary> ExtractGroundBases(string savePath)
        {
            byte[] data = File.ReadAllBytes(savePath);
            var result = new List<FactorySummary>();
            var seen = new HashSet<string>();
            int n = data.Length;
            for (int off = 0; off + 16 < n; off++)
            {
                int ver = BitConverter.ToInt32(data, off);
                if (ver < 0 || ver > 100) continue;
                int waves = BitConverter.ToInt32(data, off + 12);
                if (waves < 1 || waves > 500) continue;
                var r = new R(data, off);
                r.Budget = 120_000_000;
                var fs = new FactorySummary { Offset = off };
                try
                {
                    if (!SkipGroundSystem(r, fs)) continue;
                    string key = $"{off}:{fs.GroundBases}";
                    if (seen.Contains(key)) continue;
                    seen.Add(key);
                    fs.EndOffset = r.o;
                    result.Add(fs);
                }
                catch (Exception) { }
            }
            return result;
        }

        // ============ 主流程：特征扫描 factoryCount 段起点 ============

        /// <summary>调试：扫描 ver+pid 候选分布，打印每个候选的 TryParsePrefix 失败阶段。</summary>
        public static void DebugScan(string savePath)
        {
            byte[] data = File.ReadAllBytes(savePath);
            int n = data.Length;
            int vp = 0, pfOk = 0;
            var failCount = new Dictionary<string, int>();
            for (int off = 0; off + 8 < n; off++)
            {
                int ver = BitConverter.ToInt32(data, off);
                if (ver < 1 || ver > 100) continue;
                int pid = BitConverter.ToInt32(data, off + 4);
                if (pid < 100 || pid > 10000 || pid % 100 == 0 || pid / 100 < 1 || pid / 100 > 64) continue;
                vp++;
                var r = new R(data, off);
                var fail = new StringBuilder();
                FactorySummary fs;
                try
                {
                    if (!TryParsePrefix(r, null, out fs, fail))
                    {
                        string key = fail.ToString();
                        if (key.Length == 0) key = "?";
                        if (failCount.TryGetValue(key, out int c)) failCount[key] = c + 1; else failCount[key] = 1;
                        continue;
                    }
                }
                catch (Exception)
                {
                    if (failCount.TryGetValue("ex", out int c)) failCount["ex"] = c + 1; else failCount["ex"] = 1;
                    continue;
                }
                pfOk++;
                Console.WriteLine($"PREFIX-OK @{off} (0x{off:X8}) ver={ver} pid={pid} bc={fs.HashBucketCount} entities={fs.EntityCursor}");
                    // 完整解析
                    try
                    {
                        if (ParseOne(r, fs))
                            Console.WriteLine($"  FULL-OK end={r.o} bases={fs.GroundBases}");
                        else
                            Console.WriteLine($"  full-fail");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  full-ex: {ex.Message}");
                    }
            }
            Console.WriteLine($"vp hits total: {vp}, prefix-ok: {pfOk}");
            foreach (var kv in failCount)
                Console.WriteLine($"  fail[{kv.Key}]: {kv.Value}");
        }

        public static List<FactorySummary> Extract(string savePath, ISet<int> validPlanetIds = null)
        {
            byte[] data = File.ReadAllBytes(savePath);
            int n = data.Length;
            var result = new List<FactorySummary>();
            var seen = new HashSet<int>();

            bool pidOk(int pid) => pid >= 100 && pid <= 10000 && pid % 100 != 0 && pid / 100 >= 1 && pid / 100 <= 99;

            // —— 阶段 1：定位 gameData.factoryCount + 顺序解析 ——
            // 工厂段连续（GameData.Import：player → factoryCount → factory×N → galacticTransport），
            // 一个工厂的 EndOffset 即下一个工厂起点；工厂段结束 = 下一个位置非合法工厂前缀。
            // 定位模式：[factoryCount(1..999)] [ver=10] [pid 合法] —— 误报率 ~1e-14，全文件唯一。
            // 相比旧实现（全文件逐字节扫 ver(1..100)+pid，2.2 万候选逐个全链解析），
            // 顺序解析成本 = O(工厂总字节)，与文件大小/误报解耦，且不漏工厂（链中无“候选丢弃”路径）。
            for (int off = 0; off + 12 <= n; off++)
            {
                int fc = BitConverter.ToInt32(data, off);
                if (fc < 1 || fc > 999) continue;
                if (BitConverter.ToInt32(data, off + 4) != 10) continue;   // PlanetFactory.Import 版本恒 10
                if (!pidOk(BitConverter.ToInt32(data, off + 8))) continue;

                long pos = off + 4;
                var chain = new List<FactorySummary>();
                bool chainOk = true;
                while (pos + 12 <= n)
                {
                    try
                    {
                        if (BitConverter.ToInt32(data, (int)pos) != 10) break;      // 段尾（galacticTransport 开头）
                        if (!pidOk(BitConverter.ToInt32(data, (int)pos + 4))) break;
                        var r = new R(data, (int)pos);
                        r.Budget = LOCATE_BUDGET;
                        if (!TryParsePrefix(r, null, out var fs)) { chainOk = false; break; }
                        if (validPlanetIds != null && validPlanetIds.Count > 0 && !validPlanetIds.Contains(fs.PlanetId)) { chainOk = false; break; }
                        fs.Offset = pos;
                        r.Budget = PARSE_BUDGET;
                        if (!ParseOne(r, fs)) { Console.WriteLine($"  [chain] @0x{pos:X8} pid={fs.PlanetId} ver={fs.Version} err={fs.Error}"); chainOk = false; break; }
                        fs.EndOffset = r.o;
                        chain.Add(fs);
                        pos = r.o;
                    }
                    catch (EndOfStreamException) { chainOk = false; break; }
                    catch (Exception) { chainOk = false; break; }
                }
                // fc==1 的误报率极高（前面任意 int32=1 都能命中），且单工厂档由阶段 2 兑底更快 —— 要求 fc>=2
                if (chainOk && chain.Count == fc && fc >= 2)
                {
                    foreach (var f in chain)
                        if (seen.Add(f.PlanetId)) result.Add(f);
                    Console.WriteLine($"FactoryExtractor: factoryCount={fc}（定位 @0x{off + 4:X8}）顺序解析 {chain.Count} 个工厂（==fc 校验通过）");
                    break;
                }
                // 链断/链长≠fc = 疑似误报或 ParseOne 字段 bug，继续扫下一个 fc 模式；最终由阶段 2 兜底
            }

            // —— 阶段 2（总是执行，兜底+补漏）：全文件盲搜（ver==10 预筛 + 全链）——
            // 不再依赖阶段 1 是否 locate：阶段 1 可能被 fc 误报抢先（某存档现场：fc=1 误定位 → 只出 1 个工厂），
            // 也可能链中间断。盲搜按 pid 去重，与阶段 1 结果合并。
            {
                int before = result.Count;
                for (int off = 0; off + 12 < n; off++)
                {
                    if (BitConverter.ToInt32(data, off) != 10) continue;   // 版本恒 10 预筛（vs 旧版 ver 1..100）
                    int pid = BitConverter.ToInt32(data, off + 4);
                    if (!pidOk(pid)) continue;
                    if (validPlanetIds != null && validPlanetIds.Count > 0 && !validPlanetIds.Contains(pid)) continue;                    if (seen.Contains(pid)) continue;
                    R r = null;
                    try
                    {
                        r = new R(data, off);
                        r.Budget = LOCATE_BUDGET;
                        if (!TryParsePrefix(r, null, out var fs)) continue;
                        fs.Offset = off;
                        r.Budget = PARSE_BUDGET;
                        if (!ParseOne(r, fs)) { Console.WriteLine($"  [blind] @0x{off:X8} pid={pid} err={fs.Error}"); continue; }
                        fs.EndOffset = r.o;
                        if (fs.EntityCount <= 0 && fs.GroundBases <= 0) continue;  // 空工厂防误报
                        seen.Add(pid);
                        result.Add(fs);
                    }
                    catch (EndOfStreamException) { }
                    catch (Exception) { }
                }
                result.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                Console.WriteLine($"FactoryExtractor: 阶段 2 兜底盲搜，新增 {result.Count - before} 个工厂（共 {result.Count}）");
            }
            return result;
        }

        private static bool ParseOne(R r, FactorySummary fs)
        {
            if (!SkipEntityPool(r, fs)) { fs.Error = "entityPool"; return false; }
            if (!SkipPrebuild(r)) { fs.Error = "prebuild"; return false; }
            if (!SkipCraft(r)) { fs.Error = "craft"; return false; }
            if (!SkipEnemy(r)) { fs.Error = "enemy"; return false; }
            if (!SkipVege(r)) { fs.Error = "vege"; return false; }
            if (!SkipVein(r, fs)) { fs.Error = "vein"; return false; }
            if (!SkipRuin(r)) { fs.Error = "ruin"; return false; }
            if (!SkipCargoContainer(r)) { fs.Error = "cargoContainer"; return false; }
            if (!SkipCargoTraffic(r)) { fs.Error = "cargoTraffic"; return false; }
            if (!SkipFactoryStorage(r)) { fs.Error = "factoryStorage"; return false; }
            if (!SkipPowerSystem(r)) { fs.Error = "powerSystem"; return false; }
            if (!SkipFactorySystem(r)) { fs.Error = "factorySystem"; return false; }
            if (!SkipGroundSystem(r, fs)) { fs.Error = "enemySystem"; return false; }
            return true;
        }

        // ============ enemySystem（EnemyDFGroundSystem.Import：7 池） ============

        private static bool SkipGroundSystem(R r, FactorySummary fs)
        {
            int ver = r.I32();
            if (ver < 0 || ver > 100) return false;
            r.I32();                    // rtseed
            r.U32();                    // rtseed_lehmer
            r.I32();                    // maxAssaultWaves
            if (!SkipGroundPool(r, SkipBuilderComp, logName: "builders")) { Console.WriteLine($"  [groundSystem] builders FAIL @0x{r.o:X8}"); return false; }
            if (!SkipGroundPool(r, SkipBaseComp, fs, isBases: true, logName: "bases")) { Console.WriteLine($"  [groundSystem] bases FAIL @0x{r.o:X8}"); return false; }
            if (!SkipGroundPool(r, SkipConnectorComp, logName: "connectors")) { Console.WriteLine($"  [groundSystem] connectors FAIL @0x{r.o:X8}"); return false; }
            if (!SkipGroundPool(r, SkipReplicatorComp, logName: "replicators")) { Console.WriteLine($"  [groundSystem] replicators FAIL @0x{r.o:X8}"); return false; }
            if (!SkipGroundPool(r, SkipTurretComp, logName: "turrets")) { Console.WriteLine($"  [groundSystem] turrets FAIL @0x{r.o:X8}"); return false; }
            if (!SkipGroundPool(r, SkipShieldComp, logName: "shields")) { Console.WriteLine($"  [groundSystem] shields FAIL @0x{r.o:X8}"); return false; }
            if (!SkipGroundPool(r, SkipUnitComp, fs, isUnits: true, logName: "units")) { Console.WriteLine($"  [groundSystem] units FAIL @0x{r.o:X8}"); return false; }
            return true;
        }

        private static bool SkipGroundPool(R r, Func<R, bool> skipItem, FactorySummary fs = null, bool isUnits = false, bool isBases = false, string logName = null)
        {
            int start = r.o;
            int cap = r.I32();
            int cur = r.I32();
            int rc = r.I32();
            if (cap < 0 || cap > 100000 || cur < 0 || cur > cap + 1 || rc < 0 || rc > 100000) { Console.WriteLine($"  [groundPool:{logName}] cap={cap} cur={cur} rc={rc} @0x{r.o - 12:X8}"); return false; }
            // count 语义统一为 cursor - recycleCursor - 1（DataPool/ObjectPool 一致）
            if (fs != null && isUnits) fs.GroundUnits = Math.Max(0, cur - rc - 1);
            if (fs != null && isBases) fs.GroundBases = Math.Max(0, cur - rc - 1);
            try
            {
                for (int i = 1; i < cur; i++)
                {
                    if (isBases)
                    {
                        // ⚠️ bases 是 ObjectPool<DFGBaseComponent>（class 元素），其余 6 池是 DataPool<T>（struct）。
                        // ObjectPool 每槽前多一个 int32 标记：有元素写槽位序号 i，空槽写 0（decomp-full\ObjectPool.cs Export/Import）。
                        // 漏读会让有地面基地的行星后续 connectors/replicators 等全部错位（某存档现场）。
                        int slot = r.I32();
                        if (slot == 0) continue;
                        if (slot != i) { Console.WriteLine($"  [groundPool:{logName}] slot={slot} expect={i} @0x{r.o - 4:X8}"); return false; }
                    }
                    if (!skipItem(r)) return false;
                }
                r.Skip(rc * 4);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [groundPool:{logName}] EX {ex.GetType().Name} @0x{r.o:X8} (head@0x{start:X8} cap={cap} cur={cur} rc={rc})");
                throw;
            }
            return true;
        }

        // ---- EnemyBuilderComponent（EnemyBuilderComponent.cs:145）----
        private static bool SkipBuilderComp(R r)
        {
            r.U8();
            int id = r.I32();
            if (id <= 0) return true;
            for (int i = 0; i < 13; i++) r.I32();   // enemyId..spEnergy
            r.I16();                                // state
            r.I32(); r.I32();                       // idleEnergy/workEnergy
            r.U16(); r.U16(); r.U16(); r.U16();      // builderIndex/buildCursor/buildChance/buildCDTime
            return true;
        }

        // ---- DFGBaseComponent（DFGBaseComponent.cs:260，含 EnemyFormation×3 + EvolveData + HatredList + pbuilders）----
        private static bool SkipBaseComp(R r)
        {
            int num = r.U8();
            int id = r.U16();
            if (id <= 0) return true;
            r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); r.I32(); // enemyId..ticks
            if (num >= 1) r.I32();                  // activeTick
            r.I32(); r.I32();                       // turboTicks/turboRepress
            for (int f = 0; f < 3; f++) SkipFormation(r);
            SkipEvolve(r);
            SkipHatred(r);
            if (num >= 1)
            {
                int skills = r.I32();
                if (skills < 0 || skills > 100000) return false;
                for (int i = 0; i < skills; i++) { r.I32(); r.I32(); }
            }
            int n2 = r.I32();
            if (n2 < 0 || n2 > 10000) return false;
            for (int j = 0; j < n2; j++) SkipGDBuilder(r);
            if (num >= 2) r.I32();
            return true;
        }

        // EnemyFormation（EnemyFormation.cs:172）
        private static void SkipFormation(R r)
        {
            r.U8();
            int ports = r.U16();
            int vac = r.U16();
            for (int i = 0; i < ports; i++) r.U8();
            if (ports <= 255)
                for (int j = 0; j < vac; j++) r.U8();
            else
                for (int k = 0; k < vac; k++) r.U16();
        }

        // EvolveData（EvolveData.cs:143）
        private static void SkipEvolve(R r)
        {
            byte num = r.U8();
            for (int i = 0; i < 10; i++) r.I32();    // level..waveTicks
            if (num >= 1) r.I32();                    // waveAsmTicks
            r.I32();                                  // rankBase
        }

        // HatredList（HatredList.cs:2530）
        private static void SkipHatred(R r)
        {
            r.U8();
            for (int i = 0; i < 16; i++) r.I32();     // 8×(target+value)
        }

        // GrowthPattern_DFGround.Builder（GrowthPattern_DFGround.cs:215 结构 / :305 Export）——地面基地 pbuilders 用此结构。
        // 真实序列化：U8 版占位 + I16 protoId + FlexibleInt instId + [instId>0: I16 instBuilderId]
        //   + pos(3F32) + rot(4F32) + [protoId==8127(轨道): F32 length] + I32 matterNeeded
        //   + I16 matterSpeed + I16 connections.Length + I16×N + U8 rank
        // ⚠️ 旧实现抄了 hexCoord/yaw/outputs 那套结构（空间侧），有地面基地时在此越界。
        private static void SkipGDBuilder(R r)
        {
            r.U8();                            // 版本占位
            int protoId = r.I16();             // protoId（isConn => 8127）
            int instId = r.Flex();             // FlexibleInt instId
            if (instId > 0) r.I16();           // instBuilderId
            r.Skip(3 * 4);                     // pos.x/y/z
            r.Skip(4 * 4);                     // rot.x/y/z/w
            if (protoId == 8127) r.F32();      // 仅轨道（isConn）写 length
            r.I32();                           // matterNeeded
            r.I16();                           // matterSpeed
            int conns = r.I16();               // connections.Length
            if (conns < 0 || conns > 100000) throw new EndOfStreamException();
            r.Skip(conns * 2);                 // connections: I16×N
            r.U8();                            // rank
        }

        // TargetQueue（TargetQueue.cs:95）
        private static void SkipTargetQueue(R r)
        {
            r.U8();
            int cnt = r.U8();
            if (cnt > 0) r.Skip(12);
            if (cnt > 1) r.Skip(12);
            r.I32(); r.I32(); r.I32();
        }

        private static bool SkipConnectorComp(R r)
        {
            r.U8();
            int id = r.U16();
            if (id <= 0) return true;
            r.I32(); r.I32(); r.I32();
            r.F32();
            r.I32();
            r.U16(); r.U16();
            return true;
        }

        private static bool SkipReplicatorComp(R r)
        {
            r.U8();
            int id = r.U16();
            if (id <= 0) return true;
            r.I32(); r.I32();          // enemyId/builderId
            r.I16(); r.I16();          // baseId/productId
            r.U8();                    // productFormId
            r.U16();                   // productSpMatter
            r.I32();                   // productSpEnergy
            r.U16(); r.U16();          // productSpMax/turboSpeed
            r.U8();                    // unitSupply
            r.Skip(10 * 4);            // productInitialPos3+Rot4+Vel3
            r.U16(); r.U16();          // productInitialTick/productSp
            return true;
        }

        private static bool SkipTurretComp(R r)
        {
            r.U8();
            int id = r.U16();
            if (id <= 0) return true;
            r.I32(); r.I32(); r.I32(); r.I32();
            r.U8(); r.U8(); r.U8();
            r.I32();
            r.F32(); r.F32(); r.F32(); r.F32();
            r.I32();
            r.F32(); r.F32(); r.F32();
            r.I32(); r.I32(); r.I32();
            return true;
        }

        private static bool SkipShieldComp(R r)
        {
            r.U8();
            int id = r.U16();
            if (id <= 0) return true;
            r.I32(); r.I32(); r.I32();
            return true;
        }

        // EnemyUnitComponent（EnemyUnitComponent.cs:112，含 assaults(TargetQueue) + hatred(HatredList)）
        private static bool SkipUnitComp(R r)
        {
            byte num = r.U8();
            r.Flex(); r.Flex(); r.Flex();
            r.I16(); r.I16();
            r.U16(); r.U16();
            r.U8();
            r.Flex();
            r.I16(); r.I16(); r.I16(); r.I16();
            r.F32(); r.F32(); r.F32();
            SkipTargetQueue(r);
            SkipHatred(r);
            if (num >= 1) { r.I32(); r.I32(); }
            if (num >= 2) r.F32();
            return true;
        }
    }
}

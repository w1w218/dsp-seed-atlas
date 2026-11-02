using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DspCore
{
    /// <summary>
    /// 黑雾完整提取（HiveExtractor v2）：
    /// 按 SpaceSector.Import 权威结构定位 dfHives 段（hive 前魔法标记 19884），
    /// 完整实现 EnemyDFHiveSystem.Import（头部 + 9 组件池 + idle + formation + evolve + hatred），
    /// 提取每 hive 摘要（行星基地/太空巢穴归属、状态、在途火种数）+ **火种明细**（DFTinderComponent）。
    /// 组件/子结构依据：EnemyDFHiveSystem/DataPool/ObjectPool/各组件/EvolveData/TargetQueue/
    /// HatredList/DFCarrierData/EnemyFormation 反编译。
    /// </summary>
    public static class HiveExtractor
    {
        public class HiveSummary
        {
            public int HiveAstroId;
            public bool IsSpaceHive;
            public int StarId;
            public int PlanetId;
            public int Seed;
            public bool Realized;
            public bool IsEmpty;
            public int Ticks;
            public int TindersArrivingInTransit;
            public int TinderCount;             // 本地火种数（tinders 池）
            public int RootEnemyId;             // 巢穴核心 enemyId（0 = 核心已毁）
            public int PbuildersActive;         // 巢穴内活跃建筑数（索引≥2 且 instId>0）；
                                                // 游戏 isAlive 主要靠它，rootEnemyId 常为 0 不可单独使用
            public int CoresCount;              // cores 池元素数（>0 = 巢穴核心还在）
            public int BuildersCount;
            public int NodesCount;
            public int ReplicatorsCount;
            public int TurretsCount;
            public int UnitsCount;
            /// <summary>存活判定（EnemyDFHiveSystem.isAlive 简化版）：空巢/未实现不算活；
            /// 已实现且核心在 / 有活跃 relay / 有本地火种 → 活；否则视为已被清除（存档仍保留历史记录）。</summary>
            public bool IsAlive;
            public List<TinderRec> Tinders = new List<TinderRec>();
            public List<RelayRec> Relays = new List<RelayRec>();   // relay 投放记录（地面基地线索）
        }

        public class RelayRec
        {
            public int HiveAstroId;   // 投放巢穴
            public int RelayId;       // relay 组件 id
            public int TargetAstroId; // 投放目标行星（地面基地所在）
            public int BaseState;     // 1=已投放
            public int Stage;         // 2=投放完成
        }

        public class TinderRec
        {
            public int EnemyId;               // enemyPool 实体 id（位置在 EnemyData.pos）
            public int OriginHiveAstroId;
            public int TargetHiveAstroId;
            public short Direction;
            public short Stage;
            public float USpeed;
            public float Param0;
        }

        /// <summary>全文件多线程分块扫描：定位 19884 标记 → 完整 hive.Import → 摘要+火种。astroId 去重。</summary>
        public static List<HiveSummary> Extract(string path, int starCount, ISet<int> validPlanetIds)
        {
            long len;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                len = fs.Length;
            int threads = Math.Max(2, Math.Min(Environment.ProcessorCount, 16));
            long span = len;
            const long OVERLAP = 64 * 1024;   // hive.Import 最长可达数十 KB（组件池），重叠 64KB
            const long BLOCK = 16L * 1024 * 1024;   // 固定块流式（x86 进程内存安全：1.7GB 档不再 OOM）
            int magicHits = 0, parseOk = 0;
            var bag = new System.Collections.Concurrent.ConcurrentBag<HiveSummary>();
            System.Threading.Tasks.Parallel.For(0, threads, ti =>
            {
                long start = ti * (span / threads);
                long end = Math.Min(len, start + span / threads);
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
                        if (got < 64) { pos += BLOCK; continue; }
                        int p = 0;
                        while (p < got - 64)
                        {
                            if (BitConverter.ToInt32(buf, p) != 19884) { p++; continue; }
                            System.Threading.Interlocked.Increment(ref magicHits);
                            int off = p + 4;
                            var h = TryReadHive(buf, ref off, validPlanetIds);
                            if (h != null)
                            {
                                System.Threading.Interlocked.Increment(ref parseOk);
                                bag.Add(h);
                                p = off;   // 跳到 hive 尾
                            }
                            else p += 4;
                        }
                        pos += BLOCK;
                    }
                }
                finally { fs.Dispose(); }
            });
            var list = bag
                .GroupBy(x => x.HiveAstroId)
                .Select(g => g.First())
                .OrderBy(x => x.HiveAstroId)
                .ToList();
            {
                var sb = new System.Text.StringBuilder();
                for (int s = 0; s < stageFail.Length; s++)
                    if (stageFail[s] > 0) sb.Append($"st{s}={stageFail[s]} ");
                Console.WriteLine($"HiveExtractor: magic={magicHits} ok={parseOk} hives={list.Count} 失败stage: {sb}");
            }
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "hive-diag.txt"),
                $"{DateTime.Now:HH:mm:ss} magic={magicHits} ok={parseOk} hives={list.Count}\n");
            return list;
        }

        /// <summary>完整 EnemyDFHiveSystem.Import；成功返回摘要并推进 off，失败 null。</summary>
        static HiveSummary TryReadHive(byte[] b, ref int off, ISet<int> validPlanetIds)
        {
            try
            { try { return TryReadHiveInner(b, ref off, validPlanetIds); }
              catch (Exception) { return null; } }
            finally { }
        }
        static HiveSummary TryReadHiveInner(byte[] b, ref int off, ISet<int> validPlanetIds)
        {
            try
            {
                int stage = 0;
                int num = R32(b, ref off);
                if (num < 0 || num > 50) return Fail(stage);
                int astro = R32(b, ref off);
                bool space = astro >= 1000000;
                if (!space)
                {
                    if (!(astro >= 100 && astro < 10000 && astro % 100 != 0)) return Fail(stage);
                    if (validPlanetIds != null && !validPlanetIds.Contains(astro)) return Fail(stage);
                }
                else
                {
                    if (astro >= 1000000 + 64 * 8 + 2) return Fail(stage);
                }
                int seed = R32(b, ref off);
                int rtseed = R32(b, ref off);
                int num2 = R32(b, ref off);
                if (num2 < 0 || num2 > 10000) return Fail(stage);
                int pbuildersActive = 0;
                for (int i = 0; i < num2; i++)
                {
                    int bid;
                    if (!SkipBuilder(b, ref off, out bid)) return Fail(stage);
                    if (i >= 2 && bid > 0) pbuildersActive++;   // 0/1 槽为占位（游戏从 2 起算活跃建筑）
                }
                bool realized = Rb(b, ref off);
                bool isEmpty = Rb(b, ref off);
                int ticks = R32(b, ref off);
                if (ticks < 0) return Fail(stage);
                off += 8;                                   // turboTicks, turboRepress
                off += 1;                                   // matterStatComplete
                off += 4 * 4;                               // matter 4×int
                int rootEnemyId = R32(b, ref off);          // rootEnemyId（0 = 核心已毁）
                off += 1;                                   // isCarrierRealized
                int tindersArriving = R32(b, ref off);
                if (num >= 1) { off += 4; if (num == 1) off += 4; }
                if (num >= 3) off += 4;
                // 9 个组件池（前 8 跳过，tinders 提取）
                stage = 1;
                if (!SkipDataPool(b, ref off, SkipBuilderComp, "builders", out int cBuilders)) return Fail(stage);   // builders
                stage = 2;
                if (!SkipDataPool(b, ref off, SkipCore, "cores", out int cCores)) return Fail(stage);
                stage = 3;
                if (!SkipDataPool(b, ref off, SkipNode, "nodes", out int cNodes)) return Fail(stage);
                stage = 4;
                if (!SkipDataPool(b, ref off, SkipConnector, "connectors", out int cConnectors)) return Fail(stage);
                stage = 5;
                if (!SkipDataPool(b, ref off, SkipReplicator, "replicators", out int cReplicators)) return Fail(stage);
                stage = 6;
                if (!SkipDataPool(b, ref off, SkipGamma, "gammas", out int cGammas)) return Fail(stage);
                stage = 7;
                if (!SkipDataPool(b, ref off, SkipTurret, "turrets", out int cTurrets)) return Fail(stage);
                stage = 8;
                var relays = new List<RelayRec>();
                if (!ReadRelayPool(b, ref off, relays, astro)) return Fail(stage);     // relays 是 ObjectPool（含投放目标行星）
                stage = 9;
                var tinders = new List<TinderRec>();
                if (!ReadTinders(b, ref off, tinders)) return Fail(stage);
                stage = 10;
                if (!SkipDataPool(b, ref off, SkipUnit, "units", out int cUnits)) return Fail(stage);           // units
                // idleRelay / idleTinder
                stage = 11;
                int nRelayCap = R32(b, ref off);
                int nRelay = R32(b, ref off);
                if (nRelay < 0 || nRelay > nRelayCap + 8) return Fail(stage);
                off += nRelay * 4;
                int nTinderCap = R32(b, ref off);
                int nTinder = R32(b, ref off);
                if (nTinder < 0 || nTinder > nTinderCap + 8) return Fail(stage);
                off += nTinder * 4;
                // 3×EnemyFormation + evolve + hatred + hatredAstros（仅跳到 hive 尾；我们不依赖链遍历，失败不致命）
                stage = 12;
                for (int f = 0; f < 3; f++)
                    if (!SkipFormation(b, ref off)) { _ = Fail(stage); break; }
                stage = 13;
                if (!SkipEvolve(b, ref off)) _ = Fail(stage);
                if (!SkipHatred(b, ref off)) _ = Fail(stage);
                if (!SkipHatred(b, ref off)) _ = Fail(stage);
                return new HiveSummary
                {
                    HiveAstroId = astro,
                    IsSpaceHive = space,
                    StarId = space ? (astro - 1000000 - 1) / 8 + 1 : astro / 100,
                    PlanetId = space ? 0 : astro,
                    Seed = seed,
                    Realized = realized,
                    IsEmpty = isEmpty,
                    Ticks = ticks,
                    TindersArrivingInTransit = tindersArriving,
                    TinderCount = tinders.Count,
                    Tinders = tinders,
                    Relays = relays,
                    RootEnemyId = rootEnemyId,
                    PbuildersActive = pbuildersActive,
                    CoresCount = cCores,
                    BuildersCount = cBuilders,
                    NodesCount = cNodes,
                    ReplicatorsCount = cReplicators,
                    TurretsCount = cTurrets,
                    UnitsCount = cUnits,
                    // 存活 = 非空巢 且（未实现 / 核心在 / 有建筑 / 有活跃 relay / 有火种）
                    // 某存档实测：已清巢穴这些信号全 0；201 档活巢穴 builders=824/pbuilders=811
                    IsAlive = !isEmpty && (!realized || rootEnemyId > 0 || pbuildersActive > 0
                              || cCores > 0 || cBuilders > 0 || cNodes > 0
                              || relays.Exists(r => r.BaseState == 1) || tinders.Count > 0),
                };
            }
            catch { return null; }
        }

        static int[] stageFail = new int[20];
        static HiveSummary Fail(int stage)
        {
            try
            {
                if (stage >= 0 && stage < 20 && System.Threading.Interlocked.Increment(ref stageFail[stage]) == 1)
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "hive-diag.txt"), $"  stage {stage} fail\n");
            }
            catch { }
            return null;
        }

        // ---- 基础读取 ----
        static int R32(byte[] b, ref int o) { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
        static bool Rb(byte[] b, ref int o) { bool v = b[o] != 0; o += 1; return v; }
        static int R16(byte[] b, ref int o) { int v = BitConverter.ToInt16(b, o); o += 2; return v; }
        static int RU16(byte[] b, ref int o) { int v = BitConverter.ToUInt16(b, o); o += 2; return v; }
        /// <summary>游戏 FlexibleInt 读取（IOHelper.ReadFlexibleInt，flag 前缀编码）：
        /// 0x00=0；0x01+1B；0x02+2B(ushort)；0x03+3B；0x04+4B(int32)；0x05..0xFF=原值；负数=0x04+int32。
        /// 注意：不是 LEB128（旧实现错用 bit7 编码，instId/savedWorkProgress/stateTick 等 flex 字段
        /// 在值 1~4 与 256~65535 时字节数不同 → 大巢穴 pbuilders 逐条错位的根因）。</summary>
        static int RFlex(byte[] b, ref int o)
        {
            byte b0 = b[o]; o++;
            if (b0 == 0 || b0 > 4) return b0;                       // 0 或 5~255 原值
            switch (b0)
            {
                case 1: return b[o++];                              // byte
                case 2: { int v = b[o] | (b[o + 1] << 8); o += 2; return v; }                     // ushort
                case 3: { int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16); o += 3; return v; }  // 3B
                default: { int v = BitConverter.ToInt32(b, o); o += 4; return v; }                 // int32
            }
        }

        // ---- GrowthPattern Builder ----
        static bool SkipBuilder(byte[] b, ref int o, out int instId)
        {
            instId = 0;
            if (o + 16 >= b.Length) return false;
            o++;                                    // flag
            int protoId = R16(b, ref o);
            instId = RFlex(b, ref o);
            if (instId > 0) o += 2;                  // instBuilderId
            bool isConn = protoId == 8109 || protoId == 8110;
            if (!isConn) RFlex(b, ref o);            // savedWorkProgress
            o += 7 * 4;                              // pos 3f + rot 4f
            if (float.IsNaN(BitConverter.ToSingle(b, o - 8))) return false;
            if (isConn) o += 1 + 4;                  // size byte + length f
            o += 4;                                  // matterNeeded
            o += 2;                                  // matterSpeed
            int conns = R16(b, ref o);
            if (conns < 0 || conns > 60000 || o + conns * 2 > b.Length) return false;
            o += conns * 2;
            o++;                                     // rank
            return true;
        }

        delegate bool SkipFn(byte[] b, ref int o);

        // ---- DataPool<T> 通用 ----
        static bool SkipDataPool(byte[] b, ref int o, SkipFn skipItem, string name, out int count)
        {
            count = 0;
            int cap = R32(b, ref o);
            int cursor = R32(b, ref o);
            int rc = R32(b, ref o);
            if (cap < 0 || cap > 100000 || cursor < 0 || cursor > cap + 1 || rc < 0 || rc > cap + 1 || (cursor == 0 && rc != 0))
            {
                Console.WriteLine($"  [hiveDataPool:{name}] cap={cap} cur={cursor} rc={rc}");
                return false;
            }
            if (cursor > 1 && o > b.Length) return false;
            for (int i = 1; i < cursor; i++)
                if (!skipItem(b, ref o)) return false;
            o += rc * 4;
            count = cursor - rc - 1;
            return true;
        }

        static bool SkipObjectPool(byte[] b, ref int o, SkipFn skipItem)
        {
            int cap = R32(b, ref o);
            int cursor = R32(b, ref o);
            int rc = R32(b, ref o);
            if (cap < 0 || cap > 100000 || cursor < 0 || cursor > cap + 1 || rc < 0 || rc > cap + 1 || (cursor == 0 && rc != 0)) return false;
            for (int i = 1; i < cursor; i++)
            {
                int id = R32(b, ref o);
                if (id != 0)
                {
                    if (id != i) return false;
                    if (!skipItem(b, ref o)) return false;
                }
            }
            o += rc * 4;
            return true;
        }

        // ---- 组件跳过 ----
        static bool SkipBuilderComp(byte[] b, ref int o)
        {
            o += 1; int id = R32(b, ref o);
            if (id <= 0) return true;
            o += 13 * 4;          // enemyId..spEnergy（13×int）
            o += 2;               // state（i16）
            o += 2 * 4;           // idleEnergy workEnergy
            o += 4 * 2;           // builderIndex buildCursor buildChance buildCDTime（4×u16）
            return true;
        }
        static bool SkipCore(byte[] b, ref int o)
        {
            int num = b[o]; o++;
            int id = RU16(b, ref o);
            if (id <= 0) return true;
            o += 16 * 4;              // enemyId..buildTinderTriggerProbability（15×int + 1×float，DFSCoreComponent.Import）
            if (num >= 1) o += 4;     // disableAssaultingAlert
            return true;
        }
        static bool SkipNode(byte[] b, ref int o)
        {
            o += 1; int id = RU16(b, ref o);
            if (id <= 0) return true;
            o += 8; return true;
        }
        static bool SkipConnector(byte[] b, ref int o)
        {
            o += 1; int id = RU16(b, ref o);
            if (id <= 0) return true;
            o += 8 + 8 + 4 + 4; return true;
        }
        static bool SkipReplicator(byte[] b, ref int o)
        {
            o += 1; int id = RU16(b, ref o);
            if (id <= 0) return true;
            o += 8 + 2 + 1 + 2 + 4 + 2 + 2 + 1;
            o += 3 * 4 + 4 * 4 + 3 * 4 + 2 + 2;
            return true;
        }
        static bool SkipGamma(byte[] b, ref int o)
        {
            o += 1; int id = RU16(b, ref o);
            if (id <= 0) return true;
            o += 8; return true;
        }
        static bool SkipTurret(byte[] b, ref int o)
        {
            o += 1; int id = RU16(b, ref o);
            if (id <= 0) return true;
            o += 4 + 4 + 4;          // enemyId builderId level
            o += 1 + 1 + 1 + 4;      // state type target.type target.id
            o += 3 * 4 + 4;          // localDir muzzleY
            o += 4 + 4 + 4 + 4;      // muzzleReset attackRange sensorRange rangeInc
            o += 4 + 4 + 4;          // fire heat aimt
            return true;
        }
        static bool SkipRelay(byte[] b, ref int o)
        {
            int num = b[o]; o++;
            int id = RU16(b, ref o);
            if (id > 0)
            {
                o += 8 + 2 + 8;            // enemyId builderId dockIndex hiveAstroId targetAstroId
                o += 3 * 4 + 4;            // targetLPos targetYaw
                o += 1 + 2 + 4;            // baseState baseId baseTicks
                if (!SkipEvolve(b, ref o)) return false;   // baseEvolve
                o += 2 + 1 + 1 + 4 + 4;    // baseRespawnCD direction stage uSpeed param0
            }
            int num2 = b[o]; o++;
            if (num2 > 6) return false;
            for (int i = 0; i < 6; i++)
                if (!SkipCarrier(b, ref o)) return false;
            o += 4 * 3 + 3 * 4 + 4;        // searchAstroId searchBaseId searchChance searchLPos searchEntityCursor
            if (num >= 1)
                o += 8 + 3 * 4 + 4;        // dstMarker 3 int + 3f + yaw
            return true;
        }

        // ---- Relay 提取（DFRelayComponent.Import：targetAstroId = 地面基地所在行星）----
        static bool ReadRelayPool(byte[] b, ref int o, List<RelayRec> relays, int hiveAstroId)
        {
            int cap = R32(b, ref o);
            int cursor = R32(b, ref o);
            int rc = R32(b, ref o);
            if (cap < 0 || cap > 100000 || cursor < 0 || cursor > cap + 1 || rc < 0 || rc > cap + 1 || (cursor == 0 && rc != 0)) return false;
            for (int i = 1; i < cursor; i++)
            {
                int id = R32(b, ref o);
                if (id != 0)
                {
                    if (id != i) return false;
                    if (!ReadRelay(b, ref o, relays, hiveAstroId)) return false;
                }
            }
            o += rc * 4;
            return true;
        }
        static bool ReadRelay(byte[] b, ref int o, List<RelayRec> relays, int hiveAstroId)
        {
            int num = b[o]; o++;
            int id = RU16(b, ref o);
            int target = 0, baseState = 0, stage = 0;
            if (id > 0)
            {
                o += 4 + 4 + 2 + 4;        // enemyId builderId dockIndex hiveAstroId
                target = R32(b, ref o);    // targetAstroId（投放目标行星）
                o += 3 * 4 + 4;            // targetLPos targetYaw
                baseState = b[o]; o++;     // baseState（1=已投放）
                o += 2 + 4;                // baseId baseTicks
                if (!SkipEvolve(b, ref o)) return false;   // baseEvolve
                o += 2 + 1;                // baseRespawnCD direction
                stage = b[o]; o++;         // stage（2=投放完成）
                o += 4 + 4;                // uSpeed param0
            }
            int num2 = b[o]; o++;
            if (num2 > 6) return false;
            for (int i = 0; i < 6; i++)
                if (!SkipCarrier(b, ref o)) return false;
            o += 4 * 3 + 3 * 4 + 4;        // searchAstroId searchBaseId searchChance searchLPos searchEntityCursor
            if (num >= 1)
                o += 8 + 3 * 4 + 4;        // dstMarker
            relays.Add(new RelayRec { HiveAstroId = hiveAstroId, RelayId = id, TargetAstroId = target, BaseState = baseState, Stage = stage });
            return true;
        }
        static bool SkipUnit(byte[] b, ref int o)
        {
            int num = b[o]; o++;
            return SkipUnitExact(b, ref o, num);
        }
        static bool SkipUnitExact(byte[] b, ref int o, int num)
        {
            // id/planetId/enemyId 3×flex；protoId/level 2×i16；baseId/baseEnemyId 2×u16；behavior byte；stateTick flex
            RFlex(b, ref o); RFlex(b, ref o); RFlex(b, ref o);
            o += 2 + 2 + 2 + 2 + 1;
            RFlex(b, ref o);
            o += 2 + 2 + 2 + 2;            // fire0/1/2 heat
            o += 3 * 4;                    // repulsion
            if (!SkipTargetQueue(b, ref o)) return false;
            if (!SkipHatred(b, ref o)) return false;
            if (num >= 1) o += 8;
            if (num >= 2) o += 4;
            return true;
        }

        // ---- 子结构 ----
        static bool SkipEvolve(byte[] b, ref int o)
        {
            int num = b[o]; o++;
            o += 10 * 4;   // level..waveTicks（10×int，EvolveData.Import）
            if (num >= 1) o += 4;
            o += 4;   // rankBase
            return true;
        }
        static bool SkipTargetQueue(byte[] b, ref int o)
        {
            o += 1;
            int count = b[o]; o++;
            if (count > 0) o += 12;
            if (count > 1) o += 12;
            o += 12;
            return true;
        }
        static bool SkipHatred(byte[] b, ref int o)
        {
            o += 1 + 16 * 4;
            return true;
        }
        static bool SkipCarrier(byte[] b, ref int o)
        {
            o += 1 + 1 + 1 + 8;            // flag stage direction matter energy
            o += 3 * 8 + 4 * 4;            // upos 3d urot 4f
            o += 4 + 1 + 4 + 4 + 4;        // speed nearStart param0 param1 hp
            return true;
        }
        static bool SkipFormation(byte[] b, ref int o)
        {
            o += 1;
            int port = RU16(b, ref o);
            int vac = RU16(b, ref o);
            if (port > 65535 || vac > 65535 || o + port > b.Length) return false;
            o += port;
            if (port <= 255) o += vac;
            else o += vac * 2;
            return true;
        }

        // ---- tinder 提取（DFTinderComponent.Import）----
        static bool ReadTinders(byte[] b, ref int o, List<TinderRec> list)
        {
            int cap = R32(b, ref o);
            int cursor = R32(b, ref o);
            int rc = R32(b, ref o);
            if (cap < 0 || cap > 100000 || cursor < 0 || cursor > cap + 1 || rc < 0 || rc > cap + 1 || (cursor == 0 && rc != 0)) return false;
            for (int i = 1; i < cursor; i++)
            {
                o += 1;                    // flag
                int id = RU16(b, ref o);
                if (id > 0)
                {
                    int enemyId = R32(b, ref o);
                    int builderId = R32(b, ref o);
                    o += 2;                // dockIndex
                    int origin = R32(b, ref o);
                    int target = R32(b, ref o);
                    int direction = R16(b, ref o);
                    int stage = R16(b, ref o);
                    float uSpeed = BitConverter.ToSingle(b, o); o += 4;
                    float param0 = BitConverter.ToSingle(b, o); o += 4;
                    list.Add(new TinderRec
                    {
                        EnemyId = enemyId,
                        OriginHiveAstroId = origin,
                        TargetHiveAstroId = target,
                        Direction = (short)direction,
                        Stage = (short)stage,
                        USpeed = uSpeed,
                        Param0 = param0,
                    });
                }
            }
            o += rc * 4;
            return true;
        }
    }
}









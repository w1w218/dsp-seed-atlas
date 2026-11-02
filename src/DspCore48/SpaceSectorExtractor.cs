using System;
using System.Collections.Generic;
using System.IO;

namespace DspCore
{
    /// <summary>SpaceSector 轻量提取：astros（含太空巢穴 uPos）+ enemyPool 火种实体位置。
    /// 目的：给 tinders.csv 补"火种当前坐标 / 到目的星球距离"（DFTinderComponent 自身无位置字段，
    /// 位置在 enemyPool 动态实体 EnemyData.pos）。定位复用 DysonExtractor（dyson 段尾 = spaceSector 起点）。</summary>
    public static class SpaceSectorExtractor
    {
        public class AstroPos { public int AstroId; public double X, Y, Z; }
        public class EnemyPos
        {
            public int EnemyId;
            public int AstroId;
            public bool IsSpace;
            public bool Dynamic;
            public double X, Y, Z;
            public int DfTinderId;      // >0 = 火种实体
            public int DfRelayId;
        }

        static int R32(byte[] b, ref int o) { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
        static int RU16(byte[] b, ref int o) { int v = BitConverter.ToUInt16(b, o); o += 2; return v; }
        static int RFlex(byte[] b, ref int o)
        {
            byte b0 = b[o]; o++;
            if (b0 == 0 || b0 > 4) return b0;
            switch (b0)
            {
                case 1: return b[o++];
                case 2: { int v = b[o] | (b[o + 1] << 8); o += 2; return v; }
                case 3: { int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16); o += 3; return v; }
                default: { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            }
        }
        static double RD(byte[] b, ref int o) { double v = BitConverter.ToDouble(b, o); o += 8; return v; }
        static float RF(byte[] b, ref int o) { float v = BitConverter.ToSingle(b, o); o += 4; return v; }

        /// <summary>提取 astros 位置 + enemyPool 实体（重点：火种 dfTinderId&gt;0）。</summary>
        public static (List<AstroPos> Astros, List<EnemyPos> Enemies, long SpaceSectorOffset) Extract(string savePath, int starCount)
        {
            byte[] buf = File.ReadAllBytes(savePath);
            var dysons = DysonExtractor.Extract(savePath, starCount, null);
            int lastIdx = -1; long lastEnd = -1;
            foreach (var d in dysons)
                if (d.StarIndex > lastIdx) { lastIdx = d.StarIndex; lastEnd = d.EndOffset; }
            if (lastEnd < 0) throw new InvalidOperationException("dyson 段未定位，无法定位 spaceSector");
            long spaceStart = lastEnd + (starCount - 1 - lastIdx) * 4L;
            if (spaceStart < 0 || spaceStart >= buf.Length) throw new InvalidOperationException("spaceSector 定位越界");

            var astros = new List<AstroPos>();
            var enemies = new List<EnemyPos>();
            int o = (int)spaceStart;
            int num = R32(buf, ref o);
            int astroCap = R32(buf, ref o), astroCur = R32(buf, ref o);
            if (num < 1 || astroCap < astroCur || astroCur <= 1)
                throw new InvalidOperationException($"spaceSector 头校验失败 num={num} cap={astroCap} cur={astroCur}");
            for (int i = 1; i < astroCur; i++)
            {
                int id = R32(buf, ref o);
                if (id > 0)
                {
                    R32(buf, ref o);                 // type
                    R32(buf, ref o);                 // parentId
                    double x = RD(buf, ref o), y = RD(buf, ref o), z = RD(buf, ref o);
                    RD(buf, ref o); RD(buf, ref o); RD(buf, ref o);   // uPosNext
                    o += 16 + 16;                    // uRot uRotNext
                    RF(buf, ref o);                  // uRadius
                    astros.Add(new AstroPos { AstroId = id, X = x, Y = y, Z = z });
                }
            }
            int eCap = R32(buf, ref o), eCur = R32(buf, ref o), eRc = R32(buf, ref o);
            if (eCap < 0 || eCap > 1000000 || eCur < 0 || eCur > eCap + 1)
                throw new InvalidOperationException($"enemyPool 头异常 cap={eCap} cur={eCur}");
            for (int j = 1; j < eCur; j++)
            {
                int s = o;
                o++;                                 // flag
                int id = R32(buf, ref o);
                if (id <= 0) continue;
                RU16(buf, ref o); RU16(buf, ref o);  // protoId modelIndex
                int astroId = R32(buf, ref o);
                R32(buf, ref o);                     // originAstroId
                RU16(buf, ref o); RU16(buf, ref o);  // owner port
                bool dyn = buf[o++] != 0;
                bool sp = buf[o++] != 0;
                o += 2;                              // localized stateFlags
                double px = 0, py = 0, pz = 0;
                if (sp) { px = RD(buf, ref o); py = RD(buf, ref o); pz = RD(buf, ref o); }
                else { px = RF(buf, ref o); py = RF(buf, ref o); pz = RF(buf, ref o); }
                o += 16;                             // rot
                if (dyn) o += 12;                    // vel
                o += 4 + 4 + 4 + 2;                  // hash hashAddress combatStatId builderId
                int tinderId = 0, relayId = 0;
                if (!sp && !dyn) { o += 10; }        // 5×u16（dfGBase 等）
                else if (sp && !dyn) { o += 12; }    // 6×u16
                else { tinderId = RU16(buf, ref o); relayId = RU16(buf, ref o); }
                RFlex(buf, ref o);                   // unitId
                enemies.Add(new EnemyPos
                {
                    EnemyId = id, AstroId = astroId, IsSpace = sp, Dynamic = dyn,
                    X = px, Y = py, Z = pz, DfTinderId = tinderId, DfRelayId = relayId,
                });
            }
            return (astros, enemies, spaceStart);
        }
    }
}
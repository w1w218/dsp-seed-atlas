# -*- coding: utf-8 -*-
"""DSP 存档解析（最终版）：7-bit 变长字符串；解析头/account/GameData 到 gameDesc 验证 seed；
定位 galaxy runtime 已扫描行星段（[astroId][0][landPercent][12f][count][VeinGroup×count][-1]），
输出陆海比与每矿种组数/储量。"""
import sys, struct

if len(sys.argv) < 2:
    print("用法: python parse_dsv.py <存档.dsv> [planetids.txt]")
    sys.exit(1)
SAVE = sys.argv[1]
IDS_FILE = sys.argv[2] if len(sys.argv) > 2 else None
PLANET_IDS = set()
if IDS_FILE:
    for line in open(IDS_FILE, encoding="utf-8"):
        if line.startswith("PLANETIDS "):
            PLANET_IDS = set(int(x) for x in line.strip().split(" ")[1].split(","))
            break

NAME = {1: "铁矿", 2: "铜矿", 3: "硅石", 4: "钛石", 5: "石矿", 6: "煤矿", 7: "原油",
        8: "可燃冰", 9: "金伯利矿石", 10: "分形硅石", 11: "有机晶体", 12: "光栅石",
        13: "刺笋结晶", 14: "单极磁石"}

class R:
    def __init__(self, b, o=0):
        self.b = b; self.o = o
    def i(self): v = struct.unpack_from("<i", self.b, self.o)[0]; self.o += 4; return v
    def u8(self): v = struct.unpack_from("<Q", self.b, self.o)[0]; self.o += 8; return v
    def f(self): v = struct.unpack_from("<f", self.b, self.o)[0]; self.o += 4; return v
    def b1(self): v = self.b[self.o]; self.o += 1; return v
    def skip(self, n): self.o += n
    def s7(self):
        shift = 0; n = 0
        while True:
            byte = self.b[self.o]; self.o += 1
            n |= (byte & 0x7F) << shift
            if not (byte & 0x80): break
            shift += 7
        v = self.b[self.o:self.o+n].decode("utf-8", errors="replace"); self.o += n
        return v

def main():
    data = open(SAVE, "rb").read()
    # 头
    o = 6 + 8
    ver = struct.unpack_from("<i", data, o)[0]; o += 4
    if ver >= 7: o += 2
    o += 12
    if ver >= 6: o += 4
    o += 16
    nshot = struct.unpack_from("<i", data, o)[0]; o += 4
    if nshot > 0: o += nshot
    # 账号区（含 7-bit 字符串）
    r = R(data)
    r.o = o
    if ver >= 5:
        r.skip(4); r.skip(4); r.skip(8); r.s7(); r.skip(8)   # account.NULL.Import + uint64
    # GameData
    num = r.i(); patch = r.i()
    print("GameData 版本=%d patch=%d" % (num, patch))
    r.skip(4); r.skip(4); r.skip(8); r.s7()   # account.Import
    gameName = r.s7()
    print("gameName =", repr(gameName))
    # gameDesc
    gd = r.i()
    if gd >= 3: r.skip(8)
    if gd >= 5:
        if gd >= 6: r.skip(16)
        else: r.skip(12)
    galaxyAlgo = r.i(); galaxySeed = r.i(); starCount = r.i()
    print(">>> galaxyAlgo=%d galaxySeed=%d starCount=%d" % (galaxyAlgo, galaxySeed, starCount))
    if gd >= 10: r.skip(4)
    r.skip(4)
    if gd >= 2: r.f()
    if gd >= 1:
        n = r.i(); themeIds = [r.i() for _ in range(n)]
        print("savedThemeIds: %d 个" % n)
    if gd >= 4:
        r.skip(3)          # 3 bool
        r.skip(4 + 9 * 4)  # combatSettings: 0 + 9f
    if gd >= 5:
        r.skip(4)          # goalLevel
    gameTick = r.u8()
    print("gameTick =", gameTick)

    # achievement（开局应 0 条）
    av = r.i(); acnt = r.i()
    print("achievement: v=%d count=%d" % (av, acnt))
    if acnt > 0:
        print("  ⚠ 有成就不计，后续偏移不可靠；跳过定位改用搜索")
    search_from = r.o

    # 定位 scanned 行星段：[astroId][0][landPercent][12f][count][groups][-1]
    hits = []
    for p in range(0, len(data) - 12, 2):
        if data[p+4:p+8] == b"\x00\x00\x00\x00":
            land = struct.unpack_from("<f", data, p + 8)[0]
            if 0.10 <= land <= 0.95:
                astro = struct.unpack_from("<i", data, p)[0]
                if PLANET_IDS and astro not in PLANET_IDS:
                    continue
                if 100 <= astro < 10000 and astro % 100 != 0:
                    hits.append((p, astro, land))
    print("scanned 段候选:", len(hits))
    for p, astro, land in hits[:30]:
        vbs = struct.unpack_from("<3f", data, p + 12)
        if not all(-3 <= v <= 3 for v in vbs):
            continue
        count = struct.unpack_from("<i", data, p + 60)[0]
        if count < 2 or count > 300:   # 草原等主题单行星可达 50 组，旧上限 40 会整颗漏掉
            continue
        # 解析 groups：数组 Length 含 None 占位槽（索引 0 不落盘），实际组数 = count-1（32B/组）
        off = p + 64
        groups = []
        ok = True
        for _ in range(count - 1):
            z, vtype = struct.unpack_from("<ii", data, off)
            posv = struct.unpack_from("<3f", data, off + 8)
            cnt, amt = struct.unpack_from("<iq", data, off + 20)
            if z != 0 or not (1 <= vtype <= 14) or cnt < 0 or amt <= 0:
                ok = False
                break
            groups.append((vtype, cnt, amt, posv))
            off += 32
        if not ok:
            continue
        end_marker = struct.unpack_from("<i", data, off)[0]
        print("\n=== 已扫描行星 astroId=%d 陆海比=%.1f%%（veinBias=%s）===" % (
            astro, land * 100, tuple(round(x, 3) for x in vbs)))
        print("  矿脉组 %d 组：" % count)
        for vtype, cnt, amt, posv in groups:
            print("    %-8s 组数=%-4d 储量=%-14d @%s" % (NAME.get(vtype, "?"), cnt, amt, tuple(round(x, 3) for x in posv)))
        print("  段结束标记 = %d（-1 期望）" % end_marker)


if __name__ == "__main__":
    main()
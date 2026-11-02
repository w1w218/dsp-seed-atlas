# -*- coding: utf-8 -*-
"""对比存档真实储量与完整建模，输出每行星每矿种差异。用法: python _compare2.py <save-planets.csv> <model.csv>"""
import csv, sys

NAME = {1:('铁矿','铁'),2:('铜矿','铜'),3:('硅石','硅'),4:('钛石','钛'),5:('石矿','石'),6:('煤矿','煤'),7:('原油','油'),8:('可燃冰','燃冰'),9:('金伯利矿石','金伯利'),10:('分形硅石','分形硅'),11:('有机晶体','有机晶'),12:('光栅石','光栅石'),13:('刺笋结晶','刺笋'),14:('单极磁石','单极磁')}

def load(path):
    with open(path, encoding='utf-8-sig') as f:
        return list(csv.DictReader(f))

if len(sys.argv) < 3:
    print('用法: python _compare2.py <save-planets.csv> <model.csv>')
    sys.exit(1)
save_path, model_path = sys.argv[1], sys.argv[2]
save = load(save_path)
model = load(model_path)
print(f'存档行星行: {len(save)}  建模行星行: {len(model)}')

sm = {}
for r in save:
    if r.get('已扫描') == '是':
        sm[int(r['行星id'])] = r
mm = {}
for r in model:
    mm[int(r['行星id'])] = r

def gval(r, key, as_amount=False):
    try:
        if as_amount:
            return int(float(r.get(key) or 0) * 25000)
        return int(r.get(key) or 0)
    except ValueError:
        return None

diff_total = 0
diff_planets = 0
rows = []
for pid in sorted(mm):
    if pid not in sm:
        continue
    s, m = sm[pid], mm[pid]
    diffs = []
    for v in range(1, 15):
        sk, mk = f'{NAME[v][0]}组', f'{NAME[v][1]}组'
        ss, ms = f'{NAME[v][0]}储量', f'{NAME[v][1]}储量'
        sg, mg = gval(s, sk), gval(m, mk)
        sa, ma = gval(s, ss, as_amount=(v == 7)), gval(m, ms)
        if sg is None or mg is None:
            continue
        sa = sa or 0
        ma = ma or 0
        if sg != mg or sa != ma:
            ratio = (ma - sa) / sa * 100 if sa else 0
            diffs.append(f'{NAME[v][1]}: 存档{sg}组/{sa:,} 建模{mg}组/{ma:,} ({ratio:+.1f}%)')
    if diffs:
        diff_total += 1
        rows.append((pid, s.get('主题'), diffs))

for pid, theme, diffs in rows:
    print(f'行星 {pid} {theme}:')
    for d in diffs:
        print(f'    {d}')

print(f'\n有差异行星数: {diff_total} / {len([p for p in mm if p in sm])} 已扫描对拍')

/* 星系浏览器 —— 表格 + 3D 星系图 + 规划编辑 */
(() => {
  'use strict';

  // ============ 常量 ============
  const VEINS = [
    [1, '铁'], [2, '铜'], [3, '硅'], [4, '钛'], [5, '石'], [6, '煤'], [7, '油'],
    [8, '可燃冰'], [9, '金伯利'], [10, '分形硅'], [11, '有机晶'], [12, '光栅石'],
    [13, '刺笋'], [14, '单极磁'],
  ];
  // 恒星本体/光晕颜色（按光谱；星系视图是暗底亮点，需要饱和色相）
  const SPECTR_COLOR = {
    O: 0x66c8ff, B: 0x9fc0ff, A: 0xdfe8ff, F: 0xfff2d8, G: 0xffd75e, K: 0xff9d4d, M: 0xff6b3d,
  };
  // 行星颜色（按 EPlanetType；注意游戏拼写是 Vocano 不是 Lava）
  const PLANET_COLOR = {
    Gas: 0x7fe0b0, Vocano: 0xe0663f, Lava: 0xe0663f, Ocean: 0x35b8c8, Desert: 0xd8a878, Ice: 0xcfe8f5,
  };

  // ============ 尺寸体系（世界单位；轨道归一化到 46）============
  // 游戏 realRadius = radius×scale：普通行星 200×1=200，气巨 80×10=800
  const realRadius = pl => (pl.isGasGiant ? (pl.radius || 80) * 10 : (pl.radius || 200));
  // 视觉半径用 sqrt 压缩：普通行星 1.0、气巨 2.0（真实 200/800 的 1:4 压成 1:2，避免内圈行星互相吃掉）
  const planetRadius = pl => Math.sqrt(realRadius(pl) / 200);
  const starRadius = (lum, st) => {
    if (st.type === 'BlackHole') return 2.0;
    if (st.type === 'NeutronStar') return 2.2;
    if (st.type === 'WhiteDwarf') return 2.6;
    const base = 3.2 + Math.log10(Math.max(0.005, lum || 1) + 1) * 1.2;   // G≈3.56 O≈4.7
    return st.type === 'GiantStar' ? base * 2.4 : base;                  // 巨星明显更大
  };
  const MOON_RADIUS = 0.45;
  const ORBIT_COLOR = 0x2f8f8f;

  // ============ 状态 ============
  let G = null;               // galaxy.json
  let ROWS = [];              // 扁平行星行
  let VIEW = [];              // 当前筛选结果
  let sortKey = 'dist', sortDir = 1;   // 默认按距离升序
  let selectedPid = null;
  const isHttp = location.protocol === 'http:' || location.protocol === 'https:';

  // ============ 格式化 ============
  const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const fmtAmt = n => {
    if (!n) return '';
    if (n >= 1e8) return (n / 1e8).toFixed(2) + '亿';
    if (n >= 1e4) return (n / 1e4).toFixed(1) + '万';
    return String(n);
  };
  const fmtInt = n => (!n ? '' : n.toLocaleString('en-US'));
  const OIL_DIV = 25000;   // 油：amount / 25000 = 流速 /s（SAVE-FORMAT 口径）
  const veinCell = (v, t) => {
    if (!v) return '';
    let planned = v.plannedAmount || 0;      // 已规划采集（有采矿机，蓝）
    let unplanned = v.unplannedAmount || 0;  // 未规划采集（无采矿机，绿=开采潜力）
    const isOil = t === 7;
    const f = n => (isOil ? (n / OIL_DIV).toFixed(2) : fmtAmt(n));
    const u = isOil ? '/s' : '';
    if (!planned && !unplanned) {
      // 非工厂口径（存档矿组 / 模型推算）没有采矿机信息 → 整块储量计入未规划
      if (v.amountSource === 'factory') return '';   // 工厂口径且都为 0 = 已采空
      const amt = v.currentAmount || v.initialAmount || 0;
      if (!amt) return '';
      // 模型推算（未探索行星）：白色，区别于存档实测
      const cls = v.amountSource === 'model' ? 'calc' : 'un';
      return `<span class="${cls}">${f(amt)}${u}</span>`;
    }
    // 采空预警：已规划+未规划 < 初始储量的 10% → 已规划数字标红
    const init = v.initialAmount || 0;
    const low = init > 0 && (planned + unplanned) < init * 0.1;
    const pCls = low ? 'pl low' : 'pl';
    if (planned && unplanned)
      return `<span class="${pCls}">${f(planned)}${u}</span>/<span class="un">${f(unplanned)}${u}</span>`;
    if (planned) return `<span class="${pCls}">${f(planned)}${u}</span>`;
    return `<span class="un">${f(unplanned)}${u}</span>`;
  };
  // 气巨/冰巨气体：每采集站速率 × 40 站 × 60s → /min
  const gasRate = (r, itemId) => {
    const g = r.pl.gas;
    if (!g || !g.items) return 0;
    const i = g.items.indexOf(itemId);
    return i < 0 ? 0 : g.speeds[i] * 40 * 60;
  };
  const gasCell = (r, itemId) => {
    const v = gasRate(r, itemId);
    return v > 0 ? Math.round(v).toLocaleString('en-US') : '';
  };
  const starTypeText = st => {
    switch (st.type) {
      case 'BlackHole': return '黑洞';
      case 'NeutronStar': return '中子星';
      case 'WhiteDwarf': return '白矮星';
      case 'GiantStar': return '巨星';
      default: return (st.spectr || '?') + '型恒星';
    }
  };
  const starColor = st => {
    if (st.type === 'BlackHole') return 0xd94bff;      // 紫红光球（游戏星图观感，不是黑球）
    if (st.type === 'NeutronStar') return 0x9a86ff;    // 紫蓝
    if (st.type === 'GiantStar') {                     // 巨星按光谱：M/K 橙红，B/A/O 蓝（蓝巨星同款形状）
      return (st.spectr === 'M' || st.spectr === 'K') ? 0xff6a20 : (SPECTR_COLOR[st.spectr] ?? 0xffb070);
    }
    if (st.type === 'WhiteDwarf') return 0xeaf2ff;
    return SPECTR_COLOR[st.spectr] ?? 0xdddddd;
  };
  const remarkText = r => {
    const out = [];
    if (r.dyson) {
      const d = r.dyson;
      let s = `戴森球 ${d.layers}层`;
      if (d.built != null) s += `（空${d.empty}·建${d.built}）`;
      else if (d.building) s += `·在建${d.building}`;
      else if (d.done) s += '·完成';
      else s += '·空';
      out.push(s);
    }
    if (r.acid) out.push('硫酸');
    if (r.gas) out.push(r.collectors != null ? `采集站 ${r.collectors}/40` : '采集站 —');
    return out.join(' · ');
  };

  // ============ 列定义（24 列） ============
  const COLS = [
    { k: 'star',   t: '恒星',     w: 118, star: true, v: r => esc(r.starName), s: r => r.starName },
    { k: 'name',   t: '行星名',   w: 150, v: r => esc(r.pl.name), s: r => r.pl.name },
    { k: 'theme',  t: '主题',     w: 82,  v: r => esc(r.pl.themeName || ''), s: r => r.pl.themeName || '' },
    { k: 'space',  t: '开阔度',   w: 58, num: true, v: r => (r.pl.scanned ? (r.pl.spaceLevel ?? '—') : '—'), s: r => r.pl.spaceLevel ?? -1 },
    { k: 'plan',   t: '规划',     w: 132, cls: r => 'plan ' + (r.built ? 'built' : 'unbuilt'), v: r => esc(r.planText), s: r => r.planText, edit: true },
    { k: 'dist',   t: '距离',     w: 58, num: true, v: r => r.st.distanceFromBirth.toFixed(2), s: r => r.st.distanceFromBirth },
    { k: 'remark', t: '备注',     w: 150, v: r => esc(remarkText(r)), s: r => remarkText(r) },
    { k: 'bh',     t: '黑雾',     w: 56, num: true,
      cls: r => 'bh' + (r.hiveCount ? ' bh-' + Math.min(6, r.hiveCount) : ''),
      v: r => (r.bhCount ? r.bhCount : ''), s: r => r.bhCount },
    ...VEINS.map(([t, n]) => ({
      k: 'v' + t, t: n, w: 76, num: true,
      v: r => veinCell(r.veins[t], t),
      s: r => r.veins[t]?.currentAmount || 0,
    })),
    { k: 'h2', t: '氢/min', w: 78, num: true, v: r => gasCell(r, 1120), s: r => gasRate(r, 1120) },
    { k: 'd2', t: '重氢/min', w: 78, num: true, v: r => gasCell(r, 1121), s: r => gasRate(r, 1121) },
    { k: 'fi', t: '可燃冰气/min', w: 92, num: true, v: r => gasCell(r, 1011), s: r => gasRate(r, 1011) },
  ];

  // ============ 数据装载 ============
  async function loadData() {
    let g = null;
    if (isHttp) {
      try {
        const resp = await fetch('/api/galaxy', { cache: 'no-store' });
        if (resp.ok) g = await resp.json();
      } catch (e) { /* 回退到 galaxy-data.js */ }
    }
    if (!g) g = window.GALAXY;
    // 气巨/冰巨不可登陆 → 存档 scanned 恒 false（采集站在轨道上）；
    // 有采集站/工厂的视为已探索（否则「建了 40 个采集站却显示未探索」）
    if (g && g.stars) for (const st of g.stars) for (const p of st.planets)
      if (!p.scanned && (p.hasFactory || (p.collectors > 0))) p.scanned = true;
    return g;
  }

  function buildRows() {
    ROWS = [];
    const slotsByPlanet = {};
    ((G.plan && G.plan.slots) || []).forEach(s => {
      (slotsByPlanet[s.planetId] = slotsByPlanet[s.planetId] || []).push(s);
    });
    G.stars.forEach(st => {
      st.planets.forEach(pl => {
        const veins = {};
        (pl.veins || []).forEach(v => { veins[v.type] = v; });
        const slots = slotsByPlanet[pl.id] || [];
        ROWS.push({
          st, pl, veins, slots,
          starName: st.name, starNo: st.id,
          planText: (pl.planNote != null && pl.planNote !== '') ? pl.planNote : slots.map(s => s.id).join(' / '),
          built: !!pl.hasFactory,
          bhCount: Math.max(pl.blackHoleBases || 0, pl.blackHoleBasesRelay || 0),
          hiveCount: st.hiveCount || 0,
          dyson: st.dyson || null,
          acid: pl.waterItemId === 1116,
          collectors: pl.collectors,
          gas: pl.type === 'Gas',
        });
      });
    });
  }

  // ============ 顶部预警（火种） ============
  function renderAlert() {
    const el = document.getElementById('alert');
    const tinders = (G.meta && G.meta.save && G.meta.save.tinders) || [];
    if (!tinders.length) {
      el.className = 'alert ok';
      el.textContent = `无火种威胁　·　${G.meta.save ? G.meta.save.gameName + '  tick ' + G.meta.save.gameTick : '未融合存档'}`;
      return;
    }
    const withEta = tinders.filter(t => t.etaMinutes != null).sort((a, b) => a.etaMinutes - b.etaMinutes);
    const danger = withEta.find(t => t.etaMinutes <= 10);
    if (danger) {
      el.className = 'alert danger';
      el.textContent = `⚠ 火种袭击：目标 ${danger.targetHive}，ETA ${danger.etaMinutes} 分钟（共 ${tinders.length} 个火种）`;
    } else if (withEta.length) {
      el.className = 'alert warn';
      el.textContent = `火种在途 ${withEta.length} 个，最近 ETA ${withEta[0].etaMinutes} 分钟`;
    } else {
      el.className = 'alert warn';
      el.textContent = `火种在途 ${tinders.length} 个（无 ETA 数据）`;
    }
  }

  // ============ 筛选 / 排序 / 表格 ============
  function applyFilter() {
    const q = document.getElementById('search').value.trim().toLowerCase();
    const star = document.getElementById('f-star').value;
    const theme = document.getElementById('f-theme').value;
    const onlyScanned = document.getElementById('f-scanned').checked;
    const onlyHive = document.getElementById('f-hive').checked;
    const onlyPlan = document.getElementById('f-plan').checked;
    const onlyDyson = document.getElementById('f-dyson').checked;

    VIEW = ROWS.filter(r => {
      if (onlyScanned && !r.pl.scanned) return false;
      if (onlyHive && !r.hiveCount) return false;
      if (onlyPlan && !r.planText) return false;
      if (onlyDyson && !r.dyson) return false;
      if (star && String(r.starNo) !== star) return false;
      if (theme && r.pl.themeName !== theme) return false;
      if (q) {
        const hay = `${r.pl.name} ${r.starName} ${r.pl.id} ${r.planText}`.toLowerCase();
        if (!hay.includes(q)) return false;
      }
      return true;
    });

    if (sortKey) {
      const col = COLS.find(c => c.k === sortKey);
      if (col && col.s) {
        VIEW.sort((a, b) => {
          const x = col.s(a), y = col.s(b);
          if (typeof x === 'number' && typeof y === 'number') return (x - y) * sortDir;
          return String(x).localeCompare(String(y), 'zh-CN') * sortDir;
        });
      }
    }
    renderTable();
  }

  /** 恒星格：rowspan = 该恒星可见行星数；1 个行星只显示名，≥2 个显示完整三行（名/类型/光度） */
  function starCellHtml(rows) {
    const st = rows[0].st;
    const lines = [`<b>${esc(st.name)}</b>`];
    if (rows.length >= 2) {
      lines.push(`<span class="dim">${esc(starTypeText(st))}</span>`);
      if (st.luminosity) lines.push(`<span class="dim">${st.luminosity.toFixed(3)}</span>`);
    }
    return lines.join('<br>');
  }

  /** 按恒星分组（组内保持当前排序顺序；组顺序 = 该星首行在 VIEW 中的位置） */
  function groupView() {
    const groups = new Map();
    VIEW.forEach(r => {
      if (!groups.has(r.starNo)) groups.set(r.starNo, []);
      groups.get(r.starNo).push(r);
    });
    return [...groups.values()];
  }

  function renderTable() {
    const th = COLS.map(c =>
      `<th data-k="${c.k}" class="${c.num ? 'num' : ''}${sortKey === c.k ? ' sorted' + (sortDir < 0 ? ' desc' : '') : ''}" style="width:${c.w}px;min-width:${c.w}px;max-width:${c.w}px">${c.t}</th>`
    ).join('');
    document.querySelector('#tbl thead').innerHTML = '<tr>' + th + '</tr>';

    const body = groupView().map(rows => rows.map((r, i) => {
      const tds = COLS.map(c => {
        const cls = [c.num ? 'num' : '', c.cls ? c.cls(r) : ''].filter(Boolean).join(' ');
        if (c.star) {
          if (i > 0) return '';          // 同恒星后续行不再输出恒星格（由 rowspan 覆盖）
          return `<td data-k="star" class="star-frozen" rowspan="${rows.length}">${starCellHtml(rows)}</td>`;
        }
        const extra = c.k === 'name' ? ' name-frozen' : '';
        return `<td data-k="${c.k}" class="${cls}${extra}">${c.v(r)}</td>`;
      }).join('');
      const cs = [r.pl.scanned ? '' : 'unexplored', selectedPid === r.pl.id ? 'selected' : ''].filter(Boolean).join(' ');
      return `<tr data-pid="${r.pl.id}" class="${cs}">${tds}</tr>`;
    }).join('')).join('');
    document.querySelector('#tbl tbody').innerHTML = body;

    document.getElementById('count').textContent = `${VIEW.length} / ${ROWS.length} 行星`;
    sync3DSelection();
  }

  function initControls() {
    const stars = [...new Map(ROWS.map(r => [r.starNo, r.starName])).entries()]
      .sort((a, b) => a[0] - b[0]);
    document.getElementById('f-star').innerHTML =
      '<option value="">全部恒星</option>' + stars.map(([id, n]) => `<option value="${id}">${esc(n)}</option>`).join('');
    const themes = [...new Set(ROWS.map(r => r.pl.themeName).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'zh-CN'));
    document.getElementById('f-theme').innerHTML =
      '<option value="">全部主题</option>' + themes.map(t => `<option>${esc(t)}</option>`).join('');

    ['search', 'f-star', 'f-theme', 'f-scanned', 'f-hive', 'f-plan', 'f-dyson'].forEach(id => {
      const el = document.getElementById(id);
      el.addEventListener(id === 'search' ? 'input' : 'change', applyFilter);
    });
    document.querySelector('#tbl thead').addEventListener('click', e => {
      const th = e.target.closest('th');
      if (!th) return;
      const k = th.dataset.k;
      if (sortKey === k) sortDir = -sortDir;
      else { sortKey = k; sortDir = 1; }
      applyFilter();
    });
    // 行选中 / 双击进恒星系
    document.querySelector('#tbl tbody').addEventListener('click', e => {
      const tr = e.target.closest('tr');
      if (!tr) return;
      selectPlanet(+tr.dataset.pid, false);
    });
    document.querySelector('#tbl tbody').addEventListener('dblclick', e => {
      const tr = e.target.closest('tr');
      if (!tr) return;
      const pid = +tr.dataset.pid;
      const r = ROWS.find(x => x.pl.id === pid);
      if (r) { selectPlanet(pid, true); enterStarSystem(r.st.id, pid); }
    });
    // 规划编辑
    document.querySelector('#tbl tbody').addEventListener('dblclick', e => {
      const td = e.target.closest('td.plan');
      if (!td) return;
      const tr = td.closest('tr');
      startEdit(+tr.dataset.pid, td);
    }, true);
  }

  function startEdit(pid, td) {
    if (td.querySelector('input')) return;
    const r = ROWS.find(x => x.pl.id === pid);
    if (!r) return;
    const old = r.planText;
    td.classList.add('editing');
    const inp = document.createElement('input');
    inp.value = old;
    td.textContent = '';
    td.appendChild(inp);
    inp.focus(); inp.select();
    const finish = async (commit) => {
      if (inp.dataset.done) return;
      inp.dataset.done = '1';
      const text = inp.value.trim();
      if (!commit || text === old) { td.classList.remove('editing'); td.textContent = old; return; }
      if (!isHttp) {
        alert('编辑保存需要服务模式：DspView --galaxy <json> --serve');
        td.classList.remove('editing'); td.textContent = old; return;
      }
      try {
        const resp = await fetch('/api/notes', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ notes: { [String(pid)]: text } }),
        });
        const j = await resp.json();
        if (!j.ok) throw new Error((j.errors || []).join('; ') || '保存失败');
        r.planText = text; r.pl.planNote = text;
        td.classList.remove('editing');
        td.textContent = text;
        td.classList.toggle('unbuilt', !r.built);
        td.classList.toggle('built', r.built);
      } catch (err) {
        alert('保存失败: ' + err.message);
        td.classList.remove('editing'); td.textContent = old;
      }
    };
    inp.addEventListener('keydown', ev => {
      if (ev.key === 'Enter') { ev.preventDefault(); finish(true); }
      if (ev.key === 'Escape') { ev.preventDefault(); finish(false); }
    });
    inp.addEventListener('blur', () => finish(true));
  }

  // ============ 详情卡 ============
  function renderDetail(r) {
    const el = document.getElementById('detail');
    if (!r) { el.textContent = '点击行星查看详情'; return; }
    const p = r.pl;
    const veinRows = VEINS.map(([t, n]) => {
      const v = r.veins[t];
      if (!v || !v.currentAmount) return '';
      return `<span class="row">${n}: <b>${fmtAmt(v.currentAmount)}</b></span>`;
    }).filter(Boolean).join('　');
    el.innerHTML = `
      <h3>${esc(p.name)}　<span style="color:var(--fg-dim);font-weight:400">${esc(p.themeName)}</span></h3>
      <div class="row">恒星 <b>${esc(r.starName)}</b>　类型 <b>${starTypeText(r.st)}</b>　光度 <b>${r.st.luminosity.toFixed(3)}</b>　距离 <b>${r.st.distanceFromBirth.toFixed(2)} ly</b></div>
      <div class="row">开阔度 <b>${p.spaceLevel ?? '—'}</b>　${p.scanned ? '已探索' : '<span style="color:var(--fg-dim)">未探索</span>'}　规划 <b>${esc(r.planText || '—')}</b>${r.built ? ' <span style="color:var(--green)">已建</span>' : ''}</div>
      <div class="row">黑雾基地 <b>${r.bhCount || 0}</b>　巢穴 <b>${r.hiveCount}</b>${r.dyson ? `　戴森球 <b>${r.dyson.layers}层</b>` : ''}${r.gas ? `　采集站 <b>${r.collectors ?? '—'}/40</b>` : ''}${r.acid ? '　<b>硫酸</b>' : ''}</div>
      <div class="row">${veinRows || '无矿脉数据'}</div>`;
  }

  function selectPlanet(pid, fly) {
    selectedPid = pid;
    document.querySelectorAll('#tbl tbody tr').forEach(tr =>
      tr.classList.toggle('selected', +tr.dataset.pid === pid));
    const r = ROWS.find(x => x.pl.id === pid);
    renderDetail(r);
    const tr = document.querySelector(`#tbl tbody tr[data-pid="${pid}"]`);
    if (tr) tr.scrollIntoView({ block: 'nearest' });
    if (fly && r) {
      // 双击表格进入恒星系：若 3D 被关闭则自动打开（否则没有可看的画面）
      if (document.querySelector('main').classList.contains('no3d')) {
        localStorage.setItem('hide3d', '0');
        apply3DLayout();
      }
      enterStarSystem(r.st.id, pid);
    }
    sync3DSelection();
  }

  // ============ 3D ============
  let renderer, scene, camera, controls, groupGalaxy, groupSystem;
  let currentStar = null;
  let raycaster, mouse, tooltip;
  const pickables = [];      // { obj, kind:'star'|'planet', data }
  let labelSprites = [];
  let needsRender = true;

  function init3D() {
    if (renderer) return;            // 幂等：重复初始化会叠加 canvas → 遮挡交互 + 撑高侧栏、挤掉详情面板
    const host = document.getElementById('three');
    [...host.querySelectorAll('canvas')].forEach(c => c.remove());   // 清掉旧版本残留
    scene = new THREE.Scene();
    camera = new THREE.PerspectiveCamera(50, host.clientWidth / host.clientHeight, 0.1, 5000);
    renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
    renderer.setSize(host.clientWidth, host.clientHeight);
    host.appendChild(renderer.domElement);
    controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;

    tooltip = document.createElement('div');
    tooltip.id = 'tooltip3d';
    host.appendChild(tooltip);

    raycaster = new THREE.Raycaster();
    mouse = new THREE.Vector2();

    addStarfield();
    buildGalaxyView();
    showGalaxyView();

    renderer.domElement.addEventListener('pointermove', onPointerMove);
    renderer.domElement.addEventListener('click', e => onClick(e, false));
    renderer.domElement.addEventListener('dblclick', e => onClick(e, true));
    window.addEventListener('resize', resize3D);
    window.addEventListener('keydown', e => { if (e.key === 'Escape') showGalaxyView(); });

    document.getElementById('legend').innerHTML =
      '<span><i class="dot" style="background:#ffe9b0"></i>恒星</span>' +
      '<span><i class="dot" style="background:#1a1030;border:1px solid #8a6cff"></i>黑洞</span>' +
      '<span><i class="dot" style="background:#e0533d"></i>黑雾巢穴</span>' +
      '<span><i class="dot" style="background:#e8b84b"></i>有规划</span>' +
      '<span><i class="dot" style="background:#37c46b"></i>已建</span>';

    animate();
  }

  function resize3D() {
    const host = document.getElementById('three');
    if (!renderer) return;
    const w = Math.max(1, host.clientWidth), h = Math.max(1, host.clientHeight);
    camera.aspect = w / h;
    camera.updateProjectionMatrix();
    renderer.setSize(w, h);
    needsRender = true;
  }

  /** 应用 3D 显隐状态（关闭时表格占满全宽；打开时懒初始化） */
  function apply3DLayout() {
    const off = localStorage.getItem('hide3d') === '1';
    document.querySelector('main').classList.toggle('no3d', off);
    const btn = document.getElementById('toggle3d');
    btn.classList.toggle('off', off);
    btn.textContent = off ? '3D 关' : '3D 开';
    if (!off) { if (!renderer) init3D(); else requestAnimationFrame(resize3D); }
  }

  function init3DToggle() {
    document.getElementById('toggle3d').addEventListener('click', () => {
      localStorage.setItem('hide3d', localStorage.getItem('hide3d') === '1' ? '0' : '1');
      apply3DLayout();
    });
    apply3DLayout();
  }

  function makeLabel(text, color = '#dfe6f0', size = 42, H = 0.028) {
    const c = document.createElement('canvas');
    const ctx = c.getContext('2d');
    ctx.font = `600 ${size}px "Microsoft YaHei", sans-serif`;
    const w = Math.ceil(ctx.measureText(text).width) + 24;
    c.width = w; c.height = size + 20;
    const ctx2 = c.getContext('2d');
    ctx2.font = `600 ${size}px "Microsoft YaHei", sans-serif`;
    ctx2.fillStyle = 'rgba(5,8,14,.55)';
    ctx2.fillRect(0, 0, c.width, c.height);
    ctx2.fillStyle = color;
    ctx2.textBaseline = 'middle';
    ctx2.fillText(text, 12, c.height / 2);
    const tex = new THREE.CanvasTexture(c);
    tex.minFilter = THREE.LinearFilter;
    // sizeAttenuation=false：标签固定屏幕尺寸，不随距离缩小（保证任何缩放下都可读）
    const spr = new THREE.Sprite(new THREE.SpriteMaterial({
      map: tex, transparent: true, depthWrite: false, sizeAttenuation: false,
    }));
    spr.scale.set((c.width / c.height) * H, H, 1);
    spr.userData.isLabel = true;
    return spr;
  }

  /** 恒星光晕纹理：针尖白核（5%）+ 饱和色环（10~25%）+ 长尾表减，避免变成“白雾团” */
  function makeGlow(color, worldSize) {
    const c = document.createElement('canvas');
    c.width = c.height = 256;
    const ctx = c.getContext('2d');
    const hex = '#' + color.toString(16).padStart(6, '0');
    const g = ctx.createRadialGradient(128, 128, 0, 128, 128, 128);
    g.addColorStop(0.00, hex);
    g.addColorStop(0.040, hex);
    g.addColorStop(0.130, hex + 'b0');
    g.addColorStop(0.260, hex + '30');
    g.addColorStop(0.450, hex + '00');
    g.addColorStop(1.000, 'rgba(0,0,0,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, 256, 256);
    const tex = new THREE.CanvasTexture(c);
    tex.minFilter = THREE.LinearFilter;
    const spr = new THREE.Sprite(new THREE.SpriteMaterial({
      map: tex, blending: THREE.AdditiveBlending, transparent: true, depthWrite: false,
    }));
    spr.scale.set(worldSize, worldSize, 1);
    return spr;
  }

  /** 轨道平面：外缘贴在轨道线上（R），向内渐隐融入背景（不是以轨道线为中心的对称环带）*/
  function addOrbitBand(R, maxR, q, alphaOverride) {
    const w = 0.12 * R + 0.4;                        // 带宽随半径增大
    const inner = Math.max(0.2, R - w);
    const geo = new THREE.RingGeometry(inner, R, 128, 1);   // 外径 = R：外缘正好落在轨迹线上
    geo.rotateX(-Math.PI / 2);
    const alphaMax = alphaOverride ?? (0.12 + 0.30 * (R / Math.max(1, maxR)));
    const mat = new THREE.ShaderMaterial({
      uniforms: {
        uColor: { value: new THREE.Color(ORBIT_COLOR) },
        uInner: { value: inner },
        uOuter: { value: R },
        uAlpha: { value: alphaMax },
      },
      vertexShader:
        'varying float vR;\n' +
        'void main(){ vR = length(position.xz); gl_Position = projectionMatrix * modelViewMatrix * vec4(position,1.0); }',
      fragmentShader:
        'uniform vec3 uColor; uniform float uInner; uniform float uOuter; uniform float uAlpha; varying float vR;\n' +
        'void main(){ float t = clamp((vR - uInner) / max(0.001, uOuter - uInner), 0.0, 1.0);\n' +
        '  gl_FragColor = vec4(uColor, uAlpha * pow(t, 1.6)); }',
      transparent: true, side: THREE.DoubleSide, depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    const mesh = new THREE.Mesh(geo, mat);
    mesh.quaternion.copy(q);
    return mesh;
  }

  /** 轨迹线：行星身后延伸的弧，头部亮、尾部渐隐（加色混合 + 顶点色渐黑） */
  function addTrail(R, phase, q, color) {
    const SEG = 48, span = THREE.MathUtils.degToRad(110);
    const pos = new Float32Array((SEG + 1) * 3);
    const col = new Float32Array((SEG + 1) * 3);
    const c = new THREE.Color(color);
    for (let i = 0; i <= SEG; i++) {
      const a = phase - span * (i / SEG);             // 向运动反方向拖尾
      const v = new THREE.Vector3(Math.cos(a) * R, 0, Math.sin(a) * R).applyQuaternion(q);
      pos[i * 3] = v.x; pos[i * 3 + 1] = v.y; pos[i * 3 + 2] = v.z;
      const k = Math.pow(1 - i / SEG, 0.6);          // 尾部衰减更慢，轨迹更长可见
      col[i * 3] = c.r * k; col[i * 3 + 1] = c.g * k; col[i * 3 + 2] = c.b * k;
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    return new THREE.Line(geo, new THREE.LineBasicMaterial({
      vertexColors: true, transparent: true, opacity: 0.9,
      blending: THREE.AdditiveBlending, depthWrite: false,
    }));
  }

  /** 恒星本体：白热核心 + 菲涅尔边缘发光 + 表面滴流噪声（红巨星斑驳感）*/
  function makeStarMaterial(color, noiseAmt, coreEdge) {
    return new THREE.ShaderMaterial({
      uniforms: {
        uColor: { value: new THREE.Color(color) },
        uCore: { value: new THREE.Color(0xfffdf2) },
        uNoise: { value: noiseAmt === undefined ? 0.18 : noiseAmt },
        uCoreEdge: { value: coreEdge === undefined ? 0.80 : coreEdge },
      },
      vertexShader:
        'varying vec3 vN; varying vec3 vP; varying vec3 vL;\n' +
        'void main(){ vN = normalize(normalMatrix * normal); vL = position;\n' +
        '  vec4 mv = modelViewMatrix * vec4(position,1.0); vP = mv.xyz; gl_Position = projectionMatrix * mv; }',
      fragmentShader:
        'uniform vec3 uColor; uniform vec3 uCore; uniform float uNoise; uniform float uCoreEdge; varying vec3 vN; varying vec3 vP; varying vec3 vL;\n' +
        'float hash(vec3 p){ return fract(sin(dot(p, vec3(127.1,311.7,74.7))) * 43758.5453); }\n' +
        'float noise(vec3 p){ vec3 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);\n' +
        '  float n000=hash(i), n100=hash(i+vec3(1,0,0)), n010=hash(i+vec3(0,1,0)), n110=hash(i+vec3(1,1,0));\n' +
        '  float n001=hash(i+vec3(0,0,1)), n101=hash(i+vec3(1,0,1)), n011=hash(i+vec3(0,1,1)), n111=hash(i+vec3(1,1,1));\n' +
        '  return mix(mix(mix(n000,n100,f.x), mix(n010,n110,f.x), f.y), mix(mix(n001,n101,f.x), mix(n011,n111,f.x), f.y), f.z); }\n' +
        'void main(){\n' +
        '  vec3 v = normalize(-vP);\n' +
        '  float f = pow(1.0 - max(dot(normalize(vN), v), 0.0), 2.2);   // 0=中心 1=边缘\n' +
        '  vec3 dir = normalize(vL);\n' +
        '  float n = noise(dir * 7.0) * 0.6 + noise(dir * 17.0) * 0.4;\n' +
        '  vec3 col = mix(uCore, uColor, smoothstep(0.05, uCoreEdge, f));\n' +
        '  col *= 1.0 - uNoise * 0.55 + uNoise * 1.1 * n;\n' +
        '  col += uColor * f * 0.9;\n' +
        '  gl_FragColor = vec4(col, 1.0);\n' +
        '}',
    });
  }

  /** 恒星日冕：白热核心 + 大面积色晕长尾（恒星系视图专用）*/
  function makeSunGlow(color, worldSize) {
    const c = document.createElement('canvas');
    c.width = c.height = 256;
    const ctx = c.getContext('2d');
    const hex = '#' + color.toString(16).padStart(6, '0');
    const g = ctx.createRadialGradient(128, 128, 0, 128, 128, 128);
    g.addColorStop(0.00, 'rgba(255,255,255,1)');
    g.addColorStop(0.05, 'rgba(255,252,235,0.95)');
    g.addColorStop(0.12, hex);
    g.addColorStop(0.26, hex + '70');
    g.addColorStop(0.50, hex + '22');
    g.addColorStop(0.78, hex + '08');
    g.addColorStop(1.00, 'rgba(0,0,0,0)');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, 256, 256);
    const tex = new THREE.CanvasTexture(c);
    tex.minFilter = THREE.LinearFilter;
    const spr = new THREE.Sprite(new THREE.SpriteMaterial({
      map: tex, blending: THREE.AdditiveBlending, transparent: true, depthWrite: false,
    }));
    spr.scale.set(worldSize, worldSize, 1);
    return spr;
  }

  /** 深空背景星点（相机远处静态散点，不参与交互） */
  function addStarfield() {
    const N = 2200;
    const pos = new Float32Array(N * 3);
    for (let i = 0; i < N; i++) {
      const r = 500 + Math.random() * 900;
      const th = Math.random() * Math.PI * 2;
      const ph = Math.acos(2 * Math.random() - 1);
      pos[i * 3] = r * Math.sin(ph) * Math.cos(th);
      pos[i * 3 + 1] = r * Math.cos(ph);
      pos[i * 3 + 2] = r * Math.sin(ph) * Math.sin(th);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    scene.add(new THREE.Points(geo, new THREE.PointsMaterial({
      color: 0x8fa4c8, size: 1.2, transparent: true, opacity: 0.5, depthWrite: false,
    })));
  }

  function buildGalaxyView() {
    groupGalaxy = new THREE.Group();

    // 场景尺度：最大星距归一化到 90 单位
    const maxD = Math.max(1, ...G.stars.map(s => s.position ? Math.hypot(s.position.x, s.position.y, s.position.z) : 0));
    const k = 90 / maxD;

    const birth = G.stars[0];
    const birthPos = new THREE.Vector3(birth.position.x * k, birth.position.y * k, birth.position.z * k);

    G.stars.forEach(st => {
      const p = new THREE.Vector3(st.position.x * k, st.position.y * k, st.position.z * k);
      const lum = Math.max(0.005, st.luminosity || 1);
      const gs = 1.2 + Math.pow(lum, 0.3) * 2.6;       // 亮星≈7.2、G型≈3.8、暗星≈1.7（视觉直径≈gs×0.5）
      const glow = makeGlow(starColor(st), gs);
      glow.position.copy(p);
      glow.userData = { kind: 'star', data: st };
      groupGalaxy.add(glow);
      pickables.push({ obj: glow, kind: 'star', data: st });

      // 锐利白核（只给亮星）：游戏里恒星是“白点 + 收敛色晕”两层
      if (lum > 0.25) {
        const core = makeGlow(0xffffff, gs * 0.30);
        core.position.copy(p);
        groupGalaxy.add(core);
      }

      // 恒星名：出生星 + 所有有建筑行星的恒星（其余悬停显示）；字号小一些减少重叠
      const hasBuilt = st.planets.some(x => x.hasFactory);
      if (st.id === birth.id || hasBuilt) {
        const isBirth = st.id === birth.id;
        const spr = makeLabel(st.name, isBirth ? '#ffffff' : '#9fe3b8', isBirth ? 34 : 26, isBirth ? 0.024 : 0.018);
        spr.position.copy(p).add(new THREE.Vector3(0, gs * 0.75 + 1.0, 0));
        groupGalaxy.add(spr);
        labelSprites.push(spr);
      }
    });

    scene.add(groupGalaxy);

    camera.position.copy(birthPos.clone().add(new THREE.Vector3(70, 90, 110)));
    controls.target.copy(birthPos);
    controls.update();
  }

  function showGalaxyView() {
    currentStar = null;
    if (groupSystem) { scene.remove(groupSystem); groupSystem = null; }
    groupGalaxy.visible = true;
    const birth = G.stars[0];
    const maxD = Math.max(1, ...G.stars.map(s => s.position ? Math.hypot(s.position.x, s.position.y, s.position.z) : 0));
    const k = 90 / maxD;
    const bp = new THREE.Vector3(birth.position.x * k, birth.position.y * k, birth.position.z * k);
    camera.position.copy(bp.clone().add(new THREE.Vector3(70, 90, 110)));
    controls.target.copy(bp);
    controls.update();
    document.getElementById('hint').textContent = '双击恒星进入恒星系 · ESC 回退 · 双击「规划」单元格可编辑';
    needsRender = true;
  }

  function enterStarSystem(starId, highlightPid) {
    const st = G.stars.find(s => s.id === starId);
    if (!st) return;
    currentStar = st;
    groupGalaxy.visible = false;
    if (groupSystem) scene.remove(groupSystem);
    groupSystem = new THREE.Group();

    // 中心恒星
    const lum = Math.max(0.005, st.luminosity || 1);
    const sr = starRadius(lum, st);
    const sun = new THREE.Mesh(new THREE.SphereGeometry(sr, 48, 32),
      makeStarMaterial(starColor(st), st.type === 'GiantStar' ? 0.75 : 0.15, st.type === 'GiantStar' ? 0.45 : 0.80));
    groupSystem.add(sun);
    const sunGlow = makeSunGlow(starColor(st), sr * 12);
    groupSystem.add(sunGlow);
    const sunLabel = makeLabel(st.name, '#ffffff', 44, 0.028);
    sunLabel.position.set(0, sr * 1.7, 0);
    groupSystem.add(sunLabel);

    // 轨道归一化
    const maxOrbit = Math.max(1, ...st.planets.map(p => p.orbit ? p.orbit.radius : 1));
    const planetPos = {};
    const planetQ = {};

    st.planets.forEach(pl => {
      if (!pl.orbit || pl.orbitAround > 0) return;   // 卫星不绕恒星，单独画（见下）
      const pr = planetRadius(pl);
      // 轨道距离开方压缩（外圈更密，与游戏星图观感一致），再套恒星下限
      const Rn = Math.sqrt(pl.orbit.radius / maxOrbit) * 46;
      const R = Math.max(Rn, sr * 1.5 + pr);
      const inc = THREE.MathUtils.degToRad(pl.orbit.inclination || 0);
      const lon = THREE.MathUtils.degToRad(pl.orbit.longitude || 0);
      const phase = THREE.MathUtils.degToRad(pl.orbit.phase || 0);

      // 轨道平面（与 DSP runtimeOrbitRotation 同序：AngleAxis(lon, up) * AngleAxis(inc, forward)）
      const q = new THREE.Quaternion()
        .multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), lon))
        .multiply(new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 0, 1), inc));

      // 轨道带（内淡外亮）+ 中心基准细线
      groupSystem.add(addOrbitBand(R, 46, q));
      const pts = [];
      for (let i = 0; i <= 96; i++) {
        const a = i / 96 * Math.PI * 2;
        pts.push(new THREE.Vector3(Math.cos(a) * R, 0, Math.sin(a) * R).applyQuaternion(q));
      }
      groupSystem.add(new THREE.Line(
        new THREE.BufferGeometry().setFromPoints(pts),
        new THREE.LineBasicMaterial({ color: ORBIT_COLOR, transparent: true, opacity: 0.35, depthWrite: false })
      ));

      // 行星位置（轨道平面内按 phase）
      const base = new THREE.Vector3(Math.cos(phase) * R, 0, Math.sin(phase) * R).applyQuaternion(q);
      const pColor = PLANET_COLOR[pl.type] ?? 0xb0b8c4;
      const mat = new THREE.MeshBasicMaterial({
        color: pColor,
        transparent: !pl.scanned, opacity: pl.scanned ? 1 : 0.45,
      });
      const mesh = new THREE.Mesh(new THREE.SphereGeometry(pr, 24, 16), mat);
      mesh.position.copy(base);
      mesh.userData = { kind: 'planet', data: pl, star: st };
      groupSystem.add(mesh);
      pickables.push({ obj: mesh, kind: 'planet', data: pl });
      planetPos[pl.id] = base.clone();
      planetQ[pl.id] = q;

      // 轨迹线（行星身后，一头浓一头淡）
      groupSystem.add(addTrail(R, phase, q, pColor));

      // 行星名标签（恒星系内全显示；按序号错开高度避免重叠）
      const lbl = makeLabel(pl.name, pl.scanned ? '#dfe6f0' : '#7c8798', 32, 0.026);
      lbl.position.copy(base).add(new THREE.Vector3(0, pr + 0.9 + (pl.number % 3) * 0.9, 0));
      groupSystem.add(lbl);

      // 规划标签
      const rr = ROWS.find(x => x.pl.id === pl.id);
      if (rr && rr.planText) {
        const pl2 = makeLabel(rr.planText, rr.built ? '#37c46b' : '#4aa3ff', 26, 0.021);
        pl2.position.copy(base).add(new THREE.Vector3(0, -pr - 1.4, 0));
        groupSystem.add(pl2);
      }
    });

    // 卫星（orbitAround > 0）：绘在母行星周围；轨道半径随母行星半径缩放，避免被气巨吞没
    st.planets.forEach(pl => {
      if (!pl.orbitAround) return;
      const parentId = st.id * 100 + pl.orbitAround;   // orbitAround 是行星序号，不是 planetId
      const parent = planetPos[parentId];
      if (!parent) return;
      const parentPl = st.planets.find(x => x.id === parentId);
      const r2 = Math.max(planetRadius(parentPl || pl) * 2.4, 1.6);
      const qm = planetQ[parentId] || new THREE.Quaternion();
      const ang = THREE.MathUtils.degToRad(pl.orbit ? pl.orbit.phase : 0);
      const off = new THREE.Vector3(Math.cos(ang) * r2, 0, Math.sin(ang) * r2).applyQuaternion(qm);
      // 卫星轨道平面：同款外缘贴线、向内渐隐；平面取母行星轨道平面，中心在母行星
      const moonBand = addOrbitBand(r2, 0, qm, 0.20);
      moonBand.position.copy(parent);
      groupSystem.add(moonBand);
      // 卫星环（虚线感：细圆）
      const pts = [];
      for (let i = 0; i <= 48; i++) {
        const a = i / 48 * Math.PI * 2;
        const v = new THREE.Vector3(Math.cos(a) * r2, 0, Math.sin(a) * r2).applyQuaternion(qm).add(parent);
        pts.push(v);
      }
      groupSystem.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(pts),
        new THREE.LineBasicMaterial({ color: ORBIT_COLOR, transparent: true, opacity: 0.45, depthWrite: false })));
      const pr = MOON_RADIUS;
      const mesh = new THREE.Mesh(new THREE.SphereGeometry(pr, 12, 9),
        new THREE.MeshBasicMaterial({ color: PLANET_COLOR[pl.type] ?? 0xb0b8c4, transparent: !pl.scanned, opacity: pl.scanned ? 1 : 0.45 }));
      mesh.position.copy(parent).add(off);
      mesh.userData = { kind: 'planet', data: pl, star: st };
      groupSystem.add(mesh);
      pickables.push({ obj: mesh, kind: 'planet', data: pl });
      const lbl = makeLabel(pl.name, pl.scanned ? '#dfe6f0' : '#7c8798', 26, 0.021);
      lbl.position.copy(mesh.position).add(new THREE.Vector3(0, pr + 0.9, 0));
      groupSystem.add(lbl);
      const rr = ROWS.find(x => x.pl.id === pl.id);
      if (rr && rr.planText) {
        const pl2 = makeLabel(rr.planText, rr.built ? '#37c46b' : '#4aa3ff', 24, 0.019);
        pl2.position.copy(mesh.position).add(new THREE.Vector3(0, -pr - 1.0, 0));
        groupSystem.add(pl2);
      }
    });

    scene.add(groupSystem);
    const camDist = Math.max(70, 46 * 2.4);
    camera.position.set(0, camDist * 0.85, camDist * 0.55);
    controls.target.set(0, 0, 0);
    controls.update();
    document.getElementById('hint').textContent = `${st.name}　ESC 返回星系视图`;
    needsRender = true;
  }

  function onPointerMove(ev) {
    if (!renderer) return;
    const rect = renderer.domElement.getBoundingClientRect();
    mouse.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    mouse.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
    raycaster.setFromCamera(mouse, camera);
    const list = pickables.filter(p => p.obj.parent && p.obj.parent.visible !== false);
    const hits = raycaster.intersectObjects(list.map(p => p.obj), false);
    if (!hits.length) { tooltip.style.display = 'none'; return; }
    const obj = hits[0].object;
    const ud = obj.userData;
    let text = '';
    if (ud.kind === 'star') {
      const st = ud.data;
      const n = st.planets.length;
      const scanned = st.planets.filter(p => p.scanned).length;
      text = `${st.name}　${starTypeText(st)}　${n} 行星（已探索 ${scanned}）` +
             (st.hiveCount ? `　巢穴 ${st.hiveCount}` : '') + (st.dyson ? `　戴森球 ${st.dyson.layers}层` : '');
    } else {
      const pl = ud.data;
      const rr = ROWS.find(x => x.pl.id === pl.id);
      text = `${pl.name}　${pl.themeName}` + (rr ? `　开阔度 ${pl.spaceLevel ?? '—'}` : '') +
             (rr && rr.planText ? `　规划 ${rr.planText}` : '') + (pl.scanned ? '' : '　未探索');
    }
    tooltip.textContent = text;
    tooltip.style.left = (ev.clientX - rect.left) + 'px';
    tooltip.style.top = (ev.clientY - rect.top) + 'px';
    tooltip.style.display = 'block';
  }

  function onClick(ev, dbl) {
    if (!renderer) return;
    const rect = renderer.domElement.getBoundingClientRect();
    mouse.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    mouse.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
    raycaster.setFromCamera(mouse, camera);
    const list = pickables.filter(p => p.obj.parent && p.obj.parent.visible !== false);
    const hits = raycaster.intersectObjects(list.map(p => p.obj), false);
    if (!hits.length) return;
    const ud = hits[0].object.userData;
    if (ud.kind === 'star') {
      if (dbl) enterStarSystem(ud.data.id, null);
    } else if (ud.kind === 'planet') {
      selectPlanet(ud.data.id, false);
      if (dbl) enterStarSystem(ud.star.id, ud.data.id);
    }
  }

  function sync3DSelection() { needsRender = true; }

  function animate() {
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);   // 每帧渲染：滚轮/拖拽实时生效
    needsRender = false;
  }

  // ============ 启动 ============
  (async () => {
    G = await loadData();
    if (!G || !G.stars) {
      document.getElementById('alert').className = 'alert danger';
      document.getElementById('alert').textContent = '数据加载失败：缺少 galaxy-data.js 或服务不可用';
      return;
    }
    document.getElementById('title').textContent = `seed ${G.meta.seed}　${G.meta.save ? G.meta.save.gameName : '未融合存档'}`;
    buildRows();
    renderAlert();
    initControls();
    applyFilter();
    init3DToggle();          // 内部按 localStorage 决定是否初始化 3D（幂等，不会重复创建）

    // 深链：#star=<id> 直接进入恒星系（便于分享/截图验证）
    const m = location.hash.match(/star=(\d+)/);
    if (m) { enterStarSystem(+m[1], null); }
  })();
})();

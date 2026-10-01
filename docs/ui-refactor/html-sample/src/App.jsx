import React, { useEffect, useMemo, useRef, useState } from 'react';
import { makeStudents, VENUES, ROSTERS, STRATEGIES, MESSAGES, SNAPSHOTS, generate } from './data.js';

const I18N = {
  zh: {
    nav_flow: '工作流', nav_workbench: '排座工作台', nav_data: '资料', nav_members: '人员名单', nav_venues: '会场与布局',
    nav_rules: '规则', nav_strategies: '策略配置', nav_records: '记录', nav_snapshots: '历史快照',
    nav_settings: '设置', nav_about: '关于',
    venue: '会场', roster: '名单', generate: '生成座位安排', generating: '生成中…',
    undo: '撤销', redo: '重做', save_snapshot: '保存快照', export: '导出',
    tab_strategies: '策略状态', tab_unassigned: '未分配', tab_history: '修改记录', tab_messages: '消息',
    seats_assigned: '已分配', seat_empty: '空座', seat_fixed: '固定座位', seat_selected: '选中',
    members_title: '人员名单', add_student: '新增学生', name: '姓名', height: '身高 (cm)', gender: '性别',
    male: '男', female: '女', front_row: '需要前排', delete: '删除', save: '保存', search: '搜索姓名…',
    venues_title: '会场与布局', grid: '网格 Grid', polar: '极坐标 Polar', freeform: '自由点 Freeform',
    rows: '行数', columns: '列数', seats_per_desk: '每桌人数', aisle: '第几列后过道', preview: '预览',
    strategies_title: '策略配置', priority: '优先级', enabled: '启用', disabled: '未启用',
    snapshots_title: '历史快照', rollback: '回滚到此次', batch_delete: '批量删除', cancel: '取消',
    settings_title: '设置', appearance: '外观', behavior: '行为', shortcuts: '键盘快捷键', storage: '存储',
    theme: '主题', light: '浅色', dark: '深色', language: '语言',
    about_title: '关于 SeatFlow', version: '版本', deps: '依赖组件',
    saved: '已保存（示例）', exported: '已导出（示例）', snapshot_saved: '快照已保存（示例）',
    dialog_dirty_title: '未保存的修改', dialog_dirty_body: '当前页面有未保存的修改，切换后将丢失。',
    discard: '放弃修改', keep_editing: '继续编辑', confirm: '确定', rollback_title: '回滚快照',
    rollback_body: '将当前排座恢复为该快照，现有安排会被覆盖。',
  },
  en: {
    nav_flow: 'Workflow', nav_workbench: 'Seating Workbench', nav_data: 'Data', nav_members: 'Rosters', nav_venues: 'Venues & Layouts',
    nav_rules: 'Rules', nav_strategies: 'Strategies', nav_records: 'Records', nav_snapshots: 'Snapshots',
    nav_settings: 'Settings', nav_about: 'About',
    venue: 'Venue', roster: 'Roster', generate: 'Generate seating', generating: 'Generating…',
    undo: 'Undo', redo: 'Redo', save_snapshot: 'Save snapshot', export: 'Export',
    tab_strategies: 'Strategies', tab_unassigned: 'Unassigned', tab_history: 'History', tab_messages: 'Messages',
    seats_assigned: 'Assigned', seat_empty: 'Empty', seat_fixed: 'Fixed', seat_selected: 'Selected',
    members_title: 'Rosters', add_student: 'Add student', name: 'Name', height: 'Height (cm)', gender: 'Gender',
    male: 'Male', female: 'Female', front_row: 'Front row', delete: 'Delete', save: 'Save', search: 'Search name…',
    venues_title: 'Venues & Layouts', grid: 'Grid', polar: 'Polar', freeform: 'Freeform',
    rows: 'Rows', columns: 'Columns', seats_per_desk: 'Seats / desk', aisle: 'Aisle after column', preview: 'Preview',
    strategies_title: 'Strategies', priority: 'Priority', enabled: 'Enabled', disabled: 'Disabled',
    snapshots_title: 'Snapshots', rollback: 'Roll back to this', batch_delete: 'Batch delete', cancel: 'Cancel',
    settings_title: 'Settings', appearance: 'Appearance', behavior: 'Behavior', shortcuts: 'Shortcuts', storage: 'Storage',
    theme: 'Theme', light: 'Light', dark: 'Dark', language: 'Language',
    about_title: 'About SeatFlow', version: 'Version', deps: 'Dependencies',
    saved: 'Saved (demo)', exported: 'Exported (demo)', snapshot_saved: 'Snapshot saved (demo)',
    dialog_dirty_title: 'Unsaved changes', dialog_dirty_body: 'This page has unsaved changes. Switching will discard them.',
    discard: 'Discard', keep_editing: 'Keep editing', confirm: 'OK', rollback_title: 'Roll back snapshot',
    rollback_body: 'Restore the seating to this snapshot. Current arrangement will be overwritten.',
  },
};

const PageCtx = React.createContext(null);
const useApp = () => React.useContext(PageCtx);

function useMediaQuery(query) {
  const [matches, setMatches] = useState(() => typeof matchMedia !== 'undefined' && matchMedia(query).matches);
  useEffect(() => {
    const mq = matchMedia(query);
    const on = () => setMatches(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [query]);
  return matches;
}

export default function App() {
  const params = new URLSearchParams(location.search);
  const [lang, setLang] = useState(params.get('lang') || 'zh');
  const [theme, setTheme] = useState(params.get('theme') || 'light');
  const [page, setPage] = useState(params.get('page') || 'workbench');
  const [students] = useState(() => makeStudents(240));
  const [snapshots, setSnapshots] = useState(SNAPSHOTS);
  const [toast, setToast] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [drawer, setDrawer] = useState(null); // null | 'nav' | 'picker' | 'inspector' | 'list'
  const compact = useMediaQuery('(max-width: 900px)');
  const t = (k) => I18N[lang][k] ?? I18N.zh[k] ?? k;
  const showToast = (m) => { setToast(m); setTimeout(() => setToast(null), 2200); };

  useEffect(() => { document.documentElement.dataset.theme = theme; }, [theme]);
  useEffect(() => { setDrawer(null); }, [page]);
  useEffect(() => { if (!compact) setDrawer(null); }, [compact]);
  useEffect(() => {
    const u = new URL(location.href);
    u.searchParams.set('page', page); u.searchParams.set('theme', theme); u.searchParams.set('lang', lang);
    history.replaceState(null, '', u);
  }, [page, theme, lang]);

  const ctx = { t, lang, setLang, theme, setTheme, students, snapshots, setSnapshots, showToast, setDialog, dialog, page, setPage, compact, drawer, setDrawer };

  const PAGES = {
    workbench: <Workbench />, members: <Members />, venues: <Venues />,
    strategies: <Strategies />, snapshots: <Snapshots />, settings: <Settings />, about: <About />,
  };

  return (
    <PageCtx.Provider value={ctx}>
      <div className="app" data-drawer={drawer || undefined}>
        <Rail />
        <div className="main">
          {compact && <CompactBar />}
          {PAGES[page]}
        </div>
        <footer className="statusbar">
          <span>{page === 'workbench' ? `${t('seats_assigned')} 64 / 64 · 固定 4 座 · 策略 6/7` : 'SeatFlow UI sample · 方向 B「方格纸」'}</span>
          <span className="right">{lang === 'zh' ? '中文' : 'EN'} · {theme === 'dark' ? t('dark') : t('light')} · v2.0.0-rc</span>
        </footer>
      </div>
      {drawer && <div className="drawer-mask" onClick={() => setDrawer(null)} />}
      {dialog && <Modal {...dialog} />}
      {toast && <div className="toast">{toast}</div>}
    </PageCtx.Provider>
  );
}

// 移动/窄屏顶栏：左上呼出导航，右上呼出本页上下文菜单（数据 / 面板 / 列表）
function CompactBar() {
  const { t, page, setDrawer } = useApp();
  const right = {
    workbench: [['数据', 'picker'], ['面板', 'inspector']],
    members: [['数据集', 'list']],
    venues: [['会场', 'list']],
    strategies: [['策略', 'list']],
    snapshots: [['快照', 'list']],
  }[page] || [];
  return (
    <header className="compactbar">
      <button className="btn btn-sm" onClick={() => setDrawer('nav')}>☰ 导航</button>
      <span className="compact-title">{t(`nav_${page}`)}</span>
      <span className="compact-actions">
        {right.map(([label, type]) => (
          <button key={type} className="btn btn-sm" onClick={() => setDrawer(type)}>{label}</button>
        ))}
      </span>
    </header>
  );
}

/* ---------------- 壳 ---------------- */
function Rail() {
  const { t, page, setPage } = useApp();
  const item = (key, icon, action) => (
    <button className={`nav-item ${page === key ? 'active' : ''}`} onClick={() => action ? action() : setPage(key)}>
      <i>{icon}</i><span>{t(`nav_${key}`)}</span>
    </button>
  );
  return (
    <aside className="rail">
      <div className="brand"><span className="brand-mark">S</span><span className="brand-name">SeatFlow</span></div>
      <nav className="nav">
        <div className="nav-group">{t('nav_flow')}</div>
        {item('workbench', '▦')}
        <div className="nav-group">{t('nav_data')}</div>
        {item('members', '☰')}
        {item('venues', '▤')}
        <div className="nav-group">{t('nav_rules')}</div>
        {item('strategies', '⎔')}
        <div className="nav-group">{t('nav_records')}</div>
        {item('snapshots', '◷')}
      </nav>
      <div className="rail-bottom">
        {item('settings', '⚙')}
        {item('about', 'ⓘ')}
      </div>
    </aside>
  );
}

function CommandBar({ title, subtitle, actions }) {
  return (
    <header className="commandbar">
      <div className="crumbs"><strong>{title}</strong>{subtitle && <><span className="sep">/</span><span>{subtitle}</span></>}</div>
      <div className="actions">{actions}</div>
    </header>
  );
}

function Modal({ title, body, buttons }) {
  const { setDialog } = useApp();
  return (
    <div className="modal-mask" onClick={() => setDialog(null)}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <h3>{title}</h3>
        <div className="body">{body}</div>
        <div className="foot">
          {buttons.map((b, i) => (
            <button key={i} className={`btn ${b.primary ? 'btn-primary' : ''} ${b.danger ? 'btn-danger' : ''}`}
              onClick={() => { setDialog(null); b.onClick && b.onClick(); }}>{b.label}</button>
          ))}
        </div>
      </div>
    </div>
  );
}

/* ---------------- 排座工作台 ---------------- */
function Workbench() {
  const { t, students, showToast, setSnapshots, setDialog } = useApp();
  const [venue, setVenue] = useState(VENUES[0]);
  const [roster, setRoster] = useState(ROSTERS[0]);
  const [seats, setSeats] = useState(() => generate(VENUES[0], students));
  const [selected, setSelected] = useState(null);
  const [tab, setTab] = useState('strategies');
  const [dragStudent, setDragStudent] = useState(null);
  const [hoverSeat, setHoverSeat] = useState(null);
  const [history, setHistory] = useState([]);
  const [busy, setBusy] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [zoom, setZoom] = useState(1);
  const [boardSize, setBoardSize] = useState({ w: 0, h: 0 });
  const dragFromSeat = useRef(null);
  const canvasRef = useRef(null);
  const boardRef = useRef(null);
  const panRef = useRef(null);

  // 画布：自然尺寸测量（transform 不影响 offsetWidth）
  React.useLayoutEffect(() => {
    if (boardRef.current) setBoardSize({ w: boardRef.current.offsetWidth, h: boardRef.current.offsetHeight });
  }, [seats, venue]);

  // Ctrl/⌘ + 滚轮缩放（原生监听以允许 preventDefault）
  useEffect(() => {
    const el = canvasRef.current;
    if (!el) return;
    const handler = (e) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      e.preventDefault();
      const dir = e.deltaY > 0 ? -1 : 1;
      setZoom((z) => Math.min(2, Math.max(0.4, +(z + dir * 0.1).toFixed(2))));
    };
    el.addEventListener('wheel', handler, { passive: false });
    return () => el.removeEventListener('wheel', handler);
  }, []);

  const startPan = (e) => {
    if (e.target.closest('.seat, button, .canvas-toolbar, .zoombar')) return; // 交互元素优先
    const el = canvasRef.current;
    panRef.current = { x: e.clientX, y: e.clientY, sl: el.scrollLeft, st: el.scrollTop };
    el.classList.add('grabbing');
    try { el.setPointerCapture(e.pointerId); } catch { /* 合成事件无活动指针时忽略 */ }
  };
  const movePan = (e) => {
    const p = panRef.current; if (!p) return;
    const el = canvasRef.current;
    el.scrollLeft = p.sl - (e.clientX - p.x);
    el.scrollTop = p.st - (e.clientY - p.y);
  };
  const endPan = () => { panRef.current = null; canvasRef.current?.classList.remove('grabbing'); };
  const fitToWindow = () => {
    const el = canvasRef.current; if (!el || !boardSize.w) return;
    const z = Math.min((el.clientWidth - 48) / boardSize.w, (el.clientHeight - 80) / boardSize.h, 1);
    setZoom(Math.max(0.3, +z.toFixed(2)));
  };

  const byId = useMemo(() => Object.fromEntries(students.map(s => [s.id, s])), [students]);
  const assignedIds = useMemo(() => new Set(seats.filter(s => s.studentId).map(s => s.studentId)), [seats]);
  const unassigned = useMemo(() => students.filter(s => !assignedIds.has(s.id)), [students, assignedIds]);
  const assignedCount = seats.filter(s => s.studentId).length;

  const doGenerate = () => {
    setBusy(true);
    setHistory(h => [{ at: '刚刚', text: `按 ${STRATEGIES.filter(s => s.enabled).length} 个策略重新生成` }, ...h]);
    setTimeout(() => { setSeats(generate(venue, students)); setSelected(null); setBusy(false); }, 220); // 演示异步生成
  };

  const placeStudent = (seatId, studentId) => {
    setSeats(prev => {
      const next = prev.map(s => ({ ...s }));
      const target = next.find(s => s.id === seatId);
      if (!target) return prev;
      if (studentId) {
        next.forEach(s => { if (s.studentId === studentId) s.studentId = null; }); // 移出原座位
        target.studentId = studentId;
        setHistory(h => [{ at: '刚刚', text: `将 ${byId[studentId]?.name ?? studentId} 安排到 第${target.row}排 第${target.col}列` }, ...h]);
      }
      return next;
    });
    setDragStudent(null); setHoverSeat(null);
  };

  const onSeatClick = (seat) => {
    if (selected && selected !== seat.id) {
      setSeats(prev => {
        const next = prev.map(s => ({ ...s }));
        const a = next.find(s => s.id === selected), b = next.find(s => s.id === seat.id);
        [a.studentId, b.studentId] = [b.studentId, a.studentId];
        return next;
      });
      const a = byId[seats.find(s => s.id === selected).studentId], b = byId[seat.studentId];
      setHistory(h => [{ at: '刚刚', text: `交换 ${a?.name ?? '空座'} ↔ ${b?.name ?? '空座'}` }, ...h]);
      setSelected(null);
    } else {
      setSelected(seat.id === selected ? null : seat.id);
    }
  };

  const saveSnapshot = () => {
    setSnapshots(prev => [{ id: `s${Date.now()}`, name: `手动快照 · ${prev.length + 1}`, venue: venue.name, date: '刚刚', assigned: assignedCount, total: seats.length, note: '手动' }, ...prev]);
    showToast(t('snapshot_saved'));
  };

  const rows = [];
  const cols = venue.type === 'Grid' ? venue.columns : venue.perRing;
  const rowCount = venue.type === 'Grid' ? venue.rows : venue.rings;
  for (let r = 1; r <= rowCount; r++) {
    const rowSeats = seats.filter(s => s.row === r);
    rows.push(
      <div className="seat-row" key={r}>
        {rowSeats.map((s, i) => (
          <React.Fragment key={s.id}>
            {venue.aisleAfter && i === venue.aisleAfter && <span className="aisle" />}
            <div
              className={`seat ${s.studentId ? 'occupied' : ''} ${s.fixed ? 'fixed' : ''} ${selected === s.id ? 'selected' : ''} ${hoverSeat === s.id ? 'drop-target' : ''}`}
              draggable={!!s.studentId}
              onDragStart={() => { dragFromSeat.current = s.id; }}
              onClick={() => onSeatClick(s)}
              onDragOver={(e) => { e.preventDefault(); setHoverSeat(s.id); }}
              onDragLeave={() => setHoverSeat(h => (h === s.id ? null : h))}
              onDrop={(e) => {
                e.preventDefault();
                if (dragStudent) placeStudent(s.id, dragStudent);
                else if (dragFromSeat.current && dragFromSeat.current !== s.id) {
                  setSeats(prev => { const n = prev.map(x => ({ ...x })); const a = n.find(x => x.id === dragFromSeat.current), b = n.find(x => x.id === s.id); [a.studentId, b.studentId] = [b.studentId, a.studentId]; return n; });
                  setHistory(h => [{ at: '刚刚', text: `拖动交换座位` }, ...h]);
                }
                dragFromSeat.current = null;
              }}
              title={`第${s.row}排 第${s.col}列`}
            >
              {s.studentId && <span className="name">{byId[s.studentId]?.name}</span>}
            </div>
          </React.Fragment>
        ))}
      </div>
    );
  }

  return (
    <>
      <CommandBar
        title={t('nav_workbench')}
        subtitle={`${venue.name} · ${roster.name}`}
        actions={<>
          <button className="btn" disabled={busy}>{t('undo')}</button>
          <button className="btn" disabled={busy}>{t('redo')}</button>
          <span className="divider" />
          <button className="btn" onClick={saveSnapshot}>{t('save_snapshot')}</button>
          <span className="relative">
            <button className="btn btn-primary" onClick={() => setMenuOpen(v => !v)}>{t('export')} ▾</button>
            {menuOpen && (
              <div className="menu" onMouseLeave={() => setMenuOpen(false)}>
                {['学生视角 Excel', '教师视角 Excel', 'CSV', 'PNG 图片', 'PDF 打印'].map(x => (
                  <button key={x} onClick={() => { setMenuOpen(false); showToast(`${t('exported')} · ${x}`); }}>{x}</button>
                ))}
              </div>
            )}
          </span>
        </>}
      />
      <div className="layout">
        <div className="workbench">
          <aside className="picker">
            <div className="picker-head">{t('nav_workbench')}</div>
            <div className="select-list">
              <div className="select-group">
                <label>{t('venue')}</label>
                {VENUES.map(v => (
                  <button key={v.id} className={`select-row ${venue.id === v.id ? 'selected' : ''}`} onClick={() => { setVenue(v); setSeats(generate(v, students)); }}>
                    <span className="dot" /><span>{v.name}</span><span className="chip">{v.seats} 座</span>
                  </button>
                ))}
              </div>
              <div className="select-group">
                <label>{t('roster')}</label>
                {ROSTERS.map(r => (
                  <button key={r.id} className={`select-row ${roster.id === r.id ? 'selected' : ''}`} onClick={() => setRoster(r)}>
                    <span className="dot" /><span>{r.name}</span><span className="chip">{r.count} 人</span>
                  </button>
                ))}
              </div>
            </div>
            <div className="picker-foot">
              <button className="btn btn-primary btn-block" onClick={doGenerate} disabled={busy}>{busy ? t('generating') : t('generate')}</button>
              <div className="muted" style={{ fontSize: 'var(--fs-xs)', textAlign: 'center', marginTop: 8 }}>按当前策略生成 · 上次用时 1.8s</div>
            </div>
          </aside>

          <div
            className="canvas-wrap"
            ref={canvasRef}
            onPointerDown={startPan}
            onPointerMove={movePan}
            onPointerUp={endPan}
            onPointerCancel={endPan}
          >
            <div className="canvas-toolbar">缩放 {Math.round(zoom * 100)}% · 拖动空白处平移 · Ctrl/⌘+滚轮缩放 · 拖动座位或学生调换</div>
            <div className="canvas-stage" style={{ width: boardSize.w * zoom, height: boardSize.h * zoom }}>
              <div className="board" ref={boardRef} style={{ transform: `scale(${zoom})`, transformOrigin: 'top left' }}>
                {venue.hasPodium && <div className="podium">讲 台</div>}
                <div className="seat-grid">{rows}</div>
                <div className="legend">
                  <span className="l-occ"><i /> {t('seats_assigned')}</span>
                  <span><i /> {t('seat_empty')}</span>
                  <span className="l-sel"><i /> {t('seat_selected')}</span>
                </div>
              </div>
            </div>
            <div className="zoombar">
              <button className="btn btn-sm" onClick={() => setZoom((z) => Math.max(0.4, +(z - 0.1).toFixed(2)))}>−</button>
              <span className="zoom-label">{Math.round(zoom * 100)}%</span>
              <button className="btn btn-sm" onClick={() => setZoom((z) => Math.min(2, +(z + 0.1).toFixed(2)))}>＋</button>
              <button className="btn btn-sm" onClick={() => setZoom(1)}>100%</button>
              <button className="btn btn-sm" onClick={fitToWindow}>适应窗口</button>
            </div>
          </div>

          <aside className="inspector">
            <div className="tabs">
              <button className={`tab ${tab === 'strategies' ? 'active' : ''}`} onClick={() => setTab('strategies')}>{t('tab_strategies')}</button>
              <button className={`tab ${tab === 'unassigned' ? 'active' : ''}`} onClick={() => setTab('unassigned')}>{t('tab_unassigned')} <b>{unassigned.length}</b></button>
              <button className={`tab ${tab === 'history' ? 'active' : ''}`} onClick={() => setTab('history')}>{t('tab_history')}</button>
              <button className={`tab ${tab === 'messages' ? 'active' : ''}`} onClick={() => setTab('messages')}>{t('tab_messages')} <b>{MESSAGES.length}</b></button>
            </div>
            <div className="panel-body">
              {tab === 'strategies' && STRATEGIES.map(s => (
                <div className="strategy-row" key={s.id}>
                  <span className="prio">{s.priority}</span>
                  <span><span className="name">{s.name}</span><br /><span className="type">{s.detail}</span></span>
                  <span className={`status tag ${s.status.includes('消息') ? 'warn' : s.enabled ? 'ok' : 'muted'}`}>{s.enabled ? s.status : t('disabled')}</span>
                </div>
              ))}
              {tab === 'unassigned' && (
                <>
                  {unassigned.slice(0, 40).map(s => (
                    <div className="student-row" key={s.id} draggable onDragStart={() => setDragStudent(s.id)}>
                      <span>{s.name}</span><span className="chip">{s.height}cm</span>
                    </div>
                  ))}
                  <p className="muted" style={{ fontSize: 'var(--fs-xs)' }}>其余 {Math.max(0, unassigned.length - 40)} 名学生滚动查看 · 拖到座位即可安排</p>
                </>
              )}
              {tab === 'history' && (history.length === 0
                ? <p className="muted" style={{ fontSize: 'var(--fs-xs)' }}>生成或调整座位后，修改记录显示在这里。</p>
                : history.map((h, i) => <div className="student-row" key={i}><span className="muted">{h.at}</span><span>{h.text}</span></div>))}
              {tab === 'messages' && MESSAGES.map(m => (
                <div className="message" key={m.id}><b>{m.strategy}</b> · {m.text}</div>
              ))}
            </div>
          </aside>
        </div>
      </div>
    </>
  );
}

/* ---------------- 人员名单 ---------------- */
function Members() {
  const { t, students, showToast, setDialog } = useApp();
  const [rows, setRows] = useState(students);
  const [dataset, setDataset] = useState(ROSTERS[0]);
  const [query, setQuery] = useState('');
  const [dirty, setDirty] = useState(false);
  const filtered = rows.filter(r => r.name.includes(query));
  const patch = (id, k, v) => { setRows(rs => rs.map(r => r.id === id ? { ...r, [k]: v } : r)); setDirty(true); };
  const switchDataset = (r) => {
    if (dirty) {
      setDialog({
        title: t('dialog_dirty_title'), body: t('dialog_dirty_body'),
        buttons: [{ label: t('discard'), danger: true, onClick: () => { setDataset(r); setDirty(false); } }, { label: t('keep_editing'), primary: true }],
      });
    } else setDataset(r);
  };
  return (
    <>
      <CommandBar title={t('members_title')} subtitle={`${dataset.name} · ${rows.length} 人`} actions={<>
        <span className={`tag ${dirty ? 'warn' : 'muted'}`}>{dirty ? '未保存' : '已保存'}</span>
        <button className="btn" onClick={() => showToast(t('exported') + ' · CSV')}>导出 CSV</button>
        <button className="btn" onClick={() => showToast(t('exported') + ' · Excel')}>导出 Excel</button>
        <button className="btn btn-primary" onClick={() => { setDirty(false); showToast(t('saved')); }}>{t('save')}</button>
      </>} />
      <div className="split">
        <div className="dataset-list">
          {ROSTERS.map(r => (
            <div key={r.id} className={`dataset-row ${dataset.id === r.id ? 'selected' : ''}`} onClick={() => switchDataset(r)}>
              <div className="name">{r.name}</div>
              <div className="meta">{r.count} 人 · 导入于 {r.importedAt}</div>
            </div>
          ))}
          <button className="btn btn-sm">＋ 新建数据集</button>
        </div>
        <div className="page" style={{ flex: 1, minWidth: 0 }}>
          <div className="row">
            <input className="input" style={{ maxWidth: 260 }} name="member-search" aria-label={t('search')} placeholder={t('search')} value={query} onChange={e => setQuery(e.target.value)} />
            <span className="muted" style={{ fontSize: 'var(--fs-xs)' }}>显示 {filtered.length} / {rows.length} 行</span>
            <span className="muted" style={{ fontSize: 'var(--fs-xs)', marginLeft: 'auto' }}>列表已启用虚拟化（原型中以固定行高演示）</span>
          </div>
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th style={{ width: 48 }}>#</th><th>{t('name')}</th><th style={{ width: 120 }}>{t('height')}</th><th style={{ width: 110 }}>{t('gender')}</th><th style={{ width: 100 }}>{t('front_row')}</th><th style={{ width: 60 }} /></tr></thead>
              <tbody>
                {filtered.slice(0, 300).map((s, i) => (
                  <tr key={s.id}>
                    <td className="muted">{i + 1}</td>
                    <td><input type="text" name={`name-${s.id}`} aria-label={`${t('name')} ${i + 1}`} value={s.name} onChange={e => patch(s.id, 'name', e.target.value)} /></td>
                    <td><input type="text" name={`height-${s.id}`} aria-label={`${t('height')} ${i + 1}`} value={s.height} onChange={e => patch(s.id, 'height', Number(e.target.value) || 0)} /></td>
                    <td><select name={`gender-${s.id}`} aria-label={`${t('gender')} ${i + 1}`} value={s.gender} onChange={e => patch(s.id, 'gender', e.target.value)}><option value="male">{t('male')}</option><option value="female">{t('female')}</option></select></td>
                    <td><input type="checkbox" name={`front-${s.id}`} aria-label={`${t('front_row')} ${i + 1}`} checked={s.needsFrontRow} onChange={e => patch(s.id, 'needsFrontRow', e.target.checked)} /></td>
                    <td><button className="btn btn-sm btn-ghost" onClick={() => { setRows(rs => rs.filter(r => r.id !== s.id)); setDirty(true); }}>✕</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </>
  );
}

/* ---------------- 会场与布局 ---------------- */
function Venues() {
  const { t, showToast } = useApp();
  const [venue, setVenue] = useState(VENUES[0]);
  const [mode, setMode] = useState('Grid');
  const [params, setParams] = useState({ rows: 8, columns: 8, seatsPerDesk: 2, aisle: 4 });
  const [preview, setPreview] = useState({ seats: 64, pending: false });
  const [dirty, setDirty] = useState(false);
  const [polarRings, setPolarRings] = useState(4);
  const [polarPerRing, setPolarPerRing] = useState(12);
  const timer = useRef(null);

  const update = (k, v) => {
    setParams(p => ({ ...p, [k]: v })); setDirty(true);
    setPreview(p => ({ ...p, pending: true }));
    clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      const rows = Math.max(1, Math.min(30, Number(params.rows) || 1));
      const cols = Math.max(1, Math.min(20, Number(params.columns) || 1));
      setPreview({ seats: (k === 'rows' ? rows : params.rows) * (k === 'columns' ? cols : params.columns), pending: false });
    }, 120);
  };

  const seatCells = Array.from({ length: Math.min(preview.seats, 120) }).map((_, i) => (
    <div key={i} className="seat occupied" style={{ width: 26, height: 26 }} />
  ));

  // 极坐标：按环/每环座位数渲染（真实实现由 PolarLayoutBuilder 生成几何）
  const polarCells = [];
  for (let r = 1; r <= polarRings; r++) {
    const rad = 36 + (r - 1) * 34;
    for (let i = 0; i < polarPerRing; i++) {
      const a = (i / polarPerRing) * Math.PI * 2 + (r - 1) * 0.12;
      polarCells.push(
        <div key={`p-${r}-${i}`} className="seat occupied"
          style={{ position: 'absolute', width: 22, height: 22, left: 260 + rad * Math.cos(a) - 11, top: 160 + rad * Math.sin(a) - 11 }}
          title={`第${r}环 第${i + 1}座`} />
      );
    }
  }
  // 自由布局：显式坐标散点（真实实现由 FreeformLayoutBuilder / 坐标表生成）
  const freePointsData = [[120, 90], [200, 70], [300, 100], [400, 80], [150, 180], [260, 170], [370, 190], [180, 260], [300, 270], [420, 250], [240, 320], [360, 330]];
  const freePoints = freePointsData.map(([x, y], i) => (
    <div key={`f-${i}`} className="seat occupied" style={{ position: 'absolute', width: 22, height: 22, left: x, top: y }} title={`点 ${i + 1}`} />
  ));

  const previewContent = mode === 'Grid'
    ? <div className="seat-grid" style={{ display: 'flex', flexWrap: 'wrap', maxWidth: 520, gap: 6 }}>{seatCells}</div>
    : mode === 'Polar'
      ? <div className="preview-canvas">{polarCells}<div className="podium" style={{ position: 'absolute', left: 260 - 33, top: 160 - 13, margin: 0, width: 66 }}>中心</div></div>
      : <div className="preview-canvas">{freePoints}</div>;
  const previewCount = mode === 'Grid' ? preview.seats : mode === 'Polar' ? polarRings * polarPerRing : freePoints.length;

  return (
    <>
      <CommandBar title={t('venues_title')} subtitle={venue.name} actions={<>
        <span className={`tag ${dirty ? 'warn' : 'muted'}`}>{dirty ? '未保存' : '已保存'}</span>
        <button className="btn" onClick={() => showToast('已删除（示例）')}>删除</button>
        <button className="btn btn-primary" onClick={() => { setDirty(false); showToast(t('saved')); }}>{t('save')}</button>
      </>} />
      <div className="split">
        <div className="dataset-list">
          {VENUES.map(v => (
            <div key={v.id} className={`dataset-row ${venue.id === v.id ? 'selected' : ''}`} onClick={() => setVenue(v)}>
              <div className="name">{v.name}</div>
              <div className="meta">{v.seats} 座 · {v.type}</div>
            </div>
          ))}
          <button className="btn btn-sm">＋ 新建会场</button>
        </div>
        <div className="page" style={{ flex: 1, minWidth: 0 }}>
          <div className="card">
            <h3>布局类型</h3>
            <div className="radio-row">
              {['Grid', 'Polar', 'Freeform'].map(m => (
                <label key={m}><input type="radio" checked={mode === m} onChange={() => setMode(m)} /> {t(m.toLowerCase())}</label>
              ))}
            </div>
          </div>
          {mode === 'Grid' && (
            <div className="card">
              <h3>基础参数 <span className="hint">（输入后 120ms 防抖重算预览）</span></h3>
              <div className="param-grid">
                {[['rows', t('rows')], ['columns', t('columns')], ['seatsPerDesk', t('seats_per_desk')], ['aisle', t('aisle')]].map(([k, label]) => (
                  <div className="field" key={k}><label>{label}</label>
                    <input className="input" name={`venue-param-${k}`} aria-label={label} value={params[k]} onChange={e => update(k, e.target.value)} />
                  </div>
                ))}
              </div>
            </div>
          )}
          {mode === 'Polar' && (
            <div className="card">
              <h3>{t('polar')} <span className="hint">（真实实现含起止角度、径向/环间通道等全部参数）</span></h3>
              <div className="param-grid">
                <div className="field"><label>环数</label>
                  <input className="input" name="polar-rings" aria-label="环数" value={polarRings}
                    onChange={e => { setPolarRings(Math.max(1, Math.min(8, Number(e.target.value) || 1))); setDirty(true); }} />
                </div>
                <div className="field"><label>每环座位数</label>
                  <input className="input" name="polar-per-ring" aria-label="每环座位数" value={polarPerRing}
                    onChange={e => { setPolarPerRing(Math.max(4, Math.min(36, Number(e.target.value) || 4))); setDirty(true); }} />
                </div>
              </div>
            </div>
          )}
          {mode === 'Freeform' && (
            <div className="card">
              <h3>{t('freeform')} <span className="hint">（原「自由点管理」已并入本页）</span></h3>
              <table className="table">
                <thead><tr><th>ID</th><th>X</th><th>Y</th><th>行列</th><th>分组</th><th>类型</th></tr></thead>
                <tbody>{freePointsData.slice(0, 8).map(([x, y], i) => (
                  <tr key={i}><td>p-{i + 1}</td><td>{x}</td><td>{y}</td><td>{Math.ceil((i + 1) / 4)}-{(i % 4) + 1}</td><td>第{(i % 3) + 1}组</td><td>座位</td></tr>
                ))}</tbody>
              </table>
            </div>
          )}
          <div className="card">
            <div className="spread" style={{ marginBottom: 12 }}>
              <h3 style={{ margin: 0 }}>{t('preview')}</h3>
              <span className="muted" style={{ fontSize: 'var(--fs-xs)' }}>{preview.pending ? '重算中…' : `共 ${previewCount} 个座位`}</span>
            </div>
            <div className="venue-preview">{previewContent}</div>
          </div>
        </div>
      </div>
    </>
  );
}

/* ---------------- 策略配置 ---------------- */
function Strategies() {
  const { t, showToast } = useApp();
  const [items, setItems] = useState(STRATEGIES);
  const [sel, setSel] = useState(STRATEGIES[0]);
  const toggle = (id) => setItems(list => list.map(s => s.id === id ? { ...s, enabled: !s.enabled } : s));
  const move = (id, d) => setItems(list => list.map(s => s.id === id ? { ...s, priority: s.priority + d } : s));
  const conflict = items.filter(s => s.enabled).some((s, i, a) => a.some(o => o !== s && o.enabled && o.priority === s.priority));
  return (
    <>
      <CommandBar title={t('strategies_title')} actions={<>
        <button className="btn">重置默认</button>
        <button className="btn btn-primary" onClick={() => showToast(t('saved'))}>全部保存</button>
      </>} />
      {conflict && <div style={{ padding: '0 var(--sp-4)', marginTop: 8 }}><div className="banner warn">检测到优先级冲突：相同优先级的策略按列表顺序执行。建议调整其中一项。</div></div>}
      <div className="split">
        <div className="strategy-list">
          {items.map(s => (
            <div key={s.id} className={`strategy-item ${s.kind === 'dependent' ? 'dependent' : ''} ${sel.id === s.id ? 'selected' : ''}`} onClick={() => setSel(s)}>
              <span className={`prio`} style={{ marginTop: 2 }}>{s.priority}</span>
              <div style={{ flex: 1 }}>
                <div className="title">{s.name} {s.kind === 'dependent' && <span className="tag muted">依赖</span>}</div>
                <div className="desc">{s.desc}</div>
              </div>
              <span className={`switch ${s.enabled ? 'on' : ''}`} onClick={(e) => { e.stopPropagation(); toggle(s.id); }} />
            </div>
          ))}
        </div>
        <div className="page" style={{ flex: 1, minWidth: 0 }}>
          <div className="card">
            <h3>{sel.name}</h3>
            <p className="muted">{sel.desc}</p>
            <div className="setting-row"><span><div className="label">{t('priority')}</div><div className="desc">数字越大越先执行</div></span>
              <span className="row"><button className="btn btn-sm" onClick={() => move(sel.id, -1)}>−</button><b>{sel.priority}</b><button className="btn btn-sm" onClick={() => move(sel.id, 1)}>＋</button></span>
            </div>
            <div className="setting-row"><span><div className="label">{t('enabled')}</div><div className="desc">停用后不参与本次生成</div></span>
              <span className={`switch ${sel.enabled ? 'on' : ''}`} onClick={() => toggle(sel.id)} />
            </div>
            <div className="setting-row"><span><div className="label">参数</div><div className="desc">由策略清单声明（NumberInput / ToggleSwitch / Dropdown）</div></span>
              <span className="row"><input className="input" style={{ width: 80 }} name="strategy-param-window" aria-label="历史窗口" defaultValue="10" /><span className="muted" style={{ fontSize: 'var(--fs-xs)' }}>历史窗口</span></span>
            </div>
          </div>
          <div className="card">
            <h3>配置块</h3>
            <p className="muted" style={{ fontSize: 'var(--fs-sm)' }}>行 1 · 学生 A ↔ 学生 B（同桌）　行 2 · 学生 C ↔ 学生 D　…</p>
            <button className="btn btn-sm">＋ 新增配置行</button>
          </div>
        </div>
      </div>
    </>
  );
}

/* ---------------- 历史快照 ---------------- */
function Snapshots() {
  const { t, snapshots, setDialog, showToast } = useApp();
  const [sel, setSel] = useState(snapshots[0]);
  const [batch, setBatch] = useState(false);
  const [checked, setChecked] = useState([]);
  const cells = Array.from({ length: sel?.assigned ?? 64 }).map((_, i) => <div key={i} className="seat occupied" style={{ width: 24, height: 24 }} />);
  return (
    <>
      <CommandBar title={t('snapshots_title')} subtitle={`${snapshots.length} 个快照`} actions={<>
        <button className="btn" onClick={() => { setBatch(v => !v); setChecked([]); }}>{batch ? t('cancel') : t('batch_delete')}</button>
        {batch && <button className="btn btn-danger" disabled={!checked.length} onClick={() => showToast(`已删除 ${checked.length} 个快照（示例）`)}>{t('delete')} {checked.length || ''}</button>}
        <button className="btn btn-primary" onClick={() => showToast(t('snapshot_saved'))}>{t('save_snapshot')}</button>
      </>} />
      <div className="split">
        <div className="snapshot-list">
          {snapshots.map(s => (
            <div key={s.id} className={`snapshot-item ${sel?.id === s.id ? 'selected' : ''}`} onClick={() => setSel(s)}>
              <div className="row">
                {batch && <input type="checkbox" checked={checked.includes(s.id)} onChange={e => setChecked(c => e.target.checked ? [...c, s.id] : c.filter(x => x !== s.id))} />}
                <b style={{ fontSize: 'var(--fs-sm)' }}>{s.name}</b>
                <span className="chip" style={{ marginLeft: 'auto' }}>{s.assigned}/{s.total}</span>
              </div>
              <div className="muted" style={{ fontSize: 'var(--fs-xs)', marginTop: 4 }}>{s.venue} · {s.date} · {s.note}</div>
            </div>
          ))}
        </div>
        <div className="page" style={{ flex: 1, minWidth: 0 }}>
          {sel && <>
            <div className="banner info">快照自带会场布局与数据指纹；会场或名单变化时会提示回滚风险。</div>
            <div className="card">
              <div className="spread" style={{ marginBottom: 12 }}>
                <h3 style={{ margin: 0 }}>{sel.name}</h3>
                <button className="btn btn-primary" onClick={() => setDialog({
                  title: t('rollback_title'), body: t('rollback_body'),
                  buttons: [{ label: t('cancel') }, { label: t('confirm'), primary: true, onClick: () => showToast('已回滚（示例）') }],
                })}>{t('rollback')}</button>
              </div>
              <div className="venue-preview"><div className="seat-grid" style={{ display: 'flex', flexWrap: 'wrap', maxWidth: 480, gap: 6 }}>{cells}</div></div>
            </div>
          </>}
        </div>
      </div>
    </>
  );
}

/* ---------------- 设置 ---------------- */
function Settings() {
  const { t, theme, setTheme, lang, setLang, showToast } = useApp();
  const [shortcuts, setShortcuts] = useState({ undo: true, redo: true, save: true, zoom: true, del: true, esc: true });
  return (
    <>
      <CommandBar title={t('settings_title')} actions={<button className="btn btn-primary" onClick={() => showToast(t('saved'))}>{t('save')}</button>} />
      <div className="page">
        <div className="settings-grid">
          <div className="card">
            <h3>{t('appearance')}</h3>
            <div className="setting-row"><span><div className="label">{t('theme')}</div><div className="desc">浅色为默认，深色为夜间场景</div></span>
              <span className="seg"><button className={theme === 'light' ? 'on' : ''} onClick={() => setTheme('light')}>{t('light')}</button><button className={theme === 'dark' ? 'on' : ''} onClick={() => setTheme('dark')}>{t('dark')}</button></span>
            </div>
            <div className="setting-row"><span><div className="label">{t('language')}</div><div className="desc">中文 / English</div></span>
              <span className="seg"><button className={lang === 'zh' ? 'on' : ''} onClick={() => setLang('zh')}>中文</button><button className={lang === 'en' ? 'on' : ''} onClick={() => setLang('en')}>EN</button></span>
            </div>
          </div>
          <div className="card">
            <h3>{t('behavior')}</h3>
            <div className="setting-row"><span><div className="label">确认后再清空数据</div></span><span className="switch on" /></div>
            <div className="setting-row"><span><div className="label">快捷导出上次格式</div></span><span className="switch" /></div>
          </div>
          <div className="card">
            <h3>{t('shortcuts')}</h3>
            {[['undo', 'Ctrl+Z 撤销'], ['redo', 'Ctrl+Y 重做'], ['save', 'Ctrl+S 保存'], ['zoom', 'Ctrl+滚轮 缩放'], ['del', 'Delete 删除'], ['esc', 'Esc 取消']].map(([k, label]) => (
              <div className="setting-row" key={k}><span className="label">{label}</span>
                <span className={`switch ${shortcuts[k] ? 'on' : ''}`} onClick={() => setShortcuts(s => ({ ...s, [k]: !s[k] }))} />
              </div>
            ))}
          </div>
          <div className="card">
            <h3>{t('storage')}</h3>
            <div className="setting-row"><span><div className="label">快照配额</div><div className="desc">每个会场最多 30 个</div></span><input className="input" style={{ width: 80 }} name="snapshot-quota" aria-label="快照配额" defaultValue="30" /></div>
            <div className="setting-row"><span><div className="label">日志级别</div></span><select className="input" style={{ width: 130 }} name="log-level" aria-label="日志级别" defaultValue="Information"><option>Debug</option><option>Information</option><option>Warning</option></select></div>
          </div>
        </div>
      </div>
    </>
  );
}

/* ---------------- 关于 ---------------- */
function About() {
  const { t } = useApp();
  const deps = ['Avalonia 12.1.2', 'CommunityToolkit.Mvvm 8.4.2', '.NET 10', 'EPPlus 8', 'Serilog 4', 'FluentIcons'];
  return (
    <>
      <CommandBar title={t('about_title')} />
      <div className="page">
        <div className="card">
          <div className="row" style={{ gap: 16 }}>
            <div className="brand-mark" style={{ width: 56, height: 56, fontSize: 26, borderRadius: 10 }}>S</div>
            <div>
              <h3 style={{ marginBottom: 4 }}>SeatFlow</h3>
              <div className="muted">{t('version')} 2.0.0-rc.1+87ecb7b · 2026-10-01</div>
              <p className="dim" style={{ maxWidth: 640 }}>跨平台的智能排座系统：支持网格 / 极坐标 / 自由点布局，策略引擎与数据导入导出。本页面为 UI 重构方向 B 的高保真样机。</p>
            </div>
          </div>
        </div>
        <div className="card">
          <h3>{t('deps')}</h3>
          <div className="row" style={{ flexWrap: 'wrap' }}>{deps.map(d => <span className="chip" key={d}>{d}</span>)}</div>
        </div>
      </div>
    </>
  );
}

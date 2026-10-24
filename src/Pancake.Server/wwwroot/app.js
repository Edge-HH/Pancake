'use strict';
/* Pancake 集控网页控制台：无框架、无构建步骤，基于 hash 路由。 */

// ============================================================ 工具
class Raw { constructor(s) { this.s = s; } toString() { return this.s; } }
const raw = s => new Raw(s);
const esc = v => String(v).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const show = v => v == null || v === false ? '' : v instanceof Raw ? v.s : Array.isArray(v) ? v.map(show).join('') : esc(v);
/** 模板字符串默认转义插值；嵌套的 html`` 结果原样拼接。 */
function html(strings, ...vals) { let o = strings[0]; vals.forEach((v, i) => { o += show(v) + strings[i + 1]; }); return raw(o); }
const icon = (name, cls = '') => raw(`<svg class="icon ${cls}" aria-hidden="true"><use href="#i-${name}"/></svg>`);
const logo = (cls = 'brand-mark') => raw(`<svg class="${cls}" viewBox="0 0 40 40" aria-hidden="true"><use href="#logo"/></svg>`);
const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
const pad = n => String(n).padStart(2, '0');

const store = {
  get(k, d) { try { const v = localStorage.getItem('pancake.' + k); return v == null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem('pancake.' + k, JSON.stringify(v)); } catch { } },
  del(k) { try { localStorage.removeItem('pancake.' + k); } catch { } }
};

// ---------- 时间
function parseTime(s) {
  if (!s) return null;
  if (typeof s === 'string' && !/[zZ]$|[+-]\d\d:\d\d$/.test(s)) s += 'Z';
  const d = new Date(s); return isNaN(d) ? null : d;
}
const ymd = d => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const hm = d => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
const todayStr = () => ymd(new Date());
const shiftDay = (s, n) => { const [y, m, d] = s.split('-').map(Number); return ymd(new Date(y, m - 1, d + n)); };
function fullTime(s) { const d = parseTime(s); return d ? `${ymd(d)} ${hm(d)}` : '—'; }
function relTime(s) {
  const d = parseTime(s); if (!d) return '从未';
  const sec = (Date.now() - d) / 1000;
  if (sec < 45) return '刚刚';
  if (sec < 3600) return Math.round(sec / 60) + ' 分钟前';
  if (ymd(d) === todayStr()) return '今天 ' + hm(d);
  if (ymd(d) === shiftDay(todayStr(), -1)) return '昨天 ' + hm(d);
  if (sec < 7 * 86400) return Math.floor(sec / 86400) + ' 天前';
  return ymd(d);
}
const WEEK = '日一二三四五六';
function dateLabel(s, long = false) {
  if (!s) return '未设置日期';
  const t = todayStr();
  const [y, m, d] = s.split('-').map(Number);
  const w = '周' + WEEK[new Date(y, m - 1, d).getDay()];
  const rel = s === t ? '今天' : s === shiftDay(t, -1) ? '昨天' : s === shiftDay(t, 1) ? '明天' : '';
  const base = `${y === new Date().getFullYear() ? '' : y + '年'}${m}月${d}日`;
  return long ? `${base} ${w}${rel ? ' · ' + rel : ''}` : rel ? `${rel} · ${m}月${d}日` : `${base} ${w}`;
}
function duration(sec) {
  const d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
  return d ? `${d} 天 ${h} 小时` : h ? `${h} 小时 ${m} 分钟` : `${m} 分钟`;
}

const roleLabel = r => r === 'Administrator' ? '管理员' : r === 'Teacher' ? '老师' : r;
function avatar(name, cls = '') {
  let h = 0; for (const c of String(name)) h = (h * 31 + c.charCodeAt(0)) % 360;
  return html`<span class="avatar ${cls}" style="background:hsl(${h} 45% 47%)">${String(name || '?').trim().charAt(0).toUpperCase()}</span>`;
}
function randomPassword() {
  const a = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const bytes = crypto.getRandomValues(new Uint8Array(10));
  return [...bytes].map(b => a[b % a.length]).join('');
}
async function copyText(text, label = '已复制') {
  try { await navigator.clipboard.writeText(text); }
  catch {
    const t = document.createElement('textarea'); t.value = text; t.style.position = 'fixed'; t.style.opacity = '0';
    document.body.appendChild(t); t.select(); document.execCommand('copy'); t.remove();
  }
  toast(label);
}

// ============================================================ 主题
const theme = {
  get: () => { try { return localStorage.getItem('pancake.theme') || 'system'; } catch { return 'system'; } },
  set(v) { try { localStorage.setItem('pancake.theme', v); } catch { } theme.apply(); },
  apply() {
    const t = theme.get();
    const dark = t === 'dark' || (t === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  },
  toggle() { theme.set(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark'); }
};
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', theme.apply);

// ============================================================ 接口
class ApiError extends Error { constructor(status, message) { super(message); this.status = status; } }
const STATUS_TEXT = { 400: '请求无效', 401: '需要登录', 403: '没有权限执行此操作', 404: '内容不存在或已删除', 409: '数据冲突，请刷新后重试', 429: '操作过于频繁，请稍后再试', 500: '服务器内部错误' };
const state = { token: store.get('token', ''), me: null };

async function api(path, { method = 'GET', body, auth = true } = {}) {
  const headers = {};
  if (body !== undefined) headers['content-type'] = 'application/json';
  if (auth && state.token) headers.authorization = 'Bearer ' + state.token;
  let r;
  try { r = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) }); }
  catch { throw new ApiError(0, '无法连接服务器，请检查网络'); }
  if (r.status === 401 && auth && state.token && !path.startsWith('/api/auth/login')) {
    clearSession(); navigate('login?expired=1'); throw new ApiError(401, '登录已过期，请重新登录');
  }
  if (!r.ok) {
    let msg = '';
    try { const t = await r.text(); try { const j = JSON.parse(t); msg = typeof j === 'string' ? j : (j.detail || j.error || ''); } catch { msg = t.length < 200 ? t : ''; } } catch { }
    throw new ApiError(r.status, msg || STATUS_TEXT[r.status] || `请求失败（${r.status}）`);
  }
  if (r.status === 204) return null;
  return (r.headers.get('content-type') || '').includes('json') ? r.json() : r.text();
}

async function download(path) {
  const r = await fetch(path, { headers: state.token ? { authorization: 'Bearer ' + state.token } : {} });
  if (!r.ok) throw new ApiError(r.status, STATUS_TEXT[r.status] || '下载失败');
  const cd = r.headers.get('content-disposition') || '';
  const m = /filename\*=UTF-8''([^;]+)/i.exec(cd) || /filename="?([^";]+)"?/i.exec(cd);
  const name = m ? decodeURIComponent(m[1]) : 'download';
  const url = URL.createObjectURL(await r.blob());
  const a = document.createElement('a'); a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}

function clearSession() { state.token = ''; state.me = null; myAssignments = null; store.del('token'); }
const isAdmin = () => state.me?.role === 'Administrator';
async function ensureMe() {
  if (!state.token) return false;
  if (state.me) return true;
  try { state.me = await api('/api/auth/me'); return true; } catch { clearSession(); return false; }
}

/** 断线自动重连的 WebSocket；返回关闭函数。 */
function liveSocket(path, onEvent, onStatus = () => { }) {
  let ws, closed = false, delay = 1000, timer;
  const open = () => {
    ws = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + path);
    ws.onopen = () => { delay = 1000; onStatus(true); };
    ws.onmessage = e => { try { onEvent(JSON.parse(e.data)); } catch { } };
    ws.onclose = () => { onStatus(false); if (!closed) timer = setTimeout(open, delay = Math.min(delay * 2, 30000)); };
  };
  open();
  return () => { closed = true; clearTimeout(timer); ws && ws.close(); };
}

// ============================================================ 界面组件
function toast(message, type = 'ok') {
  const el = document.createElement('div');
  el.className = 'toast ' + type;
  el.innerHTML = show(html`${icon(type === 'error' ? 'alert' : 'check')}<span>${message}</span>`);
  $('#toasts').appendChild(el);
  setTimeout(() => { el.style.transition = 'opacity .3s'; el.style.opacity = '0'; setTimeout(() => el.remove(), 300); }, type === 'error' ? 4500 : 2600);
}
const fail = err => toast(err?.message || String(err), 'error');

/**
 * 表单对话框。onSubmit 抛出的错误显示在对话框里，返回值作为 Promise 结果。
 * 返回 false 时保持打开。
 */
function modal({ title, body, submit = '确定', cancel = '取消', danger = false, wide = false, onSubmit, onMount }) {
  return new Promise(resolve => {
    const dlg = document.createElement('dialog');
    dlg.className = 'modal' + (wide ? ' wide' : '');
    dlg.innerHTML = show(html`<form novalidate>
      <div class="modal-head"><h3>${title}</h3><span class="spacer"></span><button type="button" class="btn ghost sm icon-only" data-close aria-label="关闭">${icon('x')}</button></div>
      <div class="modal-body">${body}<div class="form-error" role="alert"></div></div>
      <div class="modal-foot">${cancel ? html`<button type="button" class="btn" data-close>${cancel}</button>` : ''}${submit ? html`<button type="submit" class="btn ${danger ? 'danger solid' : 'primary'}">${submit}</button>` : ''}</div>
    </form>`);
    document.body.appendChild(dlg);
    const form = $('form', dlg), err = $('.form-error', dlg);
    let result;
    const close = () => dlg.close();
    $$('[data-close]', dlg).forEach(b => b.onclick = close);
    dlg.addEventListener('close', () => { dlg.remove(); resolve(result); });
    form.onsubmit = async e => {
      e.preventDefault(); err.textContent = '';
      const btn = $('button[type=submit]', dlg); if (btn) btn.disabled = true;
      try {
        const r = onSubmit ? await onSubmit(form, dlg) : true;
        if (r !== false) { result = r === undefined ? true : r; close(); }
      } catch (ex) { err.textContent = ex.message; }
      finally { if (btn) btn.disabled = false; }
    };
    dlg.showModal();
    onMount?.(form, dlg, close);
    const first = $('input:not([type=checkbox]):not([readonly]),select,textarea', form); if (first) first.focus();
  });
}
const confirmDialog = ({ title, message, confirm = '确定', danger = true }) =>
  modal({ title, body: html`<p class="muted">${message}</p>`, submit: confirm, danger });

// ---------- 全局点击委托：data-act="名称"
let viewActions = {};
const globalActions = {
  'toggle-theme': () => theme.toggle(),
  'logout': async () => { try { await api('/api/auth/logout', { method: 'POST' }); } catch { } clearSession(); navigate(''); toast('已退出登录'); },
  'toggle-nav': () => $('.shell')?.classList.toggle('nav-open'),
  'copy': el => copyText(el.dataset.text, el.dataset.label || '已复制'),
  'print': () => print()
};
document.addEventListener('click', e => {
  const el = e.target.closest('[data-act]');
  if (!el) { if (e.target.closest('.shell.nav-open') && !e.target.closest('.sidebar')) $('.shell').classList.remove('nav-open'); return; }
  const fn = viewActions[el.dataset.act] || globalActions[el.dataset.act];
  if (fn) { e.preventDefault(); Promise.resolve(fn(el, e)).catch(fail); }
});

// ============================================================ 路由
let disposers = [];
const onDispose = fn => disposers.push(fn);
const navigate = path => { const h = '#/' + path; if (location.hash === h) route(); else location.hash = h; };
const routes = [
  [/^$/, viewLanding, { public: true }],
  [/^login$/, viewLogin, { public: true }],
  [/^c\/([^/]+)(?:\/([\w-]+))?$/, viewPublic, { public: true }],
  [/^overview$/, viewOverview],
  [/^classes$/, viewClasses],
  [/^classes\/([\w-]+)(?:\/(\w+))?$/, viewClass],
  [/^homework(?:\/([\w-]+))?(?:\/([\w-]+))?$/, viewHomework],
  [/^users$/, viewUsers, { admin: true }],
  [/^audit$/, viewAudit, { admin: true }],
  [/^announcements$/, viewAnnouncements],
  [/^settings$/, viewSettings]
];
let routeToken = 0;
async function route() {
  const token = ++routeToken;
  document.body.classList.add('routing');
  disposers.forEach(fn => { try { fn(); } catch { } }); disposers = []; viewActions = {};
  $('.drawer')?.remove(); $('.scrim')?.remove();
  const [path, qs] = decodeURIComponent(location.hash.replace(/^#\/?/, '')).split('?');
  const query = new URLSearchParams(qs || '');
  for (const [re, fn, opt = {}] of routes) {
    const m = path.match(re); if (!m) continue;
    if (!opt.public && !await ensureMe()) return navigate('login?next=' + encodeURIComponent(path));
    if (opt.admin && !isAdmin()) return navigate('overview');
    if (token !== routeToken) return;
    try { await fn(...m.slice(1), query); } catch (err) { if (err.status !== 401) renderError(err); }
    finally { if (token === routeToken) document.body.classList.remove('routing'); }
    return;
  }
  navigate(state.token ? 'overview' : '');
}
window.addEventListener('hashchange', route);
window.addEventListener('scroll', () => $('.topbar')?.classList.toggle('scrolled', scrollY > 4), { passive: true });

function renderError(err) {
  const target = $('#page') || $('#app');
  target.innerHTML = show(html`<div class="empty">${icon('alert')}<b>出错了</b><span>${err.message}</span><button class="btn" style="margin-top:10px" onclick="location.reload()">${icon('refresh')}重新加载</button></div>`);
}

// ============================================================ 应用框架
function shell({ nav, title, crumbs, actions = '' }) {
  const me = state.me;
  const link = (key, href, ic, label, extra = '') => html`<a href="#/${href}" class="${nav === key ? 'active' : ''}">${icon(ic)}<span>${label}</span>${extra}</a>`;
  $('#app').innerHTML = show(html`<div class="shell">
    <aside class="sidebar">
      <a class="brand" href="#/overview">${logo()}<div><b>Pancake 集控</b><small>班级作业同步中心</small></div></a>
      <nav class="nav">
        ${link('overview', 'overview', 'home', '概览')}
        ${link('classes', 'classes', 'class', '班级')}
        ${link('homework', 'homework', 'book', '作业')}
        ${link('announcements', 'announcements', 'megaphone', '公告')}
        ${isAdmin() ? html`<div class="nav-label">管理</div>${link('users', 'users', 'users', '账号')}${link('audit', 'audit', 'log', '操作日志')}` : ''}
        <div class="nav-label">系统</div>
        ${link('settings', 'settings', 'settings', '设置')}
      </nav>
      <div class="sidebar-foot">
        <div class="user-card">${avatar(me.displayName)}<div class="grow"><b>${me.displayName}</b><span>${roleLabel(me.role)} · ${me.userName}</span></div></div>
        <div class="row">
          <button class="btn ghost sm grow" data-act="toggle-theme" title="切换深浅色">${icon('moon')}<span>主题</span></button>
          <button class="btn ghost sm grow" data-act="logout">${icon('logout')}<span>退出</span></button>
        </div>
      </div>
    </aside>
    <div class="main">
      <header class="topbar">
        <button class="btn ghost icon-only menu-toggle" data-act="toggle-nav" aria-label="菜单">${icon('menu')}</button>
        <div class="grow">${crumbs ? html`<div class="crumbs">${crumbs}</div>` : ''}<h1>${title}</h1></div>
        <div class="row wrap">${actions}</div>
      </header>
      <main class="page" id="page"></main>
    </div>
  </div>`);
  return $('#page');
}
const setPage = (el, content) => { el.innerHTML = show(content); };
const loading = () => html`<div class="card card-body stack"><div class="skeleton" style="width:40%"></div><div class="skeleton"></div><div class="skeleton" style="width:70%"></div></div>`;
// ---------- 人员与班务通用
const DUTY_LABEL = { HeadTeacher: '班主任', SubjectTeacher: '科任老师', Assistant: '助教' };
const WEEKDAY_NAMES = ['', '周一', '周二', '周三', '周四', '周五', '周六', '周日'];
const LEVEL_LABEL = { info: '普通', important: '重要', urgent: '紧急' };
const levelBadge = l => html`<span class="badge ${l === 'urgent' ? 'danger' : l === 'important' ? 'warn' : 'info'}">${LEVEL_LABEL[l] || l}</span>`;
let myAssignments = null;
async function assignments() { if (!myAssignments) myAssignments = await api('/api/me/assignments').catch(() => []); return myAssignments; }
/** 当前用户在班级里的身份：管理员返回 Administrator，老师返回职务，未分配返回 null。 */
async function myDuty(classId) { if (isAdmin()) return 'Administrator'; return (await assignments()).find(a => a.classId === classId)?.duty || null; }
/** 网页编辑作业时允许改动的科目（小写）；null 表示不限。 */
async function editableSubjects(classId) {
  if (isAdmin()) return null;
  const a = (await assignments()).find(x => x.classId === classId);
  if (!a || a.duty === 'HeadTeacher' || !a.subjects.length) return null;
  const set = new Set(a.subjects.map(x => x.trim().toLowerCase()));
  set.names = a.subjects;
  return set;
}
const shortId = () => Math.random().toString(36).slice(2, 10);
const consoleAsset = classId => sha => `/api/classes/${classId}/assets/${sha}?access_token=${encodeURIComponent(state.token)}`;
/** 多选对话框：返回选中的 value 数组，取消返回 undefined。 */
function checklistModal({ title, items, selected = [], submit = '确定', hint = '', emptyText = '没有可选项。' }) {
  return modal({
    title, submit,
    body: html`${hint ? html`<p class="muted">${hint}</p>` : ''}
      ${items.length ? html`<label class="input-icon">${icon('search', 'sm')}<input class="input" data-filter placeholder="筛选"></label>
      <div class="checklist tall">${items.map(i => html`<label class="check" data-label="${i.label.toLowerCase()}"><input type="checkbox" value="${i.value}" ${selected.includes(i.value) ? 'checked' : ''}>${i.label}${i.sub ? html`<span class="faint" style="font-size:12px">${i.sub}</span>` : ''}</label>`)}</div>
      <div class="row"><button type="button" class="btn ghost sm" data-all>全选</button><button type="button" class="btn ghost sm" data-none>全不选</button></div>` : html`<p class="faint">${emptyText}</p>`}`,
    onMount: f => {
      const filter = $('[data-filter]', f);
      if (filter) filter.oninput = () => $$('.checklist label', f).forEach(l => l.classList.toggle('hidden', !l.dataset.label.includes(filter.value.trim().toLowerCase())));
      const all = $('[data-all]', f), none = $('[data-none]', f);
      if (all) all.onclick = () => $$('.checklist label:not(.hidden) input', f).forEach(x => x.checked = true);
      if (none) none.onclick = () => $$('.checklist input', f).forEach(x => x.checked = false);
    },
    onSubmit: f => $$('.checklist input:checked', f).map(x => x.value)
  });
}
const progress = (value, max) => html`<span class="progress"><span style="width:${max ? Math.round(value / max * 100) : 0}%"></span></span>`;

const empty = (ic, title, text, action = '') => html`<div class="empty">${icon(ic)}<b>${title}</b><span>${text}</span>${action ? html`<div style="margin-top:12px">${action}</div>` : ''}</div>`;

// ============================================================ 作业内容解析
const PLACEHOLDER = '在这里输入作业内容';
const pick = (o, k) => o == null ? undefined : o[k] ?? o[k[0].toLowerCase() + k.slice(1)];
function safeColor(hex) {
  if (typeof hex !== 'string') return null;
  const m = /^#([0-9a-f]{6}|[0-9a-f]{8})$/i.exec(hex.trim());
  if (!m) return null;
  return '#' + (m[1].length === 8 ? m[1].slice(2) : m[1]);
}
/**
 * 桌面端的项目 JSON 为 PascalCase；保留原始序号，网页编辑时据此告诉服务端改的是哪一项。
 * runs 是服务端按科目、条目解析好的富文本片段（旧服务端没有时按纯文本处理）。
 */
function parseProject(payloadJson, runs) {
  let doc = {}; try { doc = JSON.parse(payloadJson || '{}'); } catch { }
  return (pick(doc, 'Subjects') || []).map((s, si) => ({
    index: si,
    name: (pick(s, 'Name') || '').trim() || '未命名科目',
    color: safeColor(pick(s, 'AccentHex')),
    ink: (pick(s, 'InkStrokes') || []).length > 0,
    rect: { x: +pick(s, 'X') || 0, y: +pick(s, 'Y') || 0, w: +pick(s, 'Width') || 0, h: +pick(s, 'Height') || 0 },
    entries: (pick(s, 'Entries') || []).map((e, ei) => {
      let text = String(pick(e, 'Content') || '').replace(/\r\n?/g, '\n').replace(/\s+$/, '');
      if (text.trim() === PLACEHOLDER) text = '';
      const rich = runs?.[si]?.[ei] ? rtFromApi(runs[si][ei]) : text ? [rtRun(text)] : [];
      return { index: ei, text, runs: rich, hand: !!pick(e, 'HasHandwriting'), atts: (pick(e, 'Attachments') || []).map((a, ai) => ({ index: ai, name: pick(a, 'Name') || '附件', kind: pick(a, 'Kind') || '文件', path: pick(a, 'Path') || '' })) };
    })
  }));
}
/** 展示用：只保留有内容的作业条目。 */
const subjectsOf = (payloadJson, runs) => parseProject(payloadJson, runs).map(s => ({ ...s, entries: s.entries.filter(e => e.text || e.hand || e.atts.length) }));
const ASSET_RE = /^asset:([0-9a-f]{64})(\.[a-z0-9]{1,10})?$/i;
const IMAGE_EXT = /^\.(png|jpe?g|gif|webp|bmp)$/i;
function assetImage(a, assetUrl) {
  const m = ASSET_RE.exec(a.path || '');
  return m && assetUrl && (IMAGE_EXT.test(m[2] || '') || /图/.test(a.kind)) ? assetUrl(m[1].toLowerCase()) : null;
}
function attachmentView(a, assetUrl) {
  const src = assetImage(a, assetUrl);
  if (src) return html`<a class="att-img" href="${src}" target="_blank" rel="noopener" title="${a.name}"><img src="${src}" alt="${a.name}" loading="lazy"></a>`;
  return html`<span class="attachment" title="${a.kind}">${icon(/图|image/i.test(a.kind) ? 'image' : 'clip', 'sm')}${a.name}</span>`;
}
function homeworkText(project, subjects) {
  const lines = [`${project.name}${project.date ? '（' + dateLabel(project.date, true) + '）' : ''}`, ''];
  subjects.filter(s => s.entries.length).forEach(s => {
    lines.push(`【${s.name}】`);
    s.entries.forEach((e, i) => lines.push(`${s.entries.length > 1 ? (i + 1) + '. ' : ''}${e.text || '（手写内容）'}`));
    lines.push('');
  });
  return lines.join('\n').trim();
}
function renderSubjects(subjects, assetUrl) {
  if (!subjects.length) return empty('book', '这个项目还没有科目', '在班级大屏或网页上添加科目和作业后，会自动同步。');
  const filled = subjects.filter(s => s.entries.length), blank = subjects.filter(s => !s.entries.length);
  if (!filled.length) return empty('book', '暂无作业内容', '所有科目都还是空的。');
  return html`<div class="subjects">${filled.map(s => html`
    <section class="subject" style="${s.color ? '--subject:' + s.color : ''}">
      <div class="subject-head"><span class="swatch"></span><h3>${s.name}</h3><span class="count">${s.entries.length} 项</span></div>
      <ol class="entries">${s.entries.map((e, i) => html`
        <li class="entry"><span class="num">${i + 1}</span><div>
          ${e.text ? html`<div class="text rich-view">${raw(rtHtml(e.runs.length ? e.runs : [rtRun(e.text)]))}</div>` : html`<div class="text empty-text">${e.atts.length ? '图片作业' : '手写作业，请在班级大屏查看'}</div>`}
          ${e.atts.length || (e.hand && e.text) ? html`<div class="attachments">
            ${e.hand && e.text ? html`<span class="attachment">${icon('pen', 'sm')}含手写</span>` : ''}
            ${e.atts.map(a => attachmentView(a, assetUrl))}
          </div>` : ''}
        </div></li>`)}
      </ol>
    </section>`)}</div>
    ${blank.length ? html`<p class="faint" style="padding:0 22px 20px;font-size:12.5px">未布置：${blank.map(s => s.name).join('、')}</p>` : ''}`;
}

// ============================================================ 富文本（与大屏磁贴的格式一致）
/**
 * 作业富文本以片段数组表示：{ text, bold, italic, underline, fontFamily, fontSize, color, highlight }。
 * 服务端负责与大屏保存的 RTF 互转；这里只在 contenteditable 与片段之间转换。
 * 片段的格式记在 span 的 data-* 上，样式只是它的显示，避免浏览器计算样式带来的误差。
 */
const RT_COLORS = ['#FBBF24', '#F87171', '#60A5FA', '#4ADE80', '#F472B6'];
const RT_HIGHLIGHTS = ['#F7F7F9', '#FBBF24', '#F87171', '#60A5FA', '#4ADE80', '#F472B6'];
const RT_KEYS = ['bold', 'italic', 'underline', 'fontFamily', 'fontSize', 'color', 'highlight'];
const rtRun = (text, f = {}) => ({ text, bold: !!f.bold, italic: !!f.italic, underline: !!f.underline, fontFamily: f.fontFamily || null, fontSize: f.fontSize || null, color: f.color || null, highlight: f.highlight || null });
const rtSame = (a, b) => RT_KEYS.every(k => typeof a[k] === 'boolean' || typeof b[k] === 'boolean' ? !!a[k] === !!b[k] : (a[k] || null) === (b[k] || null));
function rtNormalize(runs) {
  const out = [];
  for (const r of runs || []) {
    if (!r || !r.text) continue;
    const run = rtRun(r.text, r);
    if (out.length && rtSame(out[out.length - 1], run)) out[out.length - 1].text += run.text; else out.push(run);
  }
  return out;
}
const rtPlain = runs => (runs || []).map(r => r.text).join('');
/** 服务端返回的片段（camelCase）转成本地格式。 */
const rtFromApi = runs => rtNormalize((runs || []).map(r => rtRun(pick(r, 'Text') || '', { bold: pick(r, 'Bold'), italic: pick(r, 'Italic'), underline: pick(r, 'Underline'), fontFamily: pick(r, 'FontFamily'), fontSize: pick(r, 'FontSize'), color: pick(r, 'Color'), highlight: pick(r, 'Highlight') })));
function rtStyle(r) {
  const s = [];
  if (r.bold) s.push('font-weight:700');
  if (r.italic) s.push('font-style:italic');
  if (r.underline) s.push('text-decoration:underline');
  if (r.fontFamily) s.push(`font-family:${JSON.stringify(r.fontFamily).replace(/"/g, "'")},sans-serif`);
  if (r.fontSize) s.push(`font-size:${Math.max(8, Math.min(96, r.fontSize * .8)).toFixed(1)}px`);
  if (r.color) s.push(`color:${r.color}`);
  if (r.highlight) s.push(`background:${r.highlight};color:${r.color || '#111'}`);
  return s.join(';');
}
/** 片段转 HTML：只读显示与编辑器共用。 */
function rtHtml(runs, editable = false) {
  const list = rtNormalize(runs);
  let out = list.map(r => {
    const attrs = RT_KEYS.filter(k => r[k]).map(k => ` data-${k.toLowerCase()}="${esc(r[k] === true ? '1' : r[k])}"`).join('');
    const body = esc(r.text).replace(/\n/g, '<br>');
    return `<span${attrs} style="${esc(rtStyle(r))}">${body}</span>`;
  }).join('');
  // 末尾换行需要多一个 <br> 才能在可编辑区域显示出空行。
  if (editable && rtPlain(list).endsWith('\n')) out += '<br data-tail="1">';
  return out;
}
/** 从 DOM 读出片段：格式取自最近的带 data-* 的 span，<br> 和块级元素换行。 */
function rtExtract(root) {
  const runs = [];
  const fmtOf = el => {
    const f = {};
    for (let n = el; n && n !== root && n.nodeType === 1; n = n.parentNode)
      for (const k of RT_KEYS) {
        const v = n.dataset?.[k.toLowerCase()];
        if (v != null && f[k] === undefined) f[k] = k === 'fontSize' ? +v : ['bold', 'italic', 'underline'].includes(k) ? true : v;
      }
    return f;
  };
  const push = (text, el) => { if (text) runs.push(rtRun(text, fmtOf(el))); };
  const walk = node => {
    for (const child of node.childNodes) {
      if (child.nodeType === 3) push(child.nodeValue.replace(/​/g, ''), child.parentNode);
      else if (child.nodeType === 1) {
        if (child.tagName === 'BR') { if (!child.dataset.tail) push('\n', child.parentNode); continue; }
        const block = /^(DIV|P)$/.test(child.tagName);
        if (block && rtPlain(runs).length && !rtPlain(runs).endsWith('\n')) push('\n', child.parentNode);
        walk(child);
      }
    }
  };
  walk(root);
  return rtNormalize(runs);
}
/** 选区在纯文本中的位置：截取到该点的内容按同样规则读出长度。 */
function rtOffset(root, node, offset) {
  const range = document.createRange();
  range.setStart(root, 0); range.setEnd(node, offset);
  const box = document.createElement('div'); box.appendChild(range.cloneContents());
  return rtPlain(rtExtract(box)).length;
}
function rtSelection(root) {
  const sel = getSelection();
  if (!sel.rangeCount || !root.contains(sel.anchorNode)) return null;
  const r = sel.getRangeAt(0);
  return { start: rtOffset(root, r.startContainer, r.startOffset), end: rtOffset(root, r.endContainer, r.endOffset) };
}
function rtSetSelection(root, start, end) {
  const find = target => {
    let left = target;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT);
    let last = null;
    while (walker.nextNode()) {
      const n = walker.currentNode;
      if (n.nodeType === 3) { if (left <= n.nodeValue.length) return [n, left]; left -= n.nodeValue.length; last = n; }
      else if (n.tagName === 'BR' && !n.dataset.tail) {
        if (left === 0) return [n.parentNode, [...n.parentNode.childNodes].indexOf(n)];
        left -= 1;
        if (left === 0) return [n.parentNode, [...n.parentNode.childNodes].indexOf(n) + 1];
      }
    }
    return last ? [last, last.nodeValue.length] : [root, root.childNodes.length];
  };
  const [sn, so] = find(start), [en, eo] = find(end);
  const range = document.createRange(); range.setStart(sn, so); range.setEnd(en, eo);
  const sel = getSelection(); sel.removeAllRanges(); sel.addRange(range);
}
/** 把 [start, end) 的片段改成 patch(run) 返回的格式。 */
function rtApply(runs, start, end, patch) {
  const out = []; let pos = 0;
  for (const r of rtNormalize(runs)) {
    const a = pos, b = pos + r.text.length; pos = b;
    if (b <= start || a >= end) { out.push(r); continue; }
    const s = Math.max(start, a) - a, e = Math.min(end, b) - a;
    if (s > 0) out.push(rtRun(r.text.slice(0, s), r));
    out.push(rtRun(r.text.slice(s, e), { ...r, ...patch(r) }));
    if (e < r.text.length) out.push(rtRun(r.text.slice(e), r));
  }
  return rtNormalize(out);
}
const rtSlice = (runs, start, end) => { let pos = 0; return rtNormalize(runs).filter(r => { const a = pos; pos += r.text.length; return pos > start && a < end; }); };

/**
 * 富文本工具栏：字体、字号、粗体、斜体、下划线、文字颜色与高光，色板与大屏一致。
 * 作用于最近一次获得焦点的编辑框中的选区；onChange(editor) 在格式改变后调用。
 */
function richToolbar(fonts, defaults) {
  const fontOptions = [...new Set([defaults.fontFamily, ...fonts].filter(Boolean))];
  return html`<div class="rt-toolbar" role="toolbar" aria-label="文字格式">
    <select class="input sm" data-rt="font" title="字体">${fontOptions.map(f => html`<option value="${f}">${f}</option>`)}</select>
    <input class="input sm rt-size" type="number" min="8" max="144" step="1" value="${defaults.fontSize}" data-rt="size" title="字号">
    <button type="button" class="btn ghost sm icon-only rt-b" data-rt="bold" title="加粗"><b>B</b></button>
    <button type="button" class="btn ghost sm icon-only" data-rt="italic" title="斜体"><i style="font-family:serif">I</i></button>
    <button type="button" class="btn ghost sm icon-only" data-rt="underline" title="下划线"><u>U</u></button>
    <span class="rt-sep"></span>
    <span class="rt-swatches" title="文字颜色"><button type="button" class="rt-swatch rt-default" data-rt="color" data-v="" title="默认文字色">A</button>${RT_COLORS.map(c => html`<button type="button" class="rt-swatch" data-rt="color" data-v="${c}" style="background:${c}" title="${c}"></button>`)}</span>
    <span class="rt-sep"></span>
    <span class="rt-swatches" title="高光"><button type="button" class="rt-swatch rt-clear" data-rt="highlight" data-v="" title="取消高光"></button>${RT_HIGHLIGHTS.map(c => html`<button type="button" class="rt-swatch rt-hl" data-rt="highlight" data-v="${c}" style="background:${c}" title="高光 ${c}"></button>`)}</span>
  </div>`;
}
/**
 * 把工具栏和若干 .rich 编辑框接起来；getRuns/setRuns 读写编辑框对应的数据。
 * 全部用委托监听挂在 container 上（工具栏和编辑框随时会重绘），再次绑定或 unbindRichEditors 时撤掉旧的监听。
 */
function bindRichEditors(container, { getRuns, setRuns, defaults }) {
  unbindRichEditors(container);
  const controller = new AbortController(), signal = controller.signal;
  container._rtAbort = controller;
  let active = null, saved = null;
  const bar = () => $('.rt-toolbar', container);
  const refreshBar = () => {
    const b = bar();
    if (!b || !active || !saved || !container.contains(active)) return;
    const runs = getRuns(active);
    const sel = saved.end > saved.start ? rtSlice(runs, saved.start, saved.end) : rtSlice(runs, Math.max(0, saved.start - 1), saved.start);
    const first = sel[0] || {};
    $('[data-rt=font]', b).value = first.fontFamily || defaults.fontFamily;
    $('[data-rt=size]', b).value = first.fontSize || defaults.fontSize;
    for (const k of ['bold', 'italic', 'underline']) $(`[data-rt=${k}]`, b).classList.toggle('on', sel.length > 0 && sel.every(r => r[k]));
  };
  const apply = patch => {
    if (!active || !container.contains(active) || !saved || saved.end <= saved.start) return toast('请先选中要设置格式的文字', 'error');
    const runs = rtApply(getRuns(active), saved.start, saved.end, patch);
    setRuns(active, runs);
    active.innerHTML = rtHtml(runs, true);
    active.focus(); rtSetSelection(active, saved.start, saved.end);
    refreshBar();
  };
  document.addEventListener('selectionchange', () => {
    const el = getSelection().anchorNode?.parentElement?.closest?.('.rich') || document.activeElement?.closest?.('.rich');
    if (!el || !container.contains(el) || el.contentEditable !== 'true') return;
    active = el; saved = rtSelection(el) || saved; refreshBar();
  }, { signal });
  container.addEventListener('input', e => {
    const el = e.target.closest?.('.rich'); if (!el) return;
    setRuns(el, rtExtract(el));
    // 删光内容后浏览器会留下空的格式元素，清掉以免下一次输入继承旧格式。
    if (!el.textContent) el.innerHTML = '';
  }, { signal });
  container.addEventListener('paste', e => {
    const el = e.target.closest?.('.rich'); if (!el) return;
    e.preventDefault();
    document.execCommand('insertText', false, (e.clipboardData?.getData('text/plain') || '').replace(/\r\n?/g, '\n'));
  }, { signal });
  container.addEventListener('keydown', e => {
    const el = e.target.closest?.('.rich'); if (!el || e.key !== 'Enter') return;
    e.preventDefault();
    document.execCommand('insertLineBreak');
  }, { signal });
  // 按下工具栏按钮时保留编辑框的选区。
  container.addEventListener('mousedown', e => { if (e.target.closest('.rt-toolbar button')) e.preventDefault(); }, { signal });
  container.addEventListener('click', e => {
    const b = e.target.closest('.rt-toolbar button[data-rt]'); if (!b) return;
    const k = b.dataset.rt;
    if (k === 'bold' || k === 'italic' || k === 'underline') {
      const sel = active && saved ? rtSlice(getRuns(active), saved.start, saved.end) : [];
      const on = !(sel.length && sel.every(r => r[k]));
      apply(() => ({ [k]: on }));
    } else if (k === 'color') apply(() => ({ color: b.dataset.v || null }));
    else if (k === 'highlight') apply(() => ({ highlight: b.dataset.v || null }));
  }, { signal });
  container.addEventListener('change', e => {
    if (!e.target.closest('.rt-toolbar')) return;
    const k = e.target.dataset.rt;
    if (k === 'font') apply(() => ({ fontFamily: e.target.value === defaults.fontFamily ? null : e.target.value }));
    if (k === 'size') { const v = Math.max(8, Math.min(144, Math.round(+e.target.value || defaults.fontSize))); apply(() => ({ fontSize: v === defaults.fontSize ? null : v })); }
  }, { signal });
  onDispose(() => controller.abort());
}
function unbindRichEditors(container) { container._rtAbort?.abort(); container._rtAbort = null; }
/** 班级正文默认字体、字号与可选字体：取自班级设置与大屏上报的字体。 */
async function richDefaults(classId) {
  const [layout, fonts] = await Promise.all([
    api(`/api/classes/${classId}/layout`).catch(() => null),
    api(`/api/classes/${classId}/fonts`).catch(() => [])]);
  let doc = {}; try { doc = JSON.parse(layout?.payloadJson || '{}'); } catch { }
  return { fontFamily: doc.TileBodyFontFamily || 'HarmonyOS Sans SC', fontSize: Math.round(+doc.TileBodyFontSize || 20), fonts: fonts || [] };
}

// ============================================================ 作业浏览器（公开页和控制台共用）
/**
 * @param src.list()            -> Promise<Project[]>
 * @param src.detail(id)        -> Promise<ProjectDetail>
 * @param src.link(id)          -> hash 路径
 * @param src.manage            是否显示管理操作
 * @param src.classId           管理操作所需
 * @param src.ws                实时通知路径
 */
function mountBrowser(root, src, selectedId) {
  let projects = [], current = null, filter = '', showDeleted = false, editing = false, savedAt = 0;
  const byId = id => projects.find(p => p.id === id);

  root.innerHTML = show(html`<div class="browser">
    <aside class="card browser-side">
      <div class="filters">
        <label class="input-icon">${icon('search', 'sm')}<input class="input" id="pf" placeholder="搜索项目或日期" autocomplete="off"></label>
        <div class="row">
          <span class="badge" id="live" title="实时同步状态"><span class="dot"></span><span>连接中</span></span>
          <span class="spacer"></span>
          ${src.manage ? html`<label class="check" style="font-size:12.5px;padding:0"><input type="checkbox" id="pd">显示已删除</label>` : ''}
        </div>
        ${src.manage ? html`<button class="btn primary sm" data-act="p-new">${icon('plus')}新建项目</button>` : ''}
      </div>
      <div class="project-list" id="pl">${loading()}</div>
    </aside>
    <section class="card" id="doc">${loading()}</section>
  </div>`);

  const setLive = on => {
    const b = $('#live', root); if (!b) return;
    b.className = 'badge ' + (on ? 'ok' : '');
    b.innerHTML = show(html`<span class="dot ${on ? 'live' : ''}"></span><span>${on ? '实时同步' : '离线'}</span>`);
  };

  function renderList() {
    const q = filter.trim().toLowerCase();
    const items = projects.filter(p => (showDeleted || !p.deleted) && (!q || p.name.toLowerCase().includes(q) || (p.date || '').includes(q) || dateLabel(p.date).includes(q)));
    const box = $('#pl', root);
    if (!items.length) { box.innerHTML = show(empty('book', projects.length ? '没有匹配的项目' : '暂无项目', projects.length ? '换个关键词试试。' : src.manage ? '点击“新建项目”布置作业，或等待班级大屏同步。' : '设备同步后项目会出现在这里。')); return; }
    const groups = new Map();
    [...items].sort((a, b) => (b.date || '').localeCompare(a.date || '') || a.name.localeCompare(b.name, 'zh')).forEach(p => {
      const k = p.date || ''; if (!groups.has(k)) groups.set(k, []); groups.get(k).push(p);
    });
    box.innerHTML = show([...groups].map(([date, ps]) => html`
      <div class="date-group">${icon('calendar', 'sm')}${dateLabel(date)}</div>
      ${ps.map(p => html`<a class="project-item ${current?.id === p.id ? 'active' : ''} ${p.deleted ? 'deleted' : ''}" href="#/${src.link(p.id)}">
        <span class="name">${p.name}${p.deleted ? html`<span class="badge danger" style="height:18px;font-size:11px;text-decoration:none">已删除</span>` : ''}</span>
        <span class="meta">更新于 ${relTime(p.updatedAt)}${src.manage ? ' · 修订 ' + p.revision : ''}</span>
      </a>`)}`));
  }

  function resetDocHandlers(doc) { doc.oninput = doc.onchange = doc.onclick = null; unbindRichEditors(doc); editing = false; }

  async function renderDoc(id) {
    const doc = $('#doc', root);
    resetDocHandlers(doc);
    const p = byId(id);
    if (!p) { doc.innerHTML = show(empty('book', '选择一个项目', src.manage ? '从左侧选择要查看的项目，或新建一个项目布置作业。' : '从左侧列表选择要查看的作业项目。')); return; }
    current = p; renderList();
    let detail;
    try { detail = await src.detail(id); } catch (err) { doc.innerHTML = show(empty('alert', '加载失败', err.message)); return; }
    if (current?.id !== id || editing) return;
    const subjects = subjectsOf(detail.payloadJson, detail.runs);
    const total = subjects.reduce((n, s) => n + s.entries.length, 0);
    const text = homeworkText({ name: detail.name, date: detail.date }, subjects);
    doc.innerHTML = show(html`
      <div class="doc-head">
        <div class="grow">
          <h2>${detail.name}</h2>
          <div class="meta">
            <span>${icon('calendar', 'sm')}${dateLabel(detail.date, true)}</span>
            <span>${icon('book', 'sm')}${total} 项作业</span>
            <span title="${fullTime(detail.updatedAt)}">${icon('refresh', 'sm')}更新于 ${relTime(detail.updatedAt)}${detail.updatedBy ? ' · ' + detail.updatedBy : ''}</span>
            ${detail.deleted ? html`<span class="badge danger">已删除</span>` : ''}
          </div>
        </div>
        <div class="row wrap no-print">
          ${src.manage && !detail.deleted ? html`<button class="btn sm primary" data-act="p-content">${icon('edit')}编辑作业</button>` : ''}
          <button class="btn sm" data-act="copy" data-text="${text}" data-label="作业文本已复制">${icon('copy')}复制文本</button>
          <button class="btn sm" data-act="print">${icon('print')}打印</button>
          ${src.manage ? html`
            <button class="btn sm" data-act="p-history">${icon('history')}历史</button>
            <button class="btn sm" data-act="p-edit">${icon('calendar')}名称与日期</button>
            ${detail.deleted
              ? html`<button class="btn sm primary" data-act="p-restore">${icon('restore')}恢复</button>`
              : html`<button class="btn sm danger icon-only" data-act="p-delete" title="删除项目">${icon('trash')}</button>`}` : ''}
        </div>
      </div>
      ${renderSubjects(subjects, src.asset)}`);
  }

  /** 网页编辑作业：科目名、颜色、作业文字和图片；没分配的科目只读。保存后服务端合并，磁贴位置与笔迹不变。 */
  async function openEditor(id) {
    const doc = $('#doc', root);
    const [detail, allowed, defaults] = await Promise.all([src.detail(id), editableSubjects(src.classId), richDefaults(src.classId)]);
    resetDocHandlers(doc);
    editing = true;
    const can = name => !allowed || allowed.has(name.trim().toLowerCase());
    const draft = parseProject(detail.payloadJson, detail.runs).map(s => ({
      index: s.index, name: s.name, color: (s.color || '#818cf8').toLowerCase(), origColor: (s.color || '').toLowerCase(), locked: !can(s.name), removed: false,
      entries: s.entries.map(e => ({ index: e.index, runs: e.runs, hand: e.hand, atts: e.atts.map(a => ({ index: a.index, name: a.name, path: a.path, kind: a.kind })) }))
    }));
    let uploading = 0;
    const subjectAt = el => draft[+el.closest('[data-si]').dataset.si];
    const entryAt = el => subjectAt(el).entries[+el.closest('[data-ei]').dataset.ei];

    const render = () => {
      doc.innerHTML = show(html`
        <div class="doc-head">
          <div class="grow"><h2>编辑作业</h2>
            <div class="meta"><span>${icon('book', 'sm')}${detail.name}</span><span>${icon('calendar', 'sm')}${dateLabel(detail.date, true)}</span>
              ${allowed ? html`<span class="badge warn">${icon('lock', 'sm')}你负责：${allowed.names.join('、')}</span>` : ''}</div>
          </div>
          <div class="row wrap"><button class="btn sm" data-ed="cancel">取消</button><button class="btn sm primary" data-ed="save">${icon('send')}保存并同步到大屏</button></div>
        </div>
        <div class="rt-dock">${richToolbar(defaults.fonts, defaults)}</div>
        <div class="editor" style="--rt-font:${JSON.stringify(defaults.fontFamily).replace(/"/g, "'")};--rt-size:${(defaults.fontSize * .8).toFixed(1)}px">
          ${draft.map((s, si) => s.removed ? '' : html`
          <section class="edit-subject ${s.locked ? 'locked' : ''}" style="--subject:${s.color}" data-si="${si}">
            <div class="edit-subject-head">
              <input type="color" value="${s.color.slice(0, 7)}" data-f="color" ${s.locked ? 'disabled' : ''} title="科目颜色" aria-label="科目颜色">
              <input class="input" value="${s.name}" data-f="name" maxlength="40" ${s.locked ? 'disabled' : ''} placeholder="科目名称" aria-label="科目名称">
              ${s.locked ? html`<span class="badge" title="由其他老师负责">${icon('lock', 'sm')}只读</span>` : html`<button type="button" class="btn ghost sm icon-only danger" data-ed="del-subject" title="删除科目">${icon('trash')}</button>`}
            </div>
            <div class="edit-entries">
              ${s.entries.map((e, ei) => html`<div class="edit-entry" data-ei="${ei}">
                <span class="num">${ei + 1}</span>
                <div class="grow" style="display:grid;gap:6px">
                  <div class="input rich" contenteditable="${s.locked ? 'false' : 'true'}" role="textbox" aria-multiline="true" data-placeholder="作业内容" ${s.locked ? 'aria-disabled="true"' : ''}>${raw(rtHtml(e.runs, true))}</div>
                  ${e.hand ? html`<small class="faint">${icon('pen', 'sm')}含手写笔迹，网页上看不到；修改文字不会影响笔迹。</small>` : ''}
                  ${e.atts.length ? html`<div class="attachments">${e.atts.map((a, ai) => {
                    const img = assetImage(a, src.asset);
                    return html`<span class="attachment ${img ? 'with-thumb' : ''}">${img ? html`<img src="${img}" alt="">` : icon('clip', 'sm')}<span>${a.name}</span>${s.locked ? '' : html`<button type="button" class="att-x" data-ed="del-att" data-ai="${ai}" title="移除">${icon('x', 'sm')}</button>`}</span>`;
                  })}</div>` : ''}
                </div>
                ${s.locked ? '' : html`<div class="edit-entry-actions">
                  <button type="button" class="btn ghost sm icon-only" data-ed="add-image" title="添加图片">${icon('image')}</button>
                  <button type="button" class="btn ghost sm icon-only danger" data-ed="del-entry" title="删除这一条">${icon('x')}</button></div>`}
              </div>`)}
              ${s.locked ? (s.entries.length ? '' : html`<p class="faint" style="font-size:12.5px;margin:0">暂无作业</p>`) : html`<button type="button" class="btn ghost sm" data-ed="add-entry">${icon('plus')}添加一条</button>`}
            </div>
          </section>`)}
          <button type="button" class="btn add-subject" data-ed="add-subject">${icon('plus')}添加科目</button>
          <p class="faint" style="font-size:12.5px;grid-column:1/-1">保存后连接的班级大屏会实时更新。修改过的文字会以纯文本显示，没改动的作业保留原来的字体和颜色。</p>
        </div>`);
    };

    const pickImages = async entry => {
      const input = document.createElement('input');
      input.type = 'file'; input.accept = 'image/*'; input.multiple = true;
      input.onchange = async () => {
        for (const file of input.files) {
          if (file.size > 50 * 1024 * 1024) { toast(`“${file.name}”超过 50 MB`, 'error'); continue; }
          uploading++;
          try {
            const r = await fetch(`/api/classes/${src.classId}/assets?name=${encodeURIComponent(file.name)}`, { method: 'POST', headers: { authorization: 'Bearer ' + state.token }, body: file });
            if (!r.ok) throw new Error((await r.text().catch(() => '')).replace(/^"|"$/g, '') || '上传失败');
            const j = await r.json();
            entry.atts.push({ index: null, name: file.name, path: j.reference, kind: '图片' });
          } catch (err) { toast(`图片上传失败：${err.message}`, 'error'); }
          finally { uploading--; }
        }
        render();
      };
      input.click();
    };

    doc.oninput = e => {
      const f = e.target.dataset.f; if (!f) return;
      if (f === 'name') subjectAt(e.target).name = e.target.value;
      if (f === 'color') { const s = subjectAt(e.target); s.color = e.target.value.toLowerCase(); e.target.closest('.edit-subject').style.setProperty('--subject', s.color); }
    };
    doc.onclick = async e => {
      const b = e.target.closest('[data-ed]'); if (!b) return;
      const act = b.dataset.ed;
      if (act === 'cancel') { await renderDoc(id); return; }
      if (act === 'add-subject') { draft.push({ index: null, name: '', color: '#818cf8', origColor: '', locked: false, removed: false, entries: [{ index: null, runs: [], atts: [] }] }); render(); $$('.edit-subject:last-of-type input[data-f=name]', doc).pop()?.focus(); return; }
      if (act === 'del-subject') { const s = subjectAt(b); if (s.entries.some(x => rtPlain(x.runs) || x.atts.length || x.hand) && !await confirmDialog({ title: '删除科目', message: `删除“${s.name || '新科目'}”及其全部作业？保存后大屏上的这个磁贴也会删除。`, confirm: '删除' })) return; s.removed = true; render(); return; }
      if (act === 'add-entry') { const s = subjectAt(b); s.entries.push({ index: null, runs: [], atts: [] }); render(); $$(`[data-si="${draft.indexOf(s)}"] .rich`, doc).pop()?.focus(); return; }
      if (act === 'del-entry') { const s = subjectAt(b); s.entries.splice(+b.closest('[data-ei]').dataset.ei, 1); render(); return; }
      if (act === 'del-att') { entryAt(b).atts.splice(+b.dataset.ai, 1); render(); return; }
      if (act === 'add-image') { await pickImages(entryAt(b)); return; }
      if (act === 'save') {
        if (uploading) return toast('图片还在上传，请稍候', 'error');
        const live = draft.filter(x => !x.removed);
        if (live.some(x => !x.name.trim())) return toast('科目名称不能为空', 'error');
        const body = {
          expectedRevision: detail.revision,
          subjects: live.map(x => ({
            index: x.index, name: x.name.trim(), accentHex: x.color !== x.origColor && !(x.index != null && !x.origColor && x.color === '#818cf8') ? x.color : null,
            entries: x.entries.filter(en => en.index != null || rtPlain(en.runs).trim() || en.atts.length).map(en => ({
              index: en.index, content: rtPlain(en.runs), runs: en.runs, attachments: en.atts.map(a => a.index != null ? { index: a.index } : { asset: a.path, name: a.name })
            }))
          }))
        };
        b.disabled = true;
        try {
          await api(`/api/classes/${src.classId}/projects/${id}/content`, { method: 'PUT', body });
          toast('作业已保存，大屏正在同步');
          savedAt = Date.now();
          editing = false;
          await load();
        } catch (err) {
          b.disabled = false;
          if (err.status === 409 && await confirmDialog({ title: '作业刚被修改', message: '班级大屏或其他老师刚刚修改了这个项目。重新载入最新内容后再编辑？（当前未保存的修改会丢失，可以先复制下来）', confirm: '重新载入', danger: false })) await openEditor(id);
          else fail(err);
        }
      }
    };
    // 编辑框与片段数据一一对应：同一条作业重绘后仍写回同一个 entry 对象。
    bindRichEditors(doc, { getRuns: el => entryAt(el).runs, setRuns: (el, runs) => { entryAt(el).runs = runs; }, defaults });
    render();
    $('.rich[contenteditable=true]', doc)?.focus();
  }

  async function load(keepSelection = true) {
    projects = await src.list();
    renderList();
    let id = keepSelection && current ? current.id : selectedId;
    if (!byId(id) || (byId(id).deleted && !src.manage)) {
      // 默认打开今天的项目；没有就打开最近一个过去的日期。
      const live = projects.filter(p => !p.deleted);
      const t = todayStr();
      const pickP = live.find(p => p.date === t)
        || [...live].filter(p => p.date && p.date < t).sort((a, b) => b.date.localeCompare(a.date))[0]
        || [...live].sort((a, b) => parseTime(b.updatedAt) - parseTime(a.updatedAt))[0];
      id = pickP?.id;
      if (id && !selectedId) history.replaceState(null, '', '#/' + src.link(id));
    }
    if (!editing) await renderDoc(id);
  }

  // 切换项目只刷新右侧内容，不重建整个页面和实时连接。
  $('#pl', root).addEventListener('click', async e => {
    const a = e.target.closest('a.project-item'); if (!a || e.ctrlKey || e.metaKey || e.shiftKey) return;
    e.preventDefault();
    if (editing && !await confirmDialog({ title: '放弃编辑', message: '当前的修改还没有保存，确定离开吗？', confirm: '离开' })) return;
    editing = false;
    history.pushState(null, '', a.getAttribute('href'));
    renderDoc(a.getAttribute('href').split('/').pop());
    if (innerWidth <= 860) $('#doc', root).scrollIntoView({ behavior: 'smooth' });
  });
  $('#pf', root).addEventListener('input', e => { filter = e.target.value; renderList(); });
  $('#pd', root)?.addEventListener('change', e => { showDeleted = e.target.checked; renderList(); });

  if (src.manage) Object.assign(viewActions, {
    'p-content': () => openEditor(current.id),
    'p-new': async () => {
      const t = todayStr(), [, m, d] = t.split('-').map(Number);
      const live = projects.filter(p => !p.deleted).sort((a, b) => (b.date || '').localeCompare(a.date || ''));
      const created = await modal({
        title: '新建作业项目', submit: '创建并编辑',
        body: html`<label class="field"><span>项目名称</span><input class="input" name="name" value="${m}月${d}日" maxlength="80"></label>
          <label class="field"><span>作业日期</span><input class="input" type="date" name="date" value="${t}"></label>
          <label class="field"><span>科目布局</span><select class="input" name="tpl">
            <option value="latest">沿用最近的项目</option>${live.slice(0, 30).map(p => html`<option value="${p.id}">沿用“${p.name}”</option>`)}<option value="none">空白项目</option></select>
            <small>沿用时保留科目、颜色和磁贴位置，只清空作业内容和笔迹。</small></label>`,
        onSubmit: f => {
          const name = f.elements.name.value.trim(); if (!name) throw new Error('请输入项目名称');
          const tpl = f.tpl.value;
          return api(`/api/classes/${src.classId}/projects`, { method: 'POST', body: { name, date: f.date.value || null, templateProjectId: /^[0-9a-f-]{36}$/.test(tpl) ? tpl : null, useLatestTemplate: tpl === 'latest' } });
        }
      });
      if (!created) return;
      toast('项目已创建');
      projects = await src.list(); current = byId(created.id); renderList();
      history.pushState(null, '', '#/' + src.link(created.id));
      await openEditor(created.id);
    },
    'p-history': () => openHistory(src.classId, current, () => load(), src.asset),
    'p-edit': async () => {
      const r = await modal({
        title: '名称与日期', submit: '保存',
        body: html`<label class="field"><span>项目名称</span><input class="input" name="name" value="${current.name}" maxlength="80" required></label>
          <label class="field"><span>作业日期</span><input class="input" type="date" name="date" value="${current.date || ''}"><small>清空则取消日期；设备下次同步时会更新。</small></label>`,
        onSubmit: f => {
          const name = f.elements.name.value.trim(); if (!name) throw new Error('项目名称不能为空');
          const date = f.date.value;
          return api(`/api/classes/${src.classId}/projects/${current.id}`, { method: 'PATCH', body: date ? { name, date } : { name, clearDate: true } });
        }
      });
      if (r) { toast('项目信息已更新'); await load(); }
    },
    'p-delete': async () => {
      if (!await confirmDialog({ title: '删除项目', message: `确定删除“${current.name}”吗？已配对设备同步后也会删除该项目。可以在“显示已删除”中恢复。`, confirm: '删除' })) return;
      await api(`/api/classes/${src.classId}/projects/${current.id}`, { method: 'DELETE' });
      toast('项目已删除'); await load();
    },
    'p-restore': async () => {
      await api(`/api/classes/${src.classId}/projects/${current.id}/restore`, { method: 'POST', body: {} });
      toast('项目已恢复'); await load();
    }
  });

  let refreshTimer;
  if (src.ws) onDispose(liveSocket(src.ws, ev => {
    src.onEvent?.(ev);
    if (ev.type && !['updated', 'deleted'].includes(ev.type)) return;
    clearTimeout(refreshTimer);
    refreshTimer = setTimeout(async () => {
      const wasCurrent = current && ev.projectId === current.id;
      await load().catch(() => { });
      if (wasCurrent && Date.now() - savedAt > 4000) toast(editing ? '这个项目刚被大屏修改，保存时会提示冲突' : '当前项目已同步最新修改', editing ? 'error' : 'ok');
    }, 400);
  }, setLive));
  else setLive(false);
  onDispose(() => clearTimeout(refreshTimer));

  return load(false);
}

// ---------- 历史版本抽屉
async function openHistory(classId, project, onRestored, assetUrl) {
  $('.drawer')?.remove(); $('.scrim')?.remove();
  const scrim = document.createElement('div'); scrim.className = 'scrim';
  const drawer = document.createElement('aside'); drawer.className = 'drawer';
  document.body.append(scrim, drawer);
  const close = () => { drawer.remove(); scrim.remove(); };
  scrim.onclick = close;
  drawer.innerHTML = show(html`<div class="drawer-head">${icon('history')}<h3 class="grow">历史版本 · ${project.name}</h3><button class="btn ghost sm icon-only" id="hx">${icon('x')}</button></div><div class="drawer-body">${loading()}</div>`);
  $('#hx', drawer).onclick = close;
  let items;
  try { items = await api(`/api/classes/${classId}/projects/${project.id}/history`); }
  catch (err) { $('.drawer-body', drawer).innerHTML = show(empty('alert', '加载失败', err.message)); return; }
  const body = $('.drawer-body', drawer);
  if (!items.length) { body.innerHTML = show(empty('history', '暂无历史记录', '升级服务端之后的每次修改都会保存一份快照，最多保留 30 个版本。')); return; }
  body.innerHTML = show(html`${items.map((h, i) => html`
    <div class="revision" data-rev="${h.revision}">
      <div class="row"><b>修订 ${h.revision}</b>${i === 0 ? html`<span class="badge ok">当前</span>` : ''}${h.deleted ? html`<span class="badge danger">删除</span>` : ''}<span class="spacer"></span><span class="faint" style="font-size:12px" title="${fullTime(h.createdAt)}">${relTime(h.createdAt)}</span></div>
      <div class="faint" style="font-size:12.5px">${h.author || '未知来源'} · ${h.name} · ${(h.size / 1024).toFixed(1)} KB</div>
    </div>`)}
    <div id="hp"></div>`);
  $$('.revision', body).forEach(el => el.onclick = async () => {
    $$('.revision', body).forEach(x => x.classList.toggle('active', x === el));
    const rev = Number(el.dataset.rev), isCurrent = rev === items[0].revision;
    const hp = $('#hp', body); hp.innerHTML = show(html`<div style="padding:16px 20px">${loading()}</div>`);
    try {
      const d = await api(`/api/classes/${classId}/projects/${project.id}/history/${rev}`);
      hp.innerHTML = show(html`<div style="padding:16px 20px 6px" class="row"><b class="grow">修订 ${rev} 预览</b>${isCurrent ? '' : html`<button class="btn sm primary" id="hr">${icon('restore')}恢复到此版本</button>`}</div>${renderSubjects(subjectsOf(d.payloadJson), assetUrl)}`);
      hp.scrollIntoView({ behavior: 'smooth', block: 'start' });
      const btn = $('#hr', hp);
      if (btn) btn.onclick = async () => {
        if (!await confirmDialog({ title: '恢复历史版本', message: `将项目内容恢复为修订 ${rev}，并生成新的修订号。设备下次同步时会拉取该版本；设备上尚未上传的修改会提示冲突。`, confirm: '恢复', danger: false })) return;
        try {
          await api(`/api/classes/${classId}/projects/${project.id}/restore`, { method: 'POST', body: { revision: rev } });
          toast('已恢复到修订 ' + rev); close(); onRestored?.();
        } catch (err) { fail(err); }
      };
    } catch (err) { hp.innerHTML = show(empty('alert', '加载失败', err.message)); }
  });
}

// ============================================================ 公开页面
function recentCodes() { return store.get('recentCodes', []); }
function rememberCode(code, name) { store.set('recentCodes', [{ code, name }, ...recentCodes().filter(x => x.code !== code)].slice(0, 5)); }

async function viewLanding(query) {
  const recent = recentCodes();
  $('#app').innerHTML = show(html`<div class="landing">
    <div class="landing-top">
      <span></span>
      <div class="row">
        <button class="btn ghost icon-only" data-act="toggle-theme" title="切换深浅色">${icon('moon')}</button>
        ${state.token ? html`<a class="btn" href="#/overview">${icon('home')}进入控制台</a>` : html`<a class="btn" href="#/login">${icon('lock')}教师登录</a>`}
      </div>
    </div>
    <div class="landing-hero"><div class="hero-card">
      ${logo()}
      <h1>今天的作业，一目了然</h1>
      <p>输入老师提供的班级短码，查看同步自班级大屏的全部作业。</p>
      <form class="code-form" id="cf">
        <input class="input" id="code" placeholder="班级短码" maxlength="16" autocomplete="off" autocapitalize="off" spellcheck="false" aria-label="班级短码">
        <button class="btn primary lg" type="submit">查看作业${icon('arrow-right')}</button>
      </form>
      <div class="form-error" id="ce" style="margin-top:10px"></div>
      ${recent.length ? html`<div class="recent-codes">${recent.map(r => html`<a href="#/c/${r.code}">${icon('history', 'sm')}${r.name || r.code}</a>`)}</div>` : ''}
    </div></div>
    <div class="landing-foot">Pancake 集控 · 公开短码仅提供只读访问</div>
  </div>`);
  $('#cf').onsubmit = async e => {
    e.preventDefault();
    const code = $('#code').value.trim().toLowerCase();
    if (!code) { $('#ce').textContent = '请输入班级短码'; return; }
    navigate('c/' + encodeURIComponent(code));
  };
  $('#code').focus();
}

async function viewLogin(query) {
  if (await ensureMe()) return navigate(query.get('next') || 'overview');
  $('#app').innerHTML = show(html`<div class="landing">
    <div class="landing-top"><a class="btn ghost" href="#/">${icon('left')}返回</a><button class="btn ghost icon-only" data-act="toggle-theme">${icon('moon')}</button></div>
    <div class="landing-hero"><form class="card auth-card stack" id="lf" novalidate>
      <div>${logo()}<h2>登录集控控制台</h2><p class="muted">老师和管理员使用账号登录</p></div>
      ${query.get('expired') ? html`<div class="banner warn" style="margin:0">${icon('alert')}<span>登录已过期，请重新登录。</span></div>` : ''}
      <label class="field"><span>账号</span><input class="input" name="user" autocomplete="username" required></label>
      <label class="field"><span>密码</span><input class="input" name="pass" type="password" autocomplete="current-password" required></label>
      <div class="form-error" id="le"></div>
      <button class="btn primary lg" type="submit">登录</button>
    </form></div>
    <div class="landing-foot">忘记密码请联系管理员重置</div>
  </div>`);
  const f = $('#lf');
  f.user.focus();
  f.onsubmit = async e => {
    e.preventDefault(); $('#le').textContent = '';
    const btn = $('button[type=submit]', f); btn.disabled = true; btn.textContent = '正在登录…';
    try {
      const r = await api('/api/auth/login', { method: 'POST', auth: false, body: { userName: f.user.value.trim(), password: f.pass.value } });
      state.token = r.accessToken; store.set('token', r.accessToken); state.me = null;
      await ensureMe();
      navigate(query.get('next') || 'overview');
    } catch (err) {
      $('#le').textContent = err.status === 401 ? '账号或密码不正确，或账号已停用' : err.message;
      btn.disabled = false; btn.textContent = '登录';
    }
  };
}

async function viewPublic(code, projectId) {
  code = code.toLowerCase();
  const base = `/api/public/classes/${encodeURIComponent(code)}`;
  let first;
  try { first = await api(base + '/projects', { auth: false }); }
  catch (err) {
    $('#app').innerHTML = show(html`<div class="landing"><div class="landing-top"><a class="btn ghost" href="#/">${icon('left')}返回</a><span></span></div>
      <div class="landing-hero">${empty('alert', err.status === 404 ? '找不到这个班级' : '加载失败', err.status === 404 ? `短码“${code}”不存在或已被重置，请向老师确认。` : err.message, html`<a class="btn primary" href="#/">重新输入</a>`)}</div></div>`);
    recentCodes().some(r => r.code === code) && err.status === 404 && store.set('recentCodes', recentCodes().filter(r => r.code !== code));
    return;
  }
  rememberCode(code, first.classroom);
  document.title = first.classroom + ' · Pancake 作业';
  onDispose(() => { document.title = 'Pancake 集控'; });
  const shareUrl = `${location.origin}${location.pathname}#/c/${code}`;
  $('#app').innerHTML = show(html`
    <header class="viewer-top">
      <a class="brand" href="#/">${logo()}</a>
      <div class="grow"><div class="class-name">${first.classroom}</div><div class="faint" style="font-size:12px">班级作业 · 只读</div></div>
      <button class="btn sm" data-act="copy" data-text="${shareUrl}" data-label="分享链接已复制">${icon('share')}<span>分享</span></button>
      <button class="btn ghost sm icon-only" data-act="toggle-theme" title="切换深浅色">${icon('moon')}</button>
    </header>
    <main class="viewer-body"><div id="bs"></div><div id="vb"></div></main>`);
  let cached = first.projects;
  const loadBoard = () => api(`${base.replace('/projects', '')}/board`, { auth: false }).then(b => { const el = $('#bs'); if (el) el.innerHTML = show(boardStrip(b)); }).catch(() => { });
  loadBoard();
  await mountBrowser($('#vb'), {
    asset: sha => `${base}/assets/${sha}`,
    onEvent: ev => { if (ev.type === 'board') loadBoard(); },
    list: async () => { if (cached) { const c = cached; cached = null; return c; } return (await api(base + '/projects', { auth: false })).projects; },
    detail: id => api(`${base}/projects/${id}`, { auth: false }),
    link: id => `c/${code}/${id}`,
    ws: `/ws/public/${encodeURIComponent(code)}`
  }, projectId);
}

/** 公告与今日值日卡片：公开页与班级页共用。 */
function boardStrip(b) {
  const items = b?.announcements || [], duty = b?.duty;
  if (!items.length && !duty) return '';
  return html`<div class="board-strip">
    ${items.map(a => html`<article class="notice-card ${a.level}">
      <div class="row wrap">${a.level !== 'info' ? levelBadge(a.level) : ''}${a.pinned ? html`<span class="badge">${icon('pin', 'sm')}置顶</span>` : ''}<b class="grow notice-title">${a.title}</b></div>
      ${a.body ? html`<p class="notice-body">${a.body}</p>` : ''}
      <div class="faint" style="font-size:12px">${a.author ? a.author + ' · ' : ''}${relTime(a.createdAt)}</div>
    </article>`)}
    ${duty ? html`<article class="notice-card duty">
      <div class="row">${icon('broom')}<b class="grow">今日值日</b><span class="faint" style="font-size:12px">${duty.label}</span></div>
      <div class="duty-today">${duty.tasks.map(t => html`<div><span>${t.task}</span><b>${t.students.join('、')}</b></div>`)}</div>
    </article>` : ''}
  </div>`;
}

// ============================================================ 概览
async function viewOverview() {
  const page = shell({ nav: 'overview', title: `你好，${state.me.displayName}`, crumbs: dateLabel(todayStr(), true) });
  setPage(page, loading());
  const [o, mine] = await Promise.all([api('/api/overview'), isAdmin() ? Promise.resolve([]) : assignments()]);
  const stat = (ic, label, value, sub, href) => html`<${href ? 'a' : 'div'} class="card stat ${href ? 'link' : ''}" ${href ? raw(`href="#/${href}"`) : ''}><div class="label">${icon(ic)}${label}</div><div class="value">${value}</div><div class="sub">${sub}</div></${href ? 'a' : 'div'}>`;
  const today = o.today || [];
  const ready = today.filter(t => t.projectId && t.filled > 0).length;
  setPage(page, html`
    ${state.me.weakPassword ? html`<div class="banner warn">${icon('alert')}<div class="grow"><b>当前账号使用弱密码</b><div class="muted" style="font-size:13px">默认或过短的密码容易被猜到，请尽快修改。</div></div><a class="btn sm" href="#/settings">修改密码</a></div>` : ''}
    ${o.deviceIssues?.length ? html`<div class="banner warn">${icon('device')}<div class="grow"><b>${o.deviceIssues.length} 台设备同步异常</b><div class="muted" style="font-size:13px">${o.deviceIssues.slice(0, 3).map(d => `${d.className} · ${d.deviceName}${d.lastError ? '：' + d.lastError : d.pendingChanges ? `：${d.pendingChanges} 项待上传` : ''}`).join('；')}</div></div><a class="btn sm" href="#/classes/${o.deviceIssues[0].classId}/devices">查看设备</a></div>` : ''}
    <div class="stats">
      ${stat('class', '班级', o.classCount, isAdmin() ? '全部班级' : '我负责的班级', 'classes')}
      ${stat('book', '今日作业', `${ready}/${today.length}`, '已布置作业的班级', 'homework')}
      ${stat('device', '已配对设备', o.deviceCount, `24 小时内活跃 ${o.activeDevices} 台 · 在线 ${o.online}`)}
      ${stat('student', '学生', o.studentCount ?? 0, '名单中的在读学生')}
      ${stat('megaphone', '生效公告', o.activeAnnouncements ?? 0, '正在大屏上显示', 'announcements')}
      ${o.userCount != null ? stat('users', '账号', o.userCount, '老师与管理员', 'users') : ''}
    </div>
    ${o.classCount === 0 ? html`<div class="card">${empty('class', isAdmin() ? '还没有班级' : '还没有分配班级', isAdmin() ? '创建第一个班级，然后用配对码把班级大屏连接到服务器。' : '请联系管理员把你分配到班级。', isAdmin() ? html`<a class="btn primary" href="#/classes?new=1">${icon('plus')}新建班级</a>` : '')}</div>` : html`
    <div class="grid-2">
      <section class="card">
        <div class="card-head"><h3 class="grow">今日作业</h3><span class="faint" style="font-size:12.5px">${dateLabel(todayStr())}</span></div>
        <div class="list-rows">${today.map(t => html`
          <div class="list-row">
            <span class="grow"><span class="title" style="display:block">${t.className}${t.online ? html` <span class="dot live" title="大屏在线" style="display:inline-block;vertical-align:middle;margin-left:4px"></span>` : ''}</span>
              <span class="meta">${t.projectId ? `${t.projectName} · ${t.filled}/${t.subjects} 个科目已布置` : '今天还没有作业项目'}</span>
              ${t.projectId ? progress(t.filled, t.subjects) : ''}</span>
            ${t.projectId ? html`<a class="btn sm" href="#/homework/${t.classId}/${t.projectId}">${icon('edit')}查看</a>` : html`<a class="btn sm primary" href="#/homework/${t.classId}?new=1">${icon('plus')}布置</a>`}
          </div>`)}</div>
      </section>
      ${mine.length ? html`<section class="card">
        <div class="card-head"><h3 class="grow">我的分工</h3></div>
        <div class="list-rows">${mine.map(a => html`<a class="list-row" href="#/classes/${a.classId}">
          <span class="grow"><span class="title" style="display:block">${a.className}</span><span class="meta">${DUTY_LABEL[a.duty] || a.duty}${a.subjects.length ? ' · ' + a.subjects.join('、') : ''}</span></span>
          ${a.duty === 'HeadTeacher' ? html`<span class="badge accent">班主任</span>` : ''}</a>`)}</div>
      </section>` : html`<section class="card">
        <div class="card-head"><h3 class="grow">最近动态</h3>${isAdmin() ? html`<a class="btn ghost sm" href="#/audit">操作日志${icon('right')}</a>` : ''}</div>
        <div style="padding:6px 0">${o.activity.length ? o.activity.map(a => html`
          <div class="timeline-item"><span class="dot"></span><div><div><b>${a.action}</b>${a.detail ? ' · ' + a.detail : ''}</div><div class="meta">${a.actor} · <span title="${fullTime(a.at)}">${relTime(a.at)}</span></div></div></div>`) : empty('log', '暂无动态', '创建班级、配对设备等操作会记录在这里。')}</div>
      </section>`}
    </div>
    <section class="card" style="margin-top:18px">
      <div class="card-head"><h3 class="grow">最近更新</h3><a class="btn ghost sm" href="#/homework">全部作业${icon('right')}</a></div>
      <div class="list-rows">${o.recent.length ? o.recent.map(p => html`
        <a class="list-row" href="#/homework/${p.classId}/${p.id}">
          <span class="grow"><span class="title" style="display:block">${p.name}${p.deleted ? html` <span class="badge danger">已删除</span>` : ''}</span><span class="meta">${p.className} · ${dateLabel(p.date)}${p.updatedBy ? ' · ' + p.updatedBy : ''}</span></span>
          <span class="meta nowrap" title="${fullTime(p.updatedAt)}">${relTime(p.updatedAt)}</span>
        </a>`) : empty('book', '暂无项目', '设备同步或在网页上新建项目后会出现在这里。')}</div>
    </section>`}`);
}

// ============================================================ 班级
async function pairingDialog(classId, className, preset) {
  const r = preset || await api(`/api/classes/${classId}/pairing-code`, { method: 'POST' });
  const expires = Date.now() + r.expiresInSeconds * 1000;
  const code = r.code || r.pairingCode;
  let timer;
  await modal({
    title: '配对班级设备', submit: '', cancel: '完成',
    body: html`<p class="muted">在班级大屏的 Pancake 中打开 <b>设置 → 集控</b>，填写以下信息：</p>
      <dl class="kv"><dt>服务器地址</dt><dd class="row"><span class="mono">${location.origin}</span><button type="button" class="btn ghost sm icon-only" data-act="copy" data-text="${location.origin}">${icon('copy', 'sm')}</button></dd><dt>班级</dt><dd>${className}</dd></dl>
      <div class="pair-code">${code.slice(0, 4)} ${code.slice(4)}</div>
      <div class="row"><span class="muted grow" id="pc-left"></span><button type="button" class="btn sm" data-act="copy" data-text="${code}" data-label="配对码已复制">${icon('copy')}复制配对码</button></div>
      <small class="faint">配对码只能使用一次。过期后可以重新生成。</small>`,
    onMount: (form, dlg) => {
      const tick = () => {
        const left = Math.max(0, Math.round((expires - Date.now()) / 1000));
        const el = $('#pc-left', dlg); if (!el) return;
        el.textContent = left ? `${Math.floor(left / 60)}:${pad(left % 60)} 后过期` : '已过期，请重新生成';
        if (!left) { $('.pair-code', dlg).style.opacity = '.35'; clearInterval(timer); }
      };
      tick(); timer = setInterval(tick, 1000);
    }
  });
  clearInterval(timer);
}

async function createClassFlow() {
  const created = await modal({
    title: '新建班级', submit: '创建',
    body: html`<label class="field"><span>班级名称</span><input class="input" name="name" placeholder="例如：高一（3）班" maxlength="60" required></label>
      <small class="faint">创建后会生成公开短码和一次性设备配对码。</small>`,
    onSubmit: f => { const name = f.elements.name.value.trim(); if (!name) throw new Error('请输入班级名称'); return api('/api/admin/classes', { method: 'POST', body: { name } }); }
  });
  if (!created) return null;
  toast('班级已创建');
  await pairingDialog(created.id, created.name, { code: created.pairingCode, expiresInSeconds: created.expiresInSeconds });
  return created;
}

async function viewClasses(query) {
  const page = shell({ nav: 'classes', title: '班级', actions: isAdmin() ? html`<button class="btn primary" data-act="new-class">${icon('plus')}新建班级</button>` : '' });
  viewActions['new-class'] = async () => { const c = await createClassFlow(); if (c) navigate('classes/' + c.id); };
  setPage(page, loading());
  const classes = await api('/api/classes');
  if (!classes.length) {
    setPage(page, html`<div class="card">${empty('class', isAdmin() ? '还没有班级' : '还没有分配班级', isAdmin() ? '每个班级对应一块班级大屏，创建后使用配对码连接设备。' : '请联系管理员把你分配到班级。', isAdmin() ? html`<button class="btn primary" data-act="new-class">${icon('plus')}新建班级</button>` : '')}</div>`);
  } else {
    setPage(page, html`<div class="toolbar"><label class="input-icon">${icon('search', 'sm')}<input class="input" id="cs" placeholder="搜索班级"></label><span class="faint">${classes.length} 个班级</span></div>
      <div class="class-grid" id="cg"></div>`);
    const renderGrid = q => $('#cg').innerHTML = show(classes.filter(c => !q || c.name.toLowerCase().includes(q) || c.publicCode.includes(q)).map(c => html`
      <a class="card class-card" href="#/classes/${c.id}">
        <div class="row"><h3 class="grow">${c.name}</h3>${c.online ? html`<span class="badge ok" title="实时连接数"><span class="dot live"></span>${c.online}</span>` : ''}</div>
        <div class="mini-stats"><div><b>${c.projectCount}</b><span>项目</span></div><div><b>${c.deviceCount}</b><span>设备</span></div><div><b>${c.teacherCount}</b><span>老师</span></div></div>
        <div class="foot"><span class="mono">${c.publicCode}</span><span class="spacer"></span><span>${c.lastUpdatedAt ? '更新于 ' + relTime(c.lastUpdatedAt) : '尚无同步'}</span></div>
      </a>`));
    renderGrid('');
    $('#cs').oninput = e => renderGrid(e.target.value.trim().toLowerCase());
  }
  if (query.get('new') && isAdmin()) viewActions['new-class']();
}

async function viewClass(classId, tab = 'projects') {
  const c = await api(`/api/classes/${classId}`);
  const publicUrl = `${location.origin}${location.pathname}#/c/${c.publicCode}`;
  const page = shell({
    nav: 'classes', title: c.name, crumbs: html`<a href="#/classes">班级</a>${icon('right', 'sm')}<span>${c.name}</span>`,
    actions: html`
      <a class="btn" href="#/homework/${c.id}">${icon('book')}查看作业</a>
      <button class="btn primary" data-act="pair">${icon('device')}配对设备</button>`
  });
  const tabs = [['projects', '项目', c.projectCount], ['layout', '布局'], ['people', '人员', c.teacherCount], ['duty', '值日'], ['notices', '公告'], ['devices', '设备', c.deviceCount], ['settings', '设置']];
  if (tab === 'teachers') tab = 'people';
  setPage(page, html`
    <div class="detail-head"><div class="grow">
      <div class="meta">
        <span class="code-chip" title="公开短码">${c.publicCode}<button class="btn ghost sm icon-only" data-act="copy" data-text="${publicUrl}" data-label="公开链接已复制" title="复制公开链接">${icon('link', 'sm')}</button></span>
        <span>创建于 ${fullTime(c.createdAt).slice(0, 10)}</span>
        <span>${c.lastUpdatedAt ? '最近同步 ' + relTime(c.lastUpdatedAt) : '尚无同步'}</span>
        ${c.online ? html`<span class="badge ok"><span class="dot live"></span>${c.online} 个实时连接</span>` : ''}
      </div>
    </div></div>
    <nav class="tabs">${tabs.map(([k, label, n]) => html`<a href="#/classes/${c.id}/${k}" class="${tab === k ? 'active' : ''}">${label}${n != null ? html`<span class="badge">${n}</span>` : ''}</a>`)}</nav>
    <div id="tab">${loading()}</div>`);
  viewActions.pair = () => pairingDialog(c.id, c.name);
  const box = $('#tab');
  const views = { projects: classProjects, layout: classLayout, people: classPeople, duty: classDuty, notices: classNotices, devices: classDevices };
  if (views[tab]) await views[tab](box, c);
  else classSettings(box, c, publicUrl);
}

async function classProjects(box, c) {
  const projects = await api(`/api/classes/${c.id}/projects`);
  let showDeleted = false;
  const render = () => {
    const rows = projects.filter(p => showDeleted || !p.deleted).sort((a, b) => (b.date || '').localeCompare(a.date || '') || parseTime(b.updatedAt) - parseTime(a.updatedAt));
    box.innerHTML = show(html`
      <div class="toolbar"><label class="check"><input type="checkbox" id="sd" ${showDeleted ? 'checked' : ''}>显示已删除（${projects.filter(p => p.deleted).length}）</label><span class="spacer"></span>
        <button class="btn sm" data-act="export">${icon('download')}导出 JSON</button></div>
      <div class="card">${rows.length ? html`<div class="table-wrap"><table class="table">
        <thead><tr><th>项目</th><th>日期</th><th class="hide-sm">最后修改</th><th class="hide-sm">修订</th><th></th></tr></thead>
        <tbody>${rows.map(p => html`<tr class="${p.deleted ? 'dim' : ''}">
          <td><a href="#/homework/${c.id}/${p.id}"><b>${p.name}</b></a> ${p.deleted ? html`<span class="badge danger">已删除</span>` : ''}</td>
          <td class="nowrap">${dateLabel(p.date)}</td>
          <td class="hide-sm"><span title="${fullTime(p.updatedAt)}">${relTime(p.updatedAt)}</span><div class="faint" style="font-size:12px">${p.updatedBy || ''}</div></td>
          <td class="hide-sm mono">${p.revision}</td>
          <td class="actions">
            <button class="btn ghost sm" data-act="history" data-id="${p.id}">${icon('history')}历史</button>
            ${p.deleted ? html`<button class="btn sm" data-act="restore" data-id="${p.id}">${icon('restore')}恢复</button>` : html`<a class="btn sm" href="#/homework/${c.id}/${p.id}">${icon('eye')}查看</a>`}
          </td></tr>`)}</tbody></table></div>`
        : empty('book', '暂无项目', '配对设备后，班级大屏上的项目会自动同步到这里。', html`<button class="btn primary" data-act="pair">${icon('device')}配对设备</button>`)}</div>`);
    $('#sd').onchange = e => { showDeleted = e.target.checked; render(); };
  };
  Object.assign(viewActions, {
    history: el => openHistory(c.id, projects.find(p => p.id === el.dataset.id), () => route(), consoleAsset(c.id)),
    restore: async el => { await api(`/api/classes/${c.id}/projects/${el.dataset.id}/restore`, { method: 'POST', body: {} }); toast('项目已恢复'); route(); },
    export: () => download(`/api/classes/${c.id}/export`)
  });
  render();
}

async function classDevices(box, c) {
  const devices = await api(`/api/classes/${c.id}/devices`);
  const status = d => {
    if (d.revoked) return html`<span class="badge danger">已吊销</span>`;
    if (d.online) return html`<span class="badge ok"><span class="dot live"></span>在线</span>`;
    const seen = parseTime(d.lastSeenAt);
    if (seen && Date.now() - seen < 15 * 60e3) return html`<span class="badge ok"><span class="dot ok"></span>活跃</span>`;
    return html`<span class="badge">离线</span>`;
  };
  const health = d => d.revoked ? '' : d.lastError ? html`<div class="faint" style="font-size:12px;color:var(--danger)">${icon('alert', 'sm')} ${d.lastError}</div>`
    : d.pendingChanges ? html`<div class="faint" style="font-size:12px">${d.pendingChanges} 项待上传</div>` : '';
  box.innerHTML = show(html`<div class="card">${devices.length ? html`<div class="table-wrap"><table class="table">
    <thead><tr><th>设备</th><th>状态</th><th>最近同步</th><th class="hide-sm">版本 · 当前项目</th><th></th></tr></thead>
    <tbody>${devices.map(d => html`<tr class="${d.revoked ? 'dim' : ''}">
      <td><div class="who">${icon('device')}<div><b>${d.deviceName || '未命名设备'}</b><span>配对于 ${fullTime(d.createdAt).slice(0, 10)}</span></div></div></td>
      <td>${status(d)}${health(d)}</td>
      <td title="${fullTime(d.lastSyncAt || d.lastSeenAt)}">${relTime(d.lastSyncAt || d.lastSeenAt)}</td>
      <td class="hide-sm">${d.appVersion || html`<span class="faint">未上报</span>`}${d.activeProject ? html`<div class="faint" style="font-size:12px">${d.activeProject}</div>` : ''}</td>
      <td class="actions">${d.revoked ? '' : html`
        <button class="btn ghost sm" data-act="dev-sync" data-id="${d.id}" ${d.online ? '' : 'disabled title="设备离线"'}>${icon('sync')}同步</button>
        <button class="btn ghost sm" data-act="dev-msg" data-id="${d.id}" ${d.online ? '' : 'disabled title="设备离线"'}>${icon('send')}消息</button>
        <button class="btn ghost sm icon-only" data-act="rename-device" data-id="${d.id}" title="重命名">${icon('edit')}</button>
        <button class="btn sm danger" data-act="revoke" data-id="${d.id}">吊销</button>`}</td></tr>`)}</tbody></table></div>`
    : empty('device', '还没有配对设备', '生成配对码，在班级大屏的 Pancake 设置 → 集控中输入即可连接。', html`<button class="btn primary" data-act="pair">${icon('device')}配对设备</button>`)}</div>
    <p class="faint" style="margin-top:12px;font-size:12.5px">在线设备可以远程触发同步或在大屏上弹出一条消息。吊销后该设备无法再读取或上传项目，需要重新配对。</p>`);
  const find = id => devices.find(d => d.id === id);
  Object.assign(viewActions, {
    'dev-sync': async el => {
      const r = await api(`/api/classes/${c.id}/devices/${el.dataset.id}/command`, { method: 'POST', body: { command: 'sync' } });
      toast(r.delivered ? '已通知设备立即同步' : '设备离线，未送达', r.delivered ? 'ok' : 'error');
    },
    'dev-msg': async el => {
      const d = find(el.dataset.id);
      const r = await modal({
        title: `发送消息到“${d.deviceName}”`, submit: '发送',
        body: html`<label class="field"><span>消息内容</span><textarea class="input" name="text" rows="3" maxlength="200" placeholder="例如：请值日生关好门窗"></textarea><small>消息会在大屏顶部提示条中显示。</small></label>`,
        onSubmit: f => { const text = f.text.value.trim(); if (!text) throw new Error('请输入消息内容'); return api(`/api/classes/${c.id}/devices/${d.id}/command`, { method: 'POST', body: { command: 'message', text } }); }
      });
      if (r) toast(r.delivered ? '消息已送达' : '设备离线，未送达', r.delivered ? 'ok' : 'error');
    },
    'rename-device': async el => {
      const d = find(el.dataset.id);
      const ok = await modal({
        title: '重命名设备', submit: '保存', body: html`<label class="field"><span>设备名称</span><input class="input" name="name" value="${d.deviceName}" maxlength="60"></label>`,
        onSubmit: f => api(`/api/classes/${c.id}/devices/${d.id}`, { method: 'PATCH', body: { name: f.elements.name.value.trim() } })
      });
      if (ok) { toast('设备已重命名'); route(); }
    },
    revoke: async el => {
      const d = find(el.dataset.id);
      if (!await confirmDialog({ title: '吊销设备', message: `吊销“${d.deviceName}”后，它将无法继续同步本班项目。确定吗？`, confirm: '吊销' })) return;
      await api(`/api/classes/${c.id}/devices/${d.id}`, { method: 'DELETE' }); toast('设备已吊销'); route();
    }
  });
}

// ---------- 人员：老师分工与学生名单
async function classPeople(box, c) {
  const [staff, students, duty] = await Promise.all([api(`/api/classes/${c.id}/staff`), api(`/api/classes/${c.id}/students`), myDuty(c.id)]);
  const canStaff = isAdmin() || duty === 'HeadTeacher';
  const canRoster = canStaff || !staff.some(t => t.duty === 'HeadTeacher');
  const others = isAdmin() ? (await api('/api/admin/users')).filter(u => u.role === 'Teacher' && !staff.some(t => t.id === u.id)) : [];
  let q = '';
  const groups = [...new Set(students.map(x => x.group).filter(Boolean))];
  const renderStudents = () => {
    const rows = students.filter(x => !q || x.name.includes(q) || x.number.includes(q) || x.group.includes(q));
    $('#sb').innerHTML = show(rows.length ? rows.map(x => html`<tr class="${x.active ? '' : 'dim'}">
      <td class="mono">${x.number || html`<span class="faint">—</span>`}</td>
      <td><b>${x.name}</b>${x.note ? html`<div class="faint" style="font-size:12px">${x.note}</div>` : ''}</td>
      <td>${x.group ? html`<span class="badge">${x.group}</span>` : html`<span class="faint">—</span>`}</td>
      <td class="hide-sm">${x.active ? html`<span class="badge ok">在读</span>` : html`<span class="badge">停用</span>`}</td>
      <td class="actions">${canRoster ? html`<button class="btn ghost sm icon-only" data-act="edit-student" data-id="${x.id}" title="编辑">${icon('edit')}</button><button class="btn ghost sm icon-only danger" data-act="del-student" data-id="${x.id}" title="移除">${icon('trash')}</button>` : ''}</td></tr>`)
      : html`<tr><td colspan="5">${empty('student', students.length ? '没有匹配的学生' : '还没有学生名单', students.length ? '换个关键词试试。' : '导入名单后可以安排值日，名单只保存在集控服务器上。')}</td></tr>`);
  };
  box.innerHTML = show(html`
    <section class="card">
      <div class="card-head"><h3 class="grow">老师与分工</h3>
        ${isAdmin() ? html`<select class="input" id="ta" style="width:220px" ${others.length ? '' : 'disabled'}><option value="">${others.length ? '选择要加入的老师…' : '没有可加入的老师'}</option>${others.map(u => html`<option value="${u.id}">${u.displayName}（${u.userName}）</option>`)}</select>
          <button class="btn sm primary" data-act="add-teacher" ${others.length ? '' : 'disabled'}>${icon('plus')}加入</button>` : ''}
      </div>
      ${staff.length ? html`<div class="list-rows">${staff.map(t => html`
        <div class="list-row">${avatar(t.displayName, 'sm')}
          <span class="grow"><span class="title" style="display:block">${t.displayName}${t.id === state.me.id ? html` <span class="badge accent">我</span>` : ''}${t.enabled ? '' : html` <span class="badge danger">已停用</span>`}</span>
            <span class="meta">${t.userName}</span></span>
          <span class="badge ${t.duty === 'HeadTeacher' ? 'accent' : t.duty === 'Assistant' ? '' : 'info'}">${DUTY_LABEL[t.duty] || t.duty}</span>
          <span class="chips hide-sm" style="max-width:300px">${t.duty === 'HeadTeacher' ? html`<span class="faint" style="font-size:12px">全部科目</span>` : t.subjects.length ? t.subjects.map(x => html`<span class="chip">${x}</span>`) : html`<span class="faint" style="font-size:12px">未限定科目</span>`}</span>
          ${canStaff ? html`<button class="btn ghost sm" data-act="edit-staff" data-id="${t.id}">${icon('edit')}分工</button>` : ''}
          ${isAdmin() ? html`<button class="btn ghost sm icon-only danger" data-act="remove-teacher" data-id="${t.id}" title="移出班级">${icon('x')}</button>` : ''}
        </div>`)}</div>` : empty('users', '还没有分配老师', isAdmin() ? '加入老师后可以设置班主任和科任老师的科目。' : '请联系管理员分配老师。')}
      <div class="card-body faint" style="font-size:12.5px;border-top:1px solid var(--border)">班主任可以管理本班全部内容和人员分工；指定了科目的科任老师和助教在网页上只能编辑自己负责的科目，未指定科目则不受限。</div>
    </section>
    <section class="card" style="margin-top:18px">
      <div class="card-head"><h3 class="grow">学生名单 <span class="faint" style="font-weight:500;font-size:13px">${students.filter(x => x.active).length} 人${groups.length ? ` · ${groups.length} 个小组` : ''}</span></h3>
        ${canRoster ? html`<button class="btn sm" data-act="import-students">${icon('upload')}批量导入</button><button class="btn sm primary" data-act="add-student">${icon('plus')}添加学生</button>` : ''}</div>
      <div class="card-body" style="padding-bottom:0"><label class="input-icon" style="max-width:280px;display:block">${icon('search', 'sm')}<input class="input" id="sq" placeholder="搜索姓名、学号或小组"></label></div>
      <div class="table-wrap"><table class="table"><thead><tr><th style="width:110px">学号</th><th>姓名</th><th>小组</th><th class="hide-sm">状态</th><th></th></tr></thead><tbody id="sb"></tbody></table></div>
    </section>`);
  renderStudents();
  $('#sq').oninput = e => { q = e.target.value.trim(); renderStudents(); };

  const studentForm = (x = {}) => html`<div class="grid-2" style="gap:14px">
      <label class="field"><span>姓名</span><input class="input" name="name" value="${x.name || ''}" maxlength="30"></label>
      <label class="field"><span>学号</span><input class="input" name="number" value="${x.number || ''}" maxlength="20"></label>
      <label class="field"><span>小组</span><input class="input" name="group" value="${x.group || ''}" maxlength="20" list="groups"><datalist id="groups">${groups.map(g => html`<option value="${g}">`)}</datalist></label>
      <label class="field"><span>备注</span><input class="input" name="note" value="${x.note || ''}" maxlength="100"></label></div>
    ${x.id ? html`<label class="check"><input type="checkbox" name="active" ${x.active ? 'checked' : ''}>在读（停用后不再参与值日）</label>` : ''}`;
  const studentBody = f => ({ name: f.elements.name.value.trim(), number: f.number.value.trim(), group: f.group.value.trim(), note: f.note.value.trim(), active: f.active ? f.active.checked : true });

  Object.assign(viewActions, {
    'add-teacher': async () => {
      const id = $('#ta').value; if (!id) return toast('请选择老师', 'error');
      await api(`/api/admin/classes/${c.id}/teachers/${id}`, { method: 'POST' }); toast('老师已加入班级'); route();
    },
    'remove-teacher': async el => {
      const t = staff.find(x => x.id === el.dataset.id);
      if (!await confirmDialog({ title: '移出班级', message: `将“${t.displayName}”移出${c.name}？该老师将无法再管理本班。`, confirm: '移出' })) return;
      await api(`/api/admin/classes/${c.id}/teachers/${t.id}`, { method: 'DELETE' }); toast('已移出'); route();
    },
    'edit-staff': async el => {
      const t = staff.find(x => x.id === el.dataset.id);
      const known = await api(`/api/classes/${c.id}/subjects`).catch(() => []);
      const all = [...new Set([...known, ...t.subjects])];
      const ok = await modal({
        title: `${t.displayName} 的分工`, submit: '保存',
        body: html`<label class="field"><span>职务</span><select class="input" name="duty">${Object.entries(DUTY_LABEL).map(([k, v]) => html`<option value="${k}" ${t.duty === k ? 'selected' : ''}>${v}</option>`)}</select><small>每班只有一位班主任，任命新班主任时原班主任会改为科任老师。</small></label>
          <div class="field" id="sf"><span>负责科目</span>
            <div class="checklist">${all.length ? all.map(x => html`<label class="check"><input type="checkbox" name="subj" value="${x}" ${t.subjects.includes(x) ? 'checked' : ''}>${x}</label>`) : html`<span class="faint" style="font-size:12.5px;padding:6px 0">本班项目里还没有科目，可以在下面手动添加。</span>`}</div>
            <div class="row"><input class="input grow" id="ns" placeholder="添加其他科目，例如：信息技术" maxlength="40"><button type="button" class="btn" id="nsb">${icon('plus')}添加</button></div>
            <small>不勾选任何科目表示不限科目。</small></div>`,
        onMount: f => {
          const toggle = () => $('#sf', f).classList.toggle('hidden', f.duty.value === 'HeadTeacher');
          f.duty.onchange = toggle; toggle();
          const add = () => {
            const v = $('#ns', f).value.trim(); if (!v) return;
            const list = $('.checklist', f);
            if (!$$('input[name=subj]', f).some(x => x.value === v)) { $('.faint', list)?.remove(); list.insertAdjacentHTML('beforeend', show(html`<label class="check"><input type="checkbox" name="subj" value="${v}" checked>${v}</label>`)); }
            $('#ns', f).value = '';
          };
          $('#nsb', f).onclick = add;
          $('#ns', f).onkeydown = e => { if (e.key === 'Enter') { e.preventDefault(); add(); } };
        },
        onSubmit: f => api(`/api/classes/${c.id}/staff/${t.id}`, { method: 'PUT', body: { duty: f.duty.value, subjects: f.duty.value === 'HeadTeacher' ? [] : $$('input[name=subj]:checked', f).map(x => x.value) } })
      });
      if (ok) { myAssignments = null; toast('分工已保存'); route(); }
    },
    'add-student': async () => {
      const ok = await modal({ title: '添加学生', submit: '添加', wide: true, body: studentForm(), onSubmit: f => { const b = studentBody(f); if (!b.name) throw new Error('请输入姓名'); return api(`/api/classes/${c.id}/students`, { method: 'POST', body: b }); } });
      if (ok) { toast('已添加'); route(); }
    },
    'edit-student': async el => {
      const x = students.find(y => y.id === el.dataset.id);
      const ok = await modal({ title: '编辑学生', submit: '保存', wide: true, body: studentForm(x), onSubmit: f => { const b = studentBody(f); if (!b.name) throw new Error('请输入姓名'); return api(`/api/classes/${c.id}/students/${x.id}`, { method: 'PATCH', body: b }); } });
      if (ok) { toast('已保存'); route(); }
    },
    'del-student': async el => {
      const x = students.find(y => y.id === el.dataset.id);
      if (!await confirmDialog({ title: '移除学生', message: `从名单中移除“${x.name}”？值日安排里的这名学生也会被移除。如果只是暂时不参与值日，可以改为“停用”。`, confirm: '移除' })) return;
      await api(`/api/classes/${c.id}/students/${x.id}`, { method: 'DELETE' }); toast('已移除'); route();
    },
    'import-students': async () => {
      const r = await modal({
        title: '批量导入学生', submit: '导入', wide: true,
        body: html`<p class="muted">每行一名学生，可以直接从表格复制粘贴。支持“姓名”“学号 姓名”或“学号 姓名 小组”，用空格、逗号或制表符分隔。</p>
          <textarea class="input mono" name="text" rows="10" placeholder="01 张三 第一组&#10;02 李四 第一组&#10;03 王五 第二组"></textarea>
          <label class="check"><input type="checkbox" name="replace">替换现有名单（不勾选时按学号或姓名合并更新）</label>`,
        onSubmit: f => { if (!f.text.value.trim()) throw new Error('请粘贴学生名单'); return api(`/api/classes/${c.id}/students/import`, { method: 'POST', body: { text: f.text.value, replaceAll: f.replace.checked } }); }
      });
      if (r) { toast(`导入完成：新增 ${r.added} 人，更新 ${r.updated} 人`); route(); }
    }
  });
}

// ---------- 值日安排
async function classDuty(box, c) {
  const [res, students, staff, duty] = await Promise.all([api(`/api/classes/${c.id}/duty`), api(`/api/classes/${c.id}/students`), api(`/api/classes/${c.id}/staff`), myDuty(c.id)]);
  const canEdit = isAdmin() || duty === 'HeadTeacher' || !staff.some(t => t.duty === 'HeadTeacher');
  const active = students.filter(x => x.active);
  const nameOf = id => active.find(x => x.id === id)?.name;
  if (!active.length) {
    box.innerHTML = show(html`<div class="card">${empty('broom', '先导入学生名单', '值日安排需要先在“人员”页导入学生名单。', html`<a class="btn primary" href="#/classes/${c.id}/people">${icon('student')}去导入名单</a>`)}</div>`);
    return;
  }
  const plan = JSON.parse(JSON.stringify(res.plan || {}));
  plan.mode ||= 'fixed'; plan.unit ||= 'day'; plan.weekdays ||= [1, 2, 3, 4, 5]; plan.tasks ||= []; plan.fixed ||= {}; plan.groups ||= [];
  plan.anchor ||= todayStr();
  if (!plan.tasks.length) plan.tasks = [{ id: shortId(), name: '扫地' }, { id: shortId(), name: '擦黑板' }, { id: shortId(), name: '倒垃圾' }];
  let dirty = false;
  const chips = ids => ids.map(nameOf).filter(Boolean).map(n => html`<span class="chip">${n}</span>`);

  const render = () => {
    box.innerHTML = show(html`<div class="stack">
      ${canEdit ? '' : html`<div class="banner warn">${icon('lock')}<span>值日安排由班主任维护，你可以查看。</span></div>`}
      <section class="card"><div class="card-head"><h3 class="grow">安排方式</h3>
        <div class="segmented" id="dm">${[['fixed', '按星期固定'], ['rotate', '小组轮换']].map(([k, l]) => html`<button type="button" data-v="${k}" class="${plan.mode === k ? 'active' : ''}" ${canEdit ? '' : 'disabled'}>${l}</button>`)}</div></div>
        <div class="card-body stack">
          <div class="row wrap"><span class="muted" style="min-width:72px">值日日</span>${[1, 2, 3, 4, 5, 6, 7].map(d => html`<label class="check day-check"><input type="checkbox" data-day="${d}" ${plan.weekdays.includes(d) ? 'checked' : ''} ${canEdit ? '' : 'disabled'}>${WEEKDAY_NAMES[d]}</label>`)}</div>
          <div class="row wrap"><span class="muted" style="min-width:72px">岗位</span>
            <span class="chips">${plan.tasks.map((t, i) => html`<span class="chip">${t.name}${canEdit ? html`<button type="button" class="chip-x" data-del-task="${i}" title="删除岗位">${icon('x', 'sm')}</button>` : ''}</span>`)}</span>
            ${canEdit ? html`<input class="input" id="nt" placeholder="新岗位" maxlength="20" style="width:140px"><button type="button" class="btn sm" id="ntb">${icon('plus')}添加</button>` : ''}</div>
          ${plan.mode === 'rotate' ? html`<div class="row wrap"><span class="muted" style="min-width:72px">轮换</span>
            <div class="segmented" id="du">${[['day', '每个值日日换一组'], ['week', '每周换一组']].map(([k, l]) => html`<button type="button" data-v="${k}" class="${plan.unit === k ? 'active' : ''}" ${canEdit ? '' : 'disabled'}>${l}</button>`)}</div>
            <label class="row" style="gap:6px"><span class="muted">从</span><input class="input" type="date" id="da" value="${plan.anchor}" style="width:160px" ${canEdit ? '' : 'disabled'}><span class="muted">起由第一组开始</span></label></div>` : ''}
        </div></section>

      ${plan.mode === 'fixed' ? html`<section class="card"><div class="card-head"><h3 class="grow">每周安排</h3><span class="faint" style="font-size:12.5px">点击格子选择学生</span></div>
        <div class="table-wrap"><table class="table duty-grid"><thead><tr><th>岗位</th>${plan.weekdays.map(d => html`<th>${WEEKDAY_NAMES[d]}</th>`)}</tr></thead>
        <tbody>${plan.tasks.map(t => html`<tr><td><b>${t.name}</b></td>${plan.weekdays.map(d => {
          const ids = plan.fixed[d]?.[t.id] || [];
          return html`<td><button type="button" class="duty-cell" data-cell="${d}|${t.id}" ${canEdit ? '' : 'disabled'}>${ids.length ? chips(ids) : html`<span class="faint">${canEdit ? '＋ 选择' : '—'}</span>`}</button></td>`;
        })}</tr>`)}</tbody></table></div></section>`
      : html`<section class="card"><div class="card-head"><h3 class="grow">值日小组</h3>
          ${canEdit ? html`<button type="button" class="btn sm" id="gby">${icon('users')}按名单中的小组生成</button><button type="button" class="btn sm" id="gsplit">平均分组</button><button type="button" class="btn sm primary" id="gadd">${icon('plus')}添加小组</button>` : ''}</div>
        <div class="card-body">${plan.groups.length ? html`<div class="group-grid">${plan.groups.map((g, i) => html`<div class="group-card">
            <div class="row"><span class="badge accent">${i + 1}</span><input class="input" data-gname="${i}" value="${g.name}" maxlength="20" ${canEdit ? '' : 'disabled'}>
              ${canEdit ? html`<button type="button" class="btn ghost sm icon-only danger" data-gdel="${i}" title="删除小组">${icon('trash')}</button>` : ''}</div>
            <button type="button" class="duty-cell" data-gmem="${i}" ${canEdit ? '' : 'disabled'}>${g.members.length ? chips(g.members) : html`<span class="faint">＋ 选择组员</span>`}</button>
          </div>`)}</div>` : empty('users', '还没有小组', '可以按名单中的“小组”一键生成，或平均分成若干组。')}
          <p class="faint" style="font-size:12.5px;margin-top:12px">轮到的小组按顺序分配岗位：第 1 位做第 1 个岗位，第 2 位做第 2 个岗位……人数多于岗位时循环分配。</p></div></section>`}

      <section class="card"><div class="card-head"><h3 class="grow">本周值日</h3>${dirty ? html`<span class="badge warn">保存后更新</span>` : ''}</div>
        <div class="week-grid">${res.week.map((d, i) => {
          const date = shiftDay(todayStr(), i + 1 - (new Date().getDay() || 7));
          return html`<div class="week-day ${date === todayStr() ? 'today' : ''}"><div class="week-head"><b>${WEEKDAY_NAMES[i + 1]}</b><span class="faint">${date.slice(5).replace('-', '/')}</span></div>
            ${d ? d.tasks.map(t => html`<div class="week-task"><span>${t.task}</span><b>${t.students.join('、')}</b></div>`) : html`<span class="faint" style="font-size:12.5px">休息</span>`}</div>`;
        })}</div></section>

      ${canEdit ? html`<div class="save-bar"><span class="faint grow">${res.updatedAt ? `上次保存：${res.updatedBy} · ${relTime(res.updatedAt)}` : '尚未保存'}　保存后班级大屏和公开页会立即显示今日值日。</span><button type="button" class="btn primary" id="ds">${icon('check')}保存安排</button></div>` : ''}
    </div>`);
    bind();
  };
  const touch = () => { dirty = true; render(); };
  const pickStudents = async (title, selected) => checklistModal({ title, items: active.map(x => ({ value: x.id, label: x.name, sub: [x.number, x.group].filter(Boolean).join(' · ') })), selected });
  const bind = () => {
    if (!canEdit) return;
    $$('#dm button').forEach(b => b.onclick = () => { plan.mode = b.dataset.v; touch(); });
    $$('#du button').forEach(b => b.onclick = () => { plan.unit = b.dataset.v; touch(); });
    $$('[data-day]').forEach(x => x.onchange = () => { const d = +x.dataset.day; plan.weekdays = x.checked ? [...plan.weekdays, d].sort() : plan.weekdays.filter(y => y !== d); touch(); });
    $$('[data-del-task]').forEach(b => b.onclick = () => { plan.tasks.splice(+b.dataset.delTask, 1); touch(); });
    const addTask = () => { const v = $('#nt').value.trim(); if (!v) return; plan.tasks.push({ id: shortId(), name: v }); touch(); $('#nt')?.focus(); };
    $('#ntb').onclick = addTask; $('#nt').onkeydown = e => { if (e.key === 'Enter') addTask(); };
    if ($('#da')) $('#da').onchange = e => { plan.anchor = e.target.value || todayStr(); dirty = true; };
    $$('[data-cell]').forEach(b => b.onclick = async () => {
      const [d, t] = b.dataset.cell.split('|');
      const task = plan.tasks.find(x => x.id === t);
      const ids = await pickStudents(`${WEEKDAY_NAMES[d]} · ${task.name}`, plan.fixed[d]?.[t] || []);
      if (!ids) return;
      (plan.fixed[d] ||= {})[t] = ids; touch();
    });
    $$('[data-gname]').forEach(x => x.oninput = () => { plan.groups[+x.dataset.gname].name = x.value; dirty = true; });
    $$('[data-gdel]').forEach(b => b.onclick = () => { plan.groups.splice(+b.dataset.gdel, 1); touch(); });
    $$('[data-gmem]').forEach(b => b.onclick = async () => {
      const g = plan.groups[+b.dataset.gmem];
      const ids = await pickStudents(`${g.name} · 组员`, g.members);
      if (ids) { g.members = ids; touch(); }
    });
    if ($('#gadd')) $('#gadd').onclick = () => { plan.groups.push({ name: `第${plan.groups.length + 1}组`, members: [] }); touch(); };
    if ($('#gby')) $('#gby').onclick = () => {
      const names = [...new Set(active.map(x => x.group).filter(Boolean))];
      if (!names.length) return toast('名单里还没有填写小组', 'error');
      plan.groups = names.map(n => ({ name: n, members: active.filter(x => x.group === n).map(x => x.id) })); touch();
    };
    if ($('#gsplit')) $('#gsplit').onclick = async () => {
      const r = await modal({ title: '平均分组', submit: '生成', body: html`<label class="field"><span>分成几组</span><input class="input" type="number" name="n" min="1" max="30" value="${Math.max(1, Math.min(10, Math.round(active.length / 4)))}"><small>按名单顺序依次分配，共 ${active.length} 名学生。</small></label>`, onSubmit: f => Math.max(1, Math.min(30, +f.n.value || 1)) });
      if (!r) return;
      plan.groups = Array.from({ length: r }, (_, i) => ({ name: `第${i + 1}组`, members: active.filter((_, k) => Math.floor(k * r / active.length) === i).map(x => x.id) }));
      touch();
    };
    $('#ds').onclick = async () => {
      try {
        await api(`/api/classes/${c.id}/duty`, { method: 'PUT', body: { expectedRevision: res.revision, plan } });
        toast('值日安排已保存'); route();
      } catch (err) { fail(err); }
    };
  };
  render();
}

// ---------- 公告
function announcementForm(a = {}) {
  const local = v => { const d = parseTime(v); return d ? `${ymd(d)}T${hm(d)}` : ''; };
  return html`<label class="field"><span>标题</span><input class="input" name="title" value="${a.title || ''}" maxlength="60" placeholder="例如：明天上午体检"></label>
    <label class="field"><span>内容（可选）</span><textarea class="input" name="body" rows="4" maxlength="2000">${a.body || ''}</textarea></label>
    <div class="grid-2" style="gap:14px">
      <label class="field"><span>级别</span><select class="input" name="level">${Object.entries(LEVEL_LABEL).map(([k, v]) => html`<option value="${k}" ${(a.level || 'info') === k ? 'selected' : ''}>${v}${k === 'urgent' ? '（红色醒目）' : k === 'important' ? '（橙色醒目）' : ''}</option>`)}</select></label>
      <label class="check" style="align-self:end"><input type="checkbox" name="pinned" ${a.pinned ? 'checked' : ''}>置顶（同级别中排在最前）</label>
      <label class="field"><span>开始显示</span><input class="input" type="datetime-local" name="starts" value="${local(a.startsAt)}"><small>留空表示立即显示</small></label>
      <label class="field"><span>结束显示</span><input class="input" type="datetime-local" name="expires" value="${local(a.expiresAt)}"><small>留空表示一直显示</small></label>
    </div>`;
}
function announcementBody(f) {
  const title = f.title.value.trim(); if (!title) throw new Error('请输入标题');
  const iso = v => v ? new Date(v).toISOString() : null;
  return { title, body: f.body.value.trim(), level: f.level.value, pinned: f.pinned.checked, startsAt: iso(f.starts.value), expiresAt: iso(f.expires.value) };
}
function announcementCard(a, { showClass = false, actions = true } = {}) {
  return html`<article class="notice-card ${a.level} ${a.active ? '' : 'inactive'}">
    <div class="row">${levelBadge(a.level)}${a.pinned ? html`<span class="badge">${icon('pin', 'sm')}置顶</span>` : ''}${a.active ? '' : html`<span class="badge">${parseTime(a.startsAt) > new Date() ? '未开始' : '已结束'}</span>`}
      <b class="grow notice-title">${a.title}</b>
      ${actions ? html`<button class="btn ghost sm icon-only" data-act="edit-notice" data-id="${a.id}" title="编辑">${icon('edit')}</button><button class="btn ghost sm icon-only danger" data-act="del-notice" data-id="${a.id}" title="删除">${icon('trash')}</button>` : ''}</div>
    ${a.body ? html`<p class="notice-body">${a.body}</p>` : ''}
    <div class="faint" style="font-size:12px">${showClass ? a.className + ' · ' : ''}${a.author} · 发布于 ${relTime(a.createdAt)}${a.expiresAt ? ` · 显示到 ${fullTime(a.expiresAt)}` : ''}</div>
  </article>`;
}
async function classNotices(box, c) {
  const items = await api(`/api/classes/${c.id}/announcements`);
  box.innerHTML = show(html`
    <div class="toolbar"><span class="muted grow">生效中的公告会实时显示在班级大屏的班务条上（多条轮播，紧急和重要靠前），也会出现在公开页上。可在布局设置里改为只显示重要公告。</span><button class="btn primary" data-act="new-notice">${icon('plus')}发布公告</button></div>
    ${items.length ? html`<div class="stack">${items.map(a => announcementCard(a))}</div>` : html`<div class="card">${empty('megaphone', '还没有公告', '发布后会实时推送到班级大屏。')}</div>`}`);
  const find = id => items.find(a => a.id === id);
  Object.assign(viewActions, {
    'new-notice': async () => { if (await modal({ title: '发布公告', submit: '发布', wide: true, body: announcementForm(), onSubmit: f => api(`/api/classes/${c.id}/announcements`, { method: 'POST', body: announcementBody(f) }) })) { toast('公告已发布'); route(); } },
    'edit-notice': async el => { const a = find(el.dataset.id); if (await modal({ title: '编辑公告', submit: '保存', wide: true, body: announcementForm(a), onSubmit: f => api(`/api/classes/${c.id}/announcements/${a.id}`, { method: 'PUT', body: announcementBody(f) }) })) { toast('公告已更新'); route(); } },
    'del-notice': async el => { const a = find(el.dataset.id); if (!await confirmDialog({ title: '删除公告', message: `删除“${a.title}”？大屏上会立即撤下。`, confirm: '删除' })) return; await api(`/api/classes/${c.id}/announcements/${a.id}`, { method: 'DELETE' }); toast('已删除'); route(); }
  });
}

// ---------- 布局与班级设置
/**
 * 班级设置页：上半部分是可拖动的布局编辑器（分屏比例、磁贴位置与大小、自由布局组件），
 * 下半部分按服务端清单（/api/settings-schema）生成全部设置表单。保存时只提交改动的键，
 * 服务端按键合并，大屏上同时修改的其他设置不会被覆盖。
 */
const DOCKED_WIDGET = /^(Split|ClockMode)/;
const WIDGET_LABEL = { Clock: '时钟', Components: '组件卡', Weather: '天气', Noise: '噪音' };
let weatherCities = null;
const loadWeatherCities = async () => weatherCities ??= await fetch('weather-cities.json').then(r => r.json()).catch(() => []);

function boardColors(doc) {
  const theme = doc.Theme || 'Dark';
  const dark = theme !== 'Light' && !(theme === 'Default' && !matchMedia('(prefers-color-scheme: dark)').matches);
  return { dark, bg: dark ? '#0E0E12' : '#F4F4F7', surface: dark ? '#16161E' : '#FFFFFF', line: dark ? '#2A2A35' : '#D9D9E6', text: dark ? '#F0F0F5' : '#111111', muted: dark ? '#737386' : '#8A8A99' };
}
/** #AARRGGBB / #RRGGBB → CSS 颜色。 */
function cssColor(hex, fallback) {
  if (typeof hex !== 'string' || !/^#([0-9a-f]{6}|[0-9a-f]{8})$/i.test(hex)) return fallback;
  if (hex.length === 7) return hex;
  return `rgba(${parseInt(hex.slice(3, 5), 16)},${parseInt(hex.slice(5, 7), 16)},${parseInt(hex.slice(7, 9), 16)},${(parseInt(hex.slice(1, 3), 16) / 255).toFixed(3)})`;
}
const bgFill = (b, fallback) => b && b.Color && !b.ColorCleared ? cssColor(b.Color, fallback) : fallback;

/**
 * 交互式布局示意图。坐标系：整屏 1600×900；作业板内的磁贴按大屏作业板坐标缩放显示。
 * state = { doc, tiles: [{index,name,color,x,y,w,h}], widgets: {key:{X,Y,Width,Height}} }，拖动时直接修改 state 并回调 onChange。
 */
function mountLayoutEditor(host, state, onChange) {
  const W = 1600, H = 900, pad = 24;
  let drag = null;
  const geom = () => {
    const doc = state.doc, mode = doc.LayoutMode || 'Split';
    const ratio = Math.min(.9, Math.max(.1, +doc.SplitRatio || .4));
    const clockW = mode === 'Split' ? W * ratio : mode === 'Clock' ? W : 0;
    const boardX = mode === 'Split' ? clockW : 0, boardW = mode === 'Clock' ? 0 : W - boardX;
    const maxX = Math.max(1200, ...state.tiles.map(t => t.x + t.w)), maxY = Math.max(700, ...state.tiles.map(t => t.y + t.h));
    const scale = boardW ? Math.min((boardW - pad * 2) / maxX, (H - pad * 2) / maxY, 1.2) : 1;
    return { mode, ratio, clockW, boardX, boardW, scale };
  };
  const draw = () => {
    const doc = state.doc, g = geom(), col = boardColors(doc);
    const grid = Math.max(16, Math.min(160, +doc.GridSize || 48)) * g.scale;
    const gridStyle = doc.GridStyle || 'Grid';
    const gridColor = cssColor(doc.GridColor, col.line), dotColor = cssColor(doc.GridDotColor, col.line);
    const pos = doc.ToolbarPosition || 'BottomCenter', tbScale = Math.min(2, Math.max(.6, +doc.ToolbarScale || 1));
    const vertical = pos.startsWith('Center');
    const tb = { w: (vertical ? 56 : 360) * tbScale, h: (vertical ? 300 : 56) * tbScale };
    const ix = +doc.ToolbarHorizontalInset || 24, iy = +doc.ToolbarVerticalInset || 24;
    const tx = pos.endsWith('Left') ? ix : pos.endsWith('Right') ? W - ix - tb.w : (W - tb.w) / 2;
    const ty = pos.startsWith('Top') ? iy : vertical ? (H - tb.h) / 2 : H - iy - tb.h;
    const titleSize = Math.max(10, (+doc.TileTitleSize || 29) * g.scale), bodySize = Math.max(8, (+doc.TileBodyFontSize || 20) * g.scale);
    const tileFill = bgFill(doc.TileBackground, col.bg), clockFill = bgFill(doc.ClockBackground, col.surface), boardFill = bgFill(doc.BoardBackground, col.surface);
    const font = esc(doc.TileBodyFontFamily || 'HarmonyOS Sans SC');
    const free = g.mode === 'Free' ? Object.entries(state.widgets).filter(([k]) => !DOCKED_WIDGET.test(k)) : [];
    host.innerHTML = `<svg class="layout-preview editable" viewBox="0 0 ${W} ${H}" role="img" aria-label="布局编辑器">
      <rect width="${W}" height="${H}" fill="${col.bg}"/>
      ${g.clockW ? `<rect width="${g.clockW}" height="${H}" fill="${clockFill}"/><text x="${g.clockW / 2}" y="${H / 2}" fill="${cssColor(doc.ClockColor, col.text)}" font-size="${Math.min(150, g.clockW / 4)}" font-weight="${{ Light: 300, Normal: 400, Medium: 500, Bold: 700 }[doc.ClockFontWeight] || 300}" text-anchor="middle" font-family="${esc(doc.ClockFontFamily || 'Segoe UI')}, sans-serif">08:30</text>
        <text x="${g.clockW / 2}" y="${H / 2 + 60}" fill="${col.muted}" font-size="${Math.min(34, g.clockW / 14)}" text-anchor="middle">${esc(dateLabel(todayStr(), true))}</text>` : ''}
      ${g.boardW ? `<rect x="${g.boardX}" width="${g.boardW}" height="${H}" fill="${boardFill}"/>
        ${gridStyle !== 'None' && grid >= 6 ? `<pattern id="lpg" x="${g.boardX + pad}" y="${pad}" width="${grid}" height="${grid}" patternUnits="userSpaceOnUse">${gridStyle === 'Dots'
          ? `<circle cx="0" cy="0" r="${Math.max(1, (+doc.GridDotDiameter || 3) * g.scale / 2)}" fill="${dotColor}"/>`
          : `<path d="M ${grid} 0 L 0 0 0 ${grid}" fill="none" stroke="${gridColor}" stroke-width="${Math.max(.5, (+doc.GridLineThickness || 1.6) * g.scale)}"/>`}</pattern><rect x="${g.boardX}" width="${g.boardW}" height="${H}" fill="url(#lpg)"/>` : ''}
        ${state.tiles.map((t, i) => { const x = g.boardX + pad + t.x * g.scale, y = pad + t.y * g.scale, w = t.w * g.scale, h = t.h * g.scale;
          return `<g class="lp-tile" data-tile="${i}"><rect x="${x}" y="${y}" width="${w}" height="${h}" rx="10" fill="${tileFill}" stroke="${t.color || '#818cf8'}" stroke-width="3"/>
            <text x="${x + 14}" y="${y + 12 + titleSize}" fill="${col.text}" font-size="${titleSize}" font-weight="600" font-family="${font}">${esc(t.name).slice(0, 12)}</text>
            ${[0, 1, 2].map(k => `<rect x="${x + 14}" y="${y + 26 + titleSize + k * bodySize * 1.6}" width="${Math.max(0, w * (k === 2 ? .45 : .75) - 14)}" height="${bodySize * .55}" rx="3" fill="${col.muted}" opacity=".45"/>`).join('')}
            <rect class="lp-grip" data-grip="${i}" x="${x + w - 18}" y="${y + h - 18}" width="16" height="16" rx="3"/></g>`; }).join('')}
        ${state.tiles.length ? '' : `<text x="${g.boardX + g.boardW / 2}" y="${H / 2}" fill="${col.muted}" font-size="34" text-anchor="middle">作业板（选择项目后可拖动磁贴）</text>`}` : ''}
      ${free.map(([k, r]) => `<g class="lp-widget" data-widget="${esc(k)}"><rect x="${r.X}" y="${r.Y}" width="${Math.max(40, r.Width)}" height="${Math.max(30, r.Height)}" rx="12" fill="${col.surface}" stroke="var(--accent)" stroke-width="3" stroke-dasharray="10 6"/>
        <text x="${r.X + 16}" y="${r.Y + 40}" fill="${col.text}" font-size="28">${esc(WIDGET_LABEL[k] || k)}</text></g>`).join('')}
      ${g.mode === 'Split' ? `<rect class="lp-split" x="${g.clockW - 24}" y="0" width="48" height="${H}"/><rect x="${g.clockW - 3}" y="0" width="6" height="${H}" fill="var(--accent)" opacity=".6" pointer-events="none"/>` : ''}
      <rect x="${tx}" y="${ty}" width="${tb.w}" height="${tb.h}" rx="${Math.min(+doc.ToolbarRadius || 14, 28) * tbScale}" fill="${doc.ToolbarBackgroundColor && !doc.ToolbarBackgroundColorCleared ? cssColor(doc.ToolbarBackgroundColor, col.surface) : col.surface}"
        fill-opacity="${doc.ToolbarBackgroundColorCleared ? .15 : Math.max(.15, +(doc.ToolbarBackgroundOpacity ?? .8))}" stroke="${cssColor(doc.ToolbarBorderColor, col.line)}" stroke-width="${Math.min(20, +(doc.ToolbarBorderThickness ?? 1)) * 2}"/>
    </svg>`;
  };
  const point = e => { const svg = $('svg', host), r = svg.getBoundingClientRect(); return { x: (e.clientX - r.left) / r.width * W, y: (e.clientY - r.top) / r.height * H }; };
  const snap = v => { const gs = Math.max(16, Math.min(160, +state.doc.GridSize || 48)); return state.doc.GridSnappingEnabled === false ? Math.round(v) : Math.round(v / gs) * gs; };
  host.addEventListener('pointerdown', e => {
    const p = point(e), g = geom();
    const grip = e.target.closest('[data-grip]'), tile = e.target.closest('[data-tile]'), widget = e.target.closest('[data-widget]'), split = e.target.closest('.lp-split');
    if (grip) { const t = state.tiles[+grip.dataset.grip]; drag = { kind: 'resize', t, p, w: t.w, h: t.h, g }; }
    else if (tile) { const t = state.tiles[+tile.dataset.tile]; drag = { kind: 'tile', t, p, x: t.x, y: t.y, g }; }
    else if (widget) { const r = state.widgets[widget.dataset.widget]; drag = { kind: 'widget', r, p, x: r.X, y: r.Y }; }
    else if (split) drag = { kind: 'split' };
    if (drag) { host.setPointerCapture(e.pointerId); e.preventDefault(); }
  });
  host.addEventListener('pointermove', e => {
    if (!drag) return;
    const p = point(e);
    if (drag.kind === 'split') state.doc.SplitRatio = Math.round(Math.min(.9, Math.max(.1, p.x / W)) * 100) / 100;
    else if (drag.kind === 'tile') { drag.t.x = Math.max(0, snap(drag.x + (p.x - drag.p.x) / drag.g.scale)); drag.t.y = Math.max(0, snap(drag.y + (p.y - drag.p.y) / drag.g.scale)); drag.t.moved = true; }
    else if (drag.kind === 'resize') { drag.t.w = Math.max(160, snap(drag.w + (p.x - drag.p.x) / drag.g.scale)); drag.t.h = Math.max(120, snap(drag.h + (p.y - drag.p.y) / drag.g.scale)); drag.t.moved = true; }
    else if (drag.kind === 'widget') { drag.r.X = Math.round(Math.max(0, drag.x + p.x - drag.p.x)); drag.r.Y = Math.round(Math.max(0, drag.y + p.y - drag.p.y)); }
    draw();
  });
  const end = () => { if (!drag) return; const kind = drag.kind; drag = null; onChange(kind); };
  host.addEventListener('pointerup', end);
  host.addEventListener('pointercancel', end);
  draw();
  return { draw };
}

/** 一项设置的表单控件；值取 current(key)。 */
function settingControl(s, current, fonts, assetUrl) {
  const v = current(s.key);
  const k = esc(s.key);
  const hint = s.hint ? html`<small>${s.hint}</small>` : '';
  switch (s.kind) {
    case 'Bool': return html`<label class="check set-bool"><input type="checkbox" data-k="${s.key}" ${v ? 'checked' : ''}>${s.label}</label>`;
    case 'Number': {
      const pct = s.min >= 0 && s.max <= 1;
      return html`<label class="field"><span>${s.label} · <b data-out="${s.key}">${pct ? Math.round(v * 100) + '%' : +(+v).toFixed(2)}</b></span>
        <input type="range" data-k="${s.key}" min="${s.min}" max="${s.max}" step="${s.step || (pct ? .01 : 1)}" value="${v}">${hint}</label>`;
    }
    case 'Enum': return html`<label class="field"><span>${s.label}</span><select class="input" data-k="${s.key}">${s.options.map(o => html`<option value="${o.value}" ${o.value === v ? 'selected' : ''}>${o.label}</option>`)}</select>${hint}</label>`;
    case 'Font': {
      const list = [...new Set([...(s.default ? [s.default] : []), ...fonts, ...(v ? [v] : [])])];
      return html`<label class="field"><span>${s.label}</span><select class="input" data-k="${s.key}">${s.default === '' ? html`<option value="" ${!v ? 'selected' : ''}>默认（内置字体）</option>` : ''}${list.map(f => html`<option value="${f}" ${f === v ? 'selected' : ''}>${f}</option>`)}</select>${hint}</label>`;
    }
    case 'Color': {
      const value = typeof v === 'string' ? v : '';
      const alpha = value.length === 9 ? parseInt(value.slice(1, 3), 16) : 255;
      const rgb = value.length >= 7 ? '#' + value.slice(-6) : '#808080';
      const cleared = s.clearedKey ? !!current(s.clearedKey) : false;
      return html`<div class="field set-color" data-color="${s.key}"><span>${s.label}</span>
        <div class="row wrap">
          <input type="color" data-c="rgb" value="${rgb}" ${!value || cleared ? 'disabled' : ''} aria-label="${s.label}">
          ${value.length === 9 || /Grid/.test(s.key) ? html`<label class="set-alpha" title="不透明度"><input type="range" data-c="alpha" min="0" max="255" value="${alpha}" ${!value || cleared ? 'disabled' : ''}><b>${Math.round(alpha / 2.55)}%</b></label>` : ''}
          ${s.default === '' ? html`<label class="check"><input type="checkbox" data-c="auto" ${!value ? 'checked' : ''} ${cleared ? 'disabled' : ''}>跟随主题</label>` : ''}
          ${s.clearedKey ? html`<label class="check"><input type="checkbox" data-c="cleared" ${cleared ? 'checked' : ''}>不使用背景色</label>` : ''}
        </div>${hint}</div>`;
    }
    case 'Background': return backgroundControl(s, v || {}, assetUrl);
    default: return '';
  }
}
function backgroundControl(s, b, assetUrl) {
  const media = p => {
    const m = ASSET_RE.exec(p || '');
    if (!p) return html`<span class="faint">无</span>`;
    if (!m) return html`<span class="badge" title="${p}">大屏本地文件</span>`;
    const src = assetUrl(m[1].toLowerCase());
    return IMAGE_EXT.test(m[2] || '') ? html`<img class="bg-thumb" src="${src}" alt="">` : html`<span class="badge">${icon('image', 'sm')}${(m[2] || '').slice(1).toUpperCase() || '文件'}</span>`;
  };
  const list = b.Playlist || [];
  return html`<div class="set-bg" data-bg="${s.key}">
    <div class="grid-2" style="gap:12px">
      <div class="field set-color"><span>背景颜色</span><div class="row wrap">
        <input type="color" data-bf="Color" value="${(b.Color || '').length >= 7 ? '#' + b.Color.slice(-6) : '#202024'}" ${!b.Color || b.ColorCleared ? 'disabled' : ''}>
        <label class="check"><input type="checkbox" data-bf="ColorAuto" ${!b.Color ? 'checked' : ''}>跟随主题</label>
        <label class="check"><input type="checkbox" data-bf="ColorCleared" ${b.ColorCleared ? 'checked' : ''}>不使用颜色</label></div></div>
      <label class="field"><span>颜色不透明度 · <b>${Math.round((b.ColorOpacity ?? .8) * 100)}%</b></span><input type="range" data-bf="ColorOpacity" min="0" max="1" step="0.01" value="${b.ColorOpacity ?? .8}"></label>
      <div class="field"><span>图片或视频</span><div class="row wrap">${media(b.ImagePath)}
        <button type="button" class="btn sm" data-bg-act="pick">${icon('upload')}选择文件</button>
        ${b.ImagePath ? html`<button type="button" class="btn ghost sm danger" data-bg-act="clear">移除</button>` : ''}</div></div>
      <label class="field"><span>图片模式</span><select class="input" data-bf="ImageMode">${[['Zoom', '缩放'], ['Stretch', '拉伸'], ['Fit', '适应']].map(([k, l]) => html`<option value="${k}" ${(b.ImageMode || 'Zoom') === k ? 'selected' : ''}>${l}</option>`)}</select></label>
      <label class="check"><input type="checkbox" data-bf="Glass" ${b.Glass ? 'checked' : ''}>毛玻璃效果</label>
      <label class="field"><span>模糊程度 · <b>${Math.round(b.Blur ?? 20)}</b></span><input type="range" data-bf="Blur" min="0" max="100" step="1" value="${b.Blur ?? 20}"></label>
    </div>
    <div class="set-playlist">
      <label class="check"><input type="checkbox" data-bf="PlaylistEnabled" ${b.PlaylistEnabled ? 'checked' : ''}>播放队列（轮播多张图片或视频）</label>
      <div class="row wrap">${list.map((p, i) => html`<span class="attachment with-thumb">${media(p)}<button type="button" class="att-x" data-bg-act="del-item" data-i="${i}" title="移除">${icon('x', 'sm')}</button></span>`)}
        <button type="button" class="btn ghost sm" data-bg-act="add-items">${icon('plus')}添加到队列</button></div>
      <div class="row wrap" style="gap:14px">
        <label class="check"><input type="checkbox" data-bf="Shuffle" ${b.Shuffle ? 'checked' : ''}>随机播放</label>
        <label class="check"><input type="checkbox" data-bf="SwitchOnTimer" ${b.SwitchOnTimer !== false ? 'checked' : ''}>按固定时间切换</label>
        <label class="field" style="width:150px"><span>切换间隔（秒）</span><input class="input" type="number" min="1" max="86400" data-bf="SwitchIntervalSeconds" value="${b.SwitchIntervalSeconds ?? 60}"></label>
        <label class="check"><input type="checkbox" data-bf="SwitchOnMediaEnded" ${b.SwitchOnMediaEnded !== false ? 'checked' : ''}>视频播放完成时切换</label>
      </div>
    </div>
  </div>`;
}

async function classLayout(box, c) {
  const [layout, schema, projects, classes, history, duty, fonts] = await Promise.all([
    api(`/api/classes/${c.id}/layout`), api('/api/settings-schema'), api(`/api/classes/${c.id}/projects`), api('/api/classes'),
    api(`/api/classes/${c.id}/layout/history`).catch(() => []), myDuty(c.id), api(`/api/classes/${c.id}/fonts`).catch(() => ['HarmonyOS Sans SC'])]);
  const canPolicy = isAdmin() || duty === 'HeadTeacher';
  const assetUrl = consoleAsset(c.id);
  let saved = {}; try { saved = JSON.parse(layout.payloadJson || '{}'); } catch { }
  const byKey = Object.fromEntries(schema.settings.map(s => [s.key, s]));
  const draft = {};
  const clone = v => v === undefined ? undefined : JSON.parse(JSON.stringify(v));
  const current = k => k in draft ? draft[k] : saved[k] !== undefined ? saved[k] : byKey[k]?.default;
  const same = (a, b) => JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
  const setValue = (k, v) => { const base = saved[k] !== undefined ? saved[k] : byKey[k]?.default; if (same(v, base)) delete draft[k]; else draft[k] = v; };
  const merged = () => { const d = { ...saved }; for (const s of schema.settings) d[s.key] = current(s.key); return d; };

  // 磁贴：取一个项目的科目位置；拖动后保存到该项目。
  const live = projects.filter(p => !p.deleted).sort((a, b) => (b.date || '').localeCompare(a.date || '') || parseTime(b.updatedAt) - parseTime(a.updatedAt));
  let tileProject = live[0] || null, tileDetail = null;
  const editor = { doc: merged(), tiles: [], widgets: clone(current('Widgets')) || {} };
  async function loadTiles() {
    editor.tiles = [];
    if (!tileProject) return;
    tileDetail = await api(`/api/classes/${c.id}/projects/${tileProject.id}`);
    editor.tiles = parseProject(tileDetail.payloadJson, tileDetail.runs).filter(s => s.rect.w > 0).map(s => ({ index: s.index, name: s.name, color: s.color, x: s.rect.x, y: s.rect.y, w: s.rect.w, h: s.rect.h }));
  }
  await loadTiles();

  const policies = [['Shared', '双向同步', '大屏上调整的设置会上传，网页修改会下发到所有大屏。不同设置可以同时修改。'], ['Managed', '网页统一下发', '只能在网页修改；大屏上的改动会被还原。'], ['Off', '不同步', '每块大屏各自保存设置。']];
  const groups = schema.groups.filter(g => schema.settings.some(s => s.group === g && (!s.hidden || g === '天气')));
  box.innerHTML = show(html`<div class="layout-page">
    <section class="card layout-main">
      <div class="card-head"><h3 class="grow">布局编辑</h3>
        <select class="input sm" id="lp-project" style="width:auto;max-width:220px" aria-label="磁贴取自项目">${live.length ? live.slice(0, 40).map(p => html`<option value="${p.id}">${p.name}</option>`) : html`<option value="">暂无项目</option>`}</select>
        <button class="btn sm" data-act="save-tiles" id="lp-save-tiles" disabled>${icon('send')}保存磁贴位置</button></div>
      <div class="card-body"><div id="lp"></div>
        <p class="faint" style="font-size:12.5px;margin-top:10px">拖动分隔线调整分屏比例，拖动磁贴移动、拖动右下角调整大小（按网格吸附）；自由布局下可以拖动组件。示意图只表现大致效果，以大屏为准。
          <span>${layout.revision ? `修订 ${layout.revision} · ${layout.updatedBy || ''} · ${relTime(layout.updatedAt)}` : '大屏尚未上传设置'}</span></p></div>
    </section>
    <div class="stack">
      <section class="card">
        <div class="card-head"><h3 class="grow">同步策略</h3></div>
        <div class="card-body stack">${policies.map(([k, l, d]) => html`<label class="policy ${layout.policy === k ? 'active' : ''}"><input type="radio" name="policy" value="${k}" ${layout.policy === k ? 'checked' : ''} ${canPolicy ? '' : 'disabled'}><span><b>${l}</b><small>${d}</small></span></label>`)}
          ${canPolicy ? '' : html`<small class="faint">同步策略由管理员或班主任设置。</small>`}</div>
      </section>
      <section class="card">
        <div class="card-head"><h3 class="grow">历史与复制</h3><button class="btn sm" data-act="copy-layout" ${layout.revision ? '' : 'disabled'}>${icon('copy')}复制到其他班级</button></div>
        ${history.length ? html`<div class="list-rows">${history.slice(0, 8).map((h, i) => html`<div class="list-row"><span class="grow"><span class="title" style="display:block">修订 ${h.revision}${i === 0 ? html` <span class="badge ok">当前</span>` : ''}</span><span class="meta">${h.author} · ${relTime(h.createdAt)}</span></span>
          ${i === 0 ? '' : html`<button class="btn ghost sm" data-act="restore-layout" data-rev="${h.revision}">${icon('restore')}恢复</button>`}</div>`)}</div>`
          : html`<div class="card-body faint" style="font-size:13px">大屏连接集控后会自动上传当前设置；也可以直接在这里修改后下发。</div>`}
      </section>
    </div>
  </div>
  <div class="set-groups" id="sg">${groups.map((g, gi) => html`<details class="card set-group" data-group="${g}" ${gi < 3 ? 'open' : ''}>
    <summary><span class="grow">${g}</span><span class="badge warn" data-dirty="${g}" hidden>未保存</span></summary>
    <div class="card-body set-grid">${g === '天气' ? html`<label class="field"><span>天气地区</span><input class="input" list="wc" id="weather-city" value="${current('WeatherCityName')}" placeholder="搜索城市" autocomplete="off"><datalist id="wc"></datalist><small>从列表中选择后生效</small></label>` : ''}
      ${schema.settings.filter(s => s.group === g && !s.hidden).map(s => settingControl(s, current, fonts, assetUrl))}</div>
  </details>`)}</div>
  <div class="savebar" id="savebar" hidden><span class="grow" id="sb-text"></span><button class="btn" data-act="discard-settings">撤销修改</button><button class="btn primary" data-act="save-settings">${icon('send')}保存并下发</button></div>`);

  const layoutEditor = mountLayoutEditor($('#lp'), editor, kind => {
    if (kind === 'split') setValue('SplitRatio', editor.doc.SplitRatio);
    if (kind === 'widget') setValue('Widgets', clone(editor.widgets));
    if (kind === 'split') { const r = $('[data-k=SplitRatio]'); if (r) { r.value = editor.doc.SplitRatio; $('[data-out=SplitRatio]').textContent = Math.round(editor.doc.SplitRatio * 100) + '%'; } }
    $('#lp-save-tiles').disabled = !editor.tiles.some(t => t.moved);
    refresh();
  });
  function refresh() {
    editor.doc = merged();
    layoutEditor.draw();
    const keys = Object.keys(draft);
    for (const g of groups) { const el = $(`[data-dirty="${CSS.escape(g)}"]`); if (el) el.hidden = !keys.some(k => byKey[k]?.group === g); }
    $('#savebar').hidden = keys.length === 0;
    $('#sb-text').textContent = `有 ${keys.length} 项设置未保存：${keys.map(k => byKey[k]?.label || k).slice(0, 6).join('、')}${keys.length > 6 ? ' 等' : ''}`;
  }
  const rerenderGroup = g => {
    const body = $(`[data-group="${CSS.escape(g)}"] .set-grid`);
    if (body) body.innerHTML = show(schema.settings.filter(s => s.group === g && !s.hidden).map(s => settingControl(s, current, fonts, assetUrl)));
  };

  const sg = $('#sg');
  sg.addEventListener('input', e => {
    const el = e.target;
    if (el.dataset.k) {
      const s = byKey[el.dataset.k];
      const v = s.kind === 'Bool' ? el.checked : s.kind === 'Number' ? +el.value : el.value;
      setValue(s.key, v);
      const out = $(`[data-out="${CSS.escape(s.key)}"]`);
      if (out) out.textContent = s.min >= 0 && s.max <= 1 ? Math.round(v * 100) + '%' : +(+v).toFixed(2);
    } else if (el.closest('[data-color]')) {
      const box = el.closest('[data-color]'), s = byKey[box.dataset.color];
      const auto = $('[data-c=auto]', box)?.checked, cleared = $('[data-c=cleared]', box)?.checked;
      if (s.clearedKey) setValue(s.clearedKey, !!cleared);
      if (auto) setValue(s.key, '');
      else {
        const rgb = $('[data-c=rgb]', box).value.slice(1).toUpperCase(), alphaEl = $('[data-c=alpha]', box);
        setValue(s.key, alphaEl ? '#' + (+alphaEl.value).toString(16).padStart(2, '0').toUpperCase() + rgb : '#' + rgb);
        if (alphaEl) alphaEl.nextElementSibling.textContent = Math.round(alphaEl.value / 2.55) + '%';
      }
      if (e.type === 'change' || el.type === 'checkbox') rerenderGroup(s.group);
    } else if (el.closest('[data-bg]') && el.dataset.bf) {
      const key = el.closest('[data-bg]').dataset.bg, b = { ...(clone(current(key)) || {}) }, f = el.dataset.bf;
      if (f === 'ColorAuto') b.Color = el.checked ? '' : (b.Color || '#202024');
      else if (f === 'Color') b.Color = el.value.toUpperCase();
      else if (el.type === 'checkbox') b[f] = el.checked;
      else if (el.type === 'range' || el.type === 'number') b[f] = +el.value;
      else b[f] = el.value;
      setValue(key, b);
      if (el.type === 'checkbox') rerenderGroup(byKey[key].group);
      else if (el.type === 'range') el.previousElementSibling.querySelector('b').textContent = f === 'ColorOpacity' ? Math.round(+el.value * 100) + '%' : Math.round(+el.value);
    }
    refresh();
  });
  sg.addEventListener('change', async e => {
    if (e.target.id !== 'weather-city') return;
    const city = (await loadWeatherCities()).find(x => x.Name === e.target.value.trim());
    if (!city) return toast('请从列表中选择地区', 'error');
    setValue('WeatherCityName', city.Name); setValue('WeatherCityCode', city.Code); refresh();
  });
  sg.addEventListener('focusin', async e => {
    if (e.target.id !== 'weather-city' || $('#wc').childElementCount) return;
    $('#wc').innerHTML = (await loadWeatherCities()).map(x => `<option value="${esc(x.Name)}">`).join('');
  });
  const uploadMedia = multiple => new Promise(resolve => {
    const input = document.createElement('input');
    input.type = 'file'; input.accept = 'image/*,video/*'; input.multiple = multiple;
    input.onchange = async () => {
      const refs = [];
      for (const file of input.files) {
        try {
          const r = await fetch(`/api/classes/${c.id}/assets?name=${encodeURIComponent(file.name)}`, { method: 'POST', headers: { authorization: 'Bearer ' + state.token }, body: file });
          if (!r.ok) throw new Error((await r.text().catch(() => '')).replace(/^"|"$/g, '') || '上传失败');
          refs.push((await r.json()).reference);
        } catch (err) { toast(`“${file.name}”上传失败：${err.message}`, 'error'); }
      }
      resolve(refs);
    };
    input.click();
  });
  sg.addEventListener('click', async e => {
    const btn = e.target.closest('[data-bg-act]'); if (!btn) return;
    const key = btn.closest('[data-bg]').dataset.bg, b = { ...(clone(current(key)) || {}) };
    const act = btn.dataset.bgAct;
    if (act === 'pick') { const [ref] = await uploadMedia(false); if (!ref) return; b.ImagePath = ref; }
    if (act === 'clear') b.ImagePath = '';
    if (act === 'add-items') { const refs = await uploadMedia(true); if (!refs.length) return; b.Playlist = [...(b.Playlist || []), ...refs]; b.PlaylistEnabled = true; }
    if (act === 'del-item') { b.Playlist = (b.Playlist || []).filter((_, i) => i !== +btn.dataset.i); }
    setValue(key, b); rerenderGroup(byKey[key].group); refresh();
  });
  $('#lp-project')?.addEventListener('change', async e => {
    if (editor.tiles.some(t => t.moved) && !await confirmDialog({ title: '放弃磁贴改动', message: '当前项目的磁贴位置还没有保存，确定切换吗？', confirm: '切换' })) { e.target.value = tileProject.id; return; }
    tileProject = live.find(p => p.id === e.target.value) || null;
    await loadTiles(); $('#lp-save-tiles').disabled = true; refresh();
  });

  $$('input[name=policy]').forEach(r => r.onchange = async () => {
    try { await api(`/api/classes/${c.id}/layout/policy`, { method: 'PUT', body: { policy: r.value } }); toast('同步策略已更新'); route(); } catch (err) { fail(err); }
  });
  // 其他大屏或网页修改了设置：没有未保存的修改时直接刷新，否则提醒。
  let remoteTimer;
  onDispose(liveSocket(`/ws/classes/${c.id}?access_token=${encodeURIComponent(state.token)}`, ev => {
    if (ev.type !== 'layout') return;
    clearTimeout(remoteTimer);
    remoteTimer = setTimeout(() => {
      if (!Object.keys(draft).length && !editor.tiles.some(t => t.moved)) route();
      else toast('班级设置刚在其他地方被修改，保存时会自动合并不冲突的部分', 'error');
    }, 500);
  }));
  onDispose(() => clearTimeout(remoteTimer));

  Object.assign(viewActions, {
    'discard-settings': () => { for (const k of Object.keys(draft)) delete draft[k]; editor.widgets = clone(current('Widgets')) || {}; groups.forEach(rerenderGroup); refresh(); },
    'save-settings': async el => {
      if (layout.policy === 'Off' && !await confirmDialog({ title: '布局同步已关闭', message: '本班当前设置为“不同步”，保存的设置不会下发到大屏。仍要保存吗？', confirm: '保存', danger: false })) return;
      el.disabled = true;
      try {
        await api(`/api/classes/${c.id}/layout`, { method: 'PATCH', body: { baseRevision: layout.revision, set: draft } });
        toast('设置已保存，在线大屏正在应用'); route();
      } catch (err) {
        el.disabled = false;
        if (err.status === 409) { toast('部分设置刚被大屏修改，已刷新为最新版本，请重新调整', 'error'); route(); }
        else fail(err);
      }
    },
    'save-tiles': async el => {
      if (!tileDetail) return;
      el.disabled = true;
      const moved = new Map(editor.tiles.filter(t => t.moved).map(t => [t.index, t]));
      const body = {
        expectedRevision: tileDetail.revision,
        subjects: parseProject(tileDetail.payloadJson, tileDetail.runs).map(s => {
          const t = moved.get(s.index);
          return {
            index: s.index, name: s.name, accentHex: null,
            entries: s.entries.map(en => ({ index: en.index, content: en.text, runs: en.runs })),
            rect: t ? { x: t.x, y: t.y, width: t.w, height: t.h } : null
          };
        })
      };
      try { await api(`/api/classes/${c.id}/projects/${tileProject.id}/content`, { method: 'PUT', body }); toast('磁贴位置已保存，大屏正在同步'); await loadTiles(); refresh(); }
      catch (err) { el.disabled = false; if (err.status === 409) { toast('这个项目刚被修改，已重新载入', 'error'); await loadTiles(); refresh(); } else fail(err); }
    },
    'restore-layout': async el => {
      if (!await confirmDialog({ title: '恢复设置', message: `把班级设置恢复为修订 ${el.dataset.rev}？会生成新的修订并下发到大屏。`, confirm: '恢复', danger: false })) return;
      await api(`/api/classes/${c.id}/layout/restore`, { method: 'POST', body: { revision: +el.dataset.rev } }); toast('设置已恢复'); route();
    },
    'copy-layout': async () => {
      const ids = await checklistModal({ title: '复制设置到其他班级', submit: '复制', hint: '目标班级的大屏会改用本班的布局与外观设置（含背景图片）。', items: classes.filter(x => x.id !== c.id).map(x => ({ value: x.id, label: x.name })), emptyText: '没有其他可管理的班级。' });
      if (!ids?.length) return;
      const r = await api(`/api/classes/${c.id}/layout/copy`, { method: 'POST', body: { classIds: ids } });
      toast(`已复制到 ${r.copied} 个班级`);
    }
  });
  refresh();
}

function classSettings(box, c, publicUrl) {
  box.innerHTML = show(html`<div class="stack" style="max-width:720px">
    <section class="card"><div class="card-head"><h3>公开访问</h3></div><div class="card-body stack">
      <p class="muted">学生和家长可以通过公开短码只读查看本班作业，无需登录。</p>
      <div class="row wrap"><span class="code-chip" style="font-size:16px;padding:6px 12px">${c.publicCode}</span>
        <button class="btn sm" data-act="copy" data-text="${publicUrl}" data-label="公开链接已复制">${icon('link')}复制链接</button>
        <a class="btn sm" href="#/c/${c.publicCode}" target="_blank">${icon('eye')}预览</a>
        ${isAdmin() ? html`<button class="btn sm" data-act="reset-code">${icon('refresh')}重置短码</button>` : ''}</div>
    </div></section>
    <section class="card"><div class="card-head"><h3>数据</h3></div><div class="card-body row wrap">
      <p class="muted grow">导出本班全部项目（含作业内容）为 JSON 文件，用于备份或迁移。</p>
      <button class="btn" data-act="export">${icon('download')}导出 JSON</button>
    </div></section>
    ${isAdmin() ? html`
    <section class="card"><div class="card-head"><h3>班级名称</h3></div><div class="card-body row">
      <input class="input grow" id="cn" value="${c.name}" maxlength="60"><button class="btn primary" data-act="rename">保存</button>
    </div></section>
    <section class="card" style="border-color:color-mix(in srgb,var(--danger) 35%,var(--border))"><div class="card-head"><h3 style="color:var(--danger)">删除班级</h3></div><div class="card-body row wrap">
      <p class="muted grow">删除后班级从列表隐藏，公开短码和设备同步立即失效。</p>
      <button class="btn danger" data-act="delete-class">${icon('trash')}删除班级</button>
    </div></section>` : ''}
  </div>`);
  Object.assign(viewActions, {
    export: () => download(`/api/classes/${c.id}/export`),
    'reset-code': async () => {
      if (!await confirmDialog({ title: '重置公开短码', message: '旧短码和已分享的链接会立即失效。确定重置吗？', confirm: '重置' })) return;
      const r = await api(`/api/admin/classes/${c.id}/public-code`, { method: 'POST' }); toast('新短码：' + r.publicCode); route();
    },
    rename: async () => { await api(`/api/admin/classes/${c.id}`, { method: 'PATCH', body: { name: $('#cn').value.trim() } }); toast('班级名称已保存'); route(); },
    'delete-class': async () => {
      const ok = await modal({
        title: '删除班级', submit: '永久删除', danger: true,
        body: html`<p class="muted">此操作会让本班所有设备停止同步。请输入班级名称 <b>${c.name}</b> 确认：</p><input class="input" name="confirm" autocomplete="off">`,
        onSubmit: f => { if (f.confirm.value.trim() !== c.name) throw new Error('输入的名称不一致'); return api(`/api/admin/classes/${c.id}`, { method: 'DELETE' }); }
      });
      if (ok) { toast('班级已删除'); navigate('classes'); }
    }
  });
}

// ============================================================ 作业浏览
async function viewHomework(classId, projectId, query) {
  if (classId instanceof URLSearchParams) { query = classId; classId = undefined; }
  if (projectId instanceof URLSearchParams) { query = projectId; projectId = undefined; }
  const classes = await api('/api/classes');
  if (!classId && classes.length) {
    const last = store.get('lastClass');
    const busiest = [...classes].sort((a, b) => (parseTime(b.lastUpdatedAt) || 0) - (parseTime(a.lastUpdatedAt) || 0))[0];
    classId = classes.some(c => c.id === last) ? last : busiest.id;
    history.replaceState(null, '', `#/homework/${classId}`);
  }
  const current = classes.find(c => c.id === classId);
  const page = shell({
    nav: 'homework', title: '作业',
    actions: classes.length ? html`<select class="input" id="hc" style="width:220px" aria-label="选择班级">${classes.map(c => html`<option value="${c.id}" ${c.id === classId ? 'selected' : ''}>${c.name}</option>`)}</select>
      ${current ? html`<a class="btn" href="#/classes/${current.id}">${icon('class')}班级详情</a>` : ''}` : ''
  });
  page.classList.add('wide');
  if (!classes.length) return setPage(page, html`<div class="card">${empty('class', '还没有班级', '创建或分配班级后可以在这里查看作业。')}</div>`);
  if (!current) return setPage(page, html`<div class="card">${empty('alert', '班级不存在', '该班级可能已被删除，或你没有访问权限。')}</div>`);
  store.set('lastClass', classId);
  $('#hc').onchange = e => navigate('homework/' + e.target.value);
  await mountBrowser(page, {
    list: () => api(`/api/classes/${classId}/projects`),
    detail: id => api(`/api/classes/${classId}/projects/${id}`),
    link: id => `homework/${classId}/${id}`,
    asset: consoleAsset(classId),
    manage: true, classId,
    ws: `/ws/classes/${classId}?access_token=${encodeURIComponent(state.token)}`
  }, projectId);
  if (query?.get('new')) { history.replaceState(null, '', `#/homework/${classId}`); viewActions['p-new']?.(); }
}

// ============================================================ 账号管理
async function viewUsers() {
  const page = shell({ nav: 'users', title: '账号', actions: html`<button class="btn primary" data-act="new-user">${icon('plus')}新建账号</button>` });
  setPage(page, loading());
  const [users, classes] = await Promise.all([api('/api/admin/users'), api('/api/classes')]);
  const find = id => users.find(u => u.id === id);
  const classChecklist = selected => classes.length
    ? html`<div class="checklist">${classes.map(c => html`<label class="check"><input type="checkbox" name="cls" value="${c.id}" ${selected.includes(c.id) ? 'checked' : ''}>${c.name}</label>`)}</div>`
    : html`<small class="faint">还没有班级。</small>`;
  const checked = f => $$('input[name=cls]:checked', f).map(x => x.value);

  setPage(page, html`
    <div class="toolbar"><label class="input-icon">${icon('search', 'sm')}<input class="input" id="us" placeholder="搜索姓名或账号"></label><span class="faint">${users.length} 个账号</span></div>
    <div class="card"><div class="table-wrap"><table class="table">
      <thead><tr><th>用户</th><th>角色</th><th class="hide-sm">负责班级</th><th class="hide-sm">最近登录</th><th></th></tr></thead>
      <tbody id="ub"></tbody></table></div></div>`);
  const renderRows = q => $('#ub').innerHTML = show(users.filter(u => !q || u.displayName.toLowerCase().includes(q) || u.userName.toLowerCase().includes(q)).map(u => html`
    <tr class="${u.enabled ? '' : 'dim'}">
      <td><div class="who">${avatar(u.displayName, 'sm')}<div><b>${u.displayName}${u.id === state.me.id ? html` <span class="badge accent">我</span>` : ''}</b><span>${u.userName}</span></div></div></td>
      <td><span class="badge ${u.role === 'Administrator' ? 'info' : ''}">${roleLabel(u.role)}</span>${u.enabled ? '' : html` <span class="badge danger">已停用</span>`}</td>
      <td class="hide-sm">${u.role === 'Administrator' ? html`<span class="faint">全部班级</span>` : u.classes.length ? u.classes.map(c => html`<span class="badge" style="margin:2px 4px 2px 0">${c.name}</span>`) : html`<span class="faint">未分配</span>`}</td>
      <td class="hide-sm" title="${fullTime(u.lastLoginAt)}">${relTime(u.lastLoginAt)}</td>
      <td class="actions">
        <button class="btn ghost sm" data-act="edit-user" data-id="${u.id}">${icon('edit')}编辑</button>
        <button class="btn ghost sm" data-act="reset-pass" data-id="${u.id}">${icon('key')}重置密码</button>
        ${u.id === state.me.id ? '' : html`<button class="btn ghost sm icon-only danger" data-act="delete-user" data-id="${u.id}" title="删除">${icon('trash')}</button>`}
      </td></tr>`));
  renderRows('');
  $('#us').oninput = e => renderRows(e.target.value.trim().toLowerCase());

  Object.assign(viewActions, {
    'new-user': async () => {
      const created = await modal({
        title: '新建账号', submit: '创建', wide: true,
        body: html`<div class="grid-2" style="gap:14px">
            <label class="field"><span>账号</span><input class="input" name="userName" placeholder="登录名，不含空格" maxlength="32" autocomplete="off"></label>
            <label class="field"><span>显示名称</span><input class="input" name="displayName" placeholder="例如：王老师" maxlength="40"></label>
          </div>
          <label class="field"><span>初始密码</span><div class="row"><input class="input grow mono" name="password" value="${randomPassword()}" minlength="8"><button type="button" class="btn" id="gen">${icon('refresh')}生成</button><button type="button" class="btn icon-only" id="cp" title="复制">${icon('copy')}</button></div><small>至少 8 位。请把初始密码告知对方，登录后可自行修改。</small></label>
          <label class="field"><span>角色</span><select class="input" name="role"><option value="Teacher">老师 — 管理被分配的班级</option><option value="Administrator">管理员 — 管理全部班级和账号</option></select></label>
          <div class="field" id="cls-field"><span>负责班级</span>${classChecklist([])}</div>`,
        onMount: f => {
          $('#gen', f).onclick = () => { f.password.value = randomPassword(); };
          $('#cp', f).onclick = () => copyText(f.password.value, '密码已复制');
          f.role.onchange = () => $('#cls-field', f).classList.toggle('hidden', f.role.value !== 'Teacher');
        },
        onSubmit: async f => {
          const body = { userName: f.userName.value.trim(), displayName: f.displayName.value.trim(), password: f.password.value, role: f.role.value };
          const u = await api('/api/admin/users', { method: 'POST', body });
          const cls = checked(f);
          if (body.role === 'Teacher' && cls.length) await api(`/api/admin/users/${u.id}/classes`, { method: 'PUT', body: { classIds: cls } });
          return u;
        }
      });
      if (created) { toast(`账号 ${created.userName} 已创建`); route(); }
    },
    'edit-user': async el => {
      const u = find(el.dataset.id), self = u.id === state.me.id;
      const ok = await modal({
        title: '编辑账号', submit: '保存', wide: true,
        body: html`<div class="grid-2" style="gap:14px">
            <label class="field"><span>账号</span><input class="input" value="${u.userName}" readonly disabled></label>
            <label class="field"><span>显示名称</span><input class="input" name="displayName" value="${u.displayName}" maxlength="40"></label>
          </div>
          <label class="field"><span>角色</span><select class="input" name="role" ${self ? 'disabled' : ''}><option value="Teacher" ${u.role === 'Teacher' ? 'selected' : ''}>老师</option><option value="Administrator" ${u.role === 'Administrator' ? 'selected' : ''}>管理员</option></select>${self ? html`<small>不能修改自己的角色。</small>` : ''}</label>
          <label class="check"><input type="checkbox" name="enabled" ${u.enabled ? 'checked' : ''} ${self ? 'disabled' : ''}>允许登录</label>
          <div class="field ${u.role === 'Teacher' ? '' : 'hidden'}" id="cls-field"><span>负责班级</span>${classChecklist(u.classes.map(c => c.id))}</div>`,
        onMount: f => { f.role.onchange = () => $('#cls-field', f).classList.toggle('hidden', f.role.value !== 'Teacher'); },
        onSubmit: async f => {
          const body = { displayName: f.displayName.value.trim() };
          if (!self) { body.role = f.role.value; body.enabled = f.enabled.checked; }
          await api(`/api/admin/users/${u.id}`, { method: 'PATCH', body });
          if ((body.role || u.role) === 'Teacher') await api(`/api/admin/users/${u.id}/classes`, { method: 'PUT', body: { classIds: checked(f) } });
        }
      });
      if (ok) {
        toast('账号已更新');
        if (self) { state.me = null; await ensureMe(); }
        route();
      }
    },
    'reset-pass': async el => {
      const u = find(el.dataset.id);
      const ok = await modal({
        title: '重置密码', submit: '重置',
        body: html`<p class="muted">为 <b>${u.displayName}</b> 设置新密码，对方已登录的会话将失效。</p>
          <label class="field"><span>新密码</span><div class="row"><input class="input grow mono" name="p" value="${randomPassword()}"><button type="button" class="btn icon-only" id="cp" title="复制">${icon('copy')}</button></div><small>至少 8 位。</small></label>`,
        onMount: f => { $('#cp', f).onclick = () => copyText(f.p.value, '密码已复制'); },
        onSubmit: f => api(`/api/admin/users/${u.id}/password`, { method: 'POST', body: { newPassword: f.p.value } })
      });
      if (ok) toast('密码已重置');
    },
    'delete-user': async el => {
      const u = find(el.dataset.id);
      if (!await confirmDialog({ title: '删除账号', message: `确定删除“${u.displayName}（${u.userName}）”吗？此操作不可撤销，已配对的设备不受影响。`, confirm: '删除' })) return;
      await api(`/api/admin/users/${u.id}`, { method: 'DELETE' }); toast('账号已删除'); route();
    }
  });
}

// ============================================================ 操作日志
async function viewAudit() {
  const page = shell({ nav: 'audit', title: '操作日志' });
  const classes = await api('/api/classes');
  let items = [], q = '', classId = '', done = false;
  setPage(page, html`
    <div class="toolbar">
      <label class="input-icon">${icon('search', 'sm')}<input class="input" id="aq" placeholder="搜索操作者、动作或内容"></label>
      <select class="input" id="ac" style="width:200px"><option value="">全部班级</option>${classes.map(c => html`<option value="${c.id}">${c.name}</option>`)}</select>
    </div>
    <div class="card"><div class="table-wrap"><table class="table"><thead><tr><th>时间</th><th>操作者</th><th>动作</th><th>内容</th><th class="hide-sm">班级</th></tr></thead><tbody id="ab"></tbody></table></div>
    <div class="card-body row" style="justify-content:center;border-top:1px solid var(--border)"><button class="btn" data-act="more" id="am">加载更多</button></div></div>`);
  const tone = a => /删除|吊销|停用|失败/.test(a) ? 'danger' : /创建|新建|恢复|回滚/.test(a) ? 'ok' : /登录/.test(a) ? '' : 'info';
  const render = () => {
    $('#ab').innerHTML = show(items.length ? items.map(a => html`<tr>
      <td class="nowrap" title="${fullTime(a.at)}">${fullTime(a.at)}</td><td>${a.actor}</td>
      <td><span class="badge ${tone(a.action)}">${a.action}</span></td><td>${a.detail}</td><td class="hide-sm">${a.className || html`<span class="faint">—</span>`}</td></tr>`)
      : html`<tr><td colspan="5">${empty('log', '没有记录', q || classId ? '没有符合条件的记录。' : '系统操作会记录在这里。')}</td></tr>`);
    $('#am').classList.toggle('hidden', done);
  };
  const load = async (reset) => {
    if (reset) { items = []; done = false; }
    const params = new URLSearchParams({ take: 100 });
    if (q) params.set('q', q); if (classId) params.set('classId', classId);
    if (items.length) params.set('before', items[items.length - 1].id);
    const batch = await api('/api/admin/audit?' + params);
    items.push(...batch); done = batch.length < 100; render();
  };
  let t;
  $('#aq').oninput = e => { clearTimeout(t); t = setTimeout(() => { q = e.target.value.trim(); load(true).catch(fail); }, 300); };
  $('#ac').onchange = e => { classId = e.target.value; load(true).catch(fail); };
  viewActions.more = () => load(false);
  await load(true);
}

// ============================================================ 公告
async function viewAnnouncements() {
  const page = shell({ nav: 'announcements', title: '公告', actions: html`<button class="btn primary" data-act="broadcast">${icon('megaphone')}发布公告</button>` });
  setPage(page, loading());
  const [items, classes] = await Promise.all([api('/api/announcements'), api('/api/classes')]);
  let classId = '', onlyActive = false;
  const render = () => {
    const rows = items.filter(a => (!classId || a.classId === classId) && (!onlyActive || a.active));
    $('#al').innerHTML = show(rows.length ? html`<div class="stack">${rows.map(a => announcementCard(a, { showClass: true }))}</div>` : html`<div class="card">${empty('megaphone', '没有公告', items.length ? '没有符合条件的公告。' : '发布公告后会实时显示在班级大屏顶部。')}</div>`);
  };
  setPage(page, html`<div class="toolbar">
      <select class="input" id="ac" style="width:220px"><option value="">全部班级</option>${classes.map(x => html`<option value="${x.id}">${x.name}</option>`)}</select>
      <label class="check"><input type="checkbox" id="aa">只看生效中</label><span class="spacer"></span><span class="faint">${items.length} 条</span></div>
    <div id="al"></div>`);
  render();
  $('#ac').onchange = e => { classId = e.target.value; render(); };
  $('#aa').onchange = e => { onlyActive = e.target.checked; render(); };
  const find = id => items.find(a => a.id === id);
  Object.assign(viewActions, {
    broadcast: async () => {
      if (!classes.length) return toast('还没有可管理的班级', 'error');
      const r = await modal({
        title: '发布公告', submit: '发布', wide: true,
        body: html`${announcementForm()}<div class="field"><span>发送到</span><div class="checklist">${classes.map(x => html`<label class="check"><input type="checkbox" name="cls" value="${x.id}" ${classes.length === 1 ? 'checked' : ''}>${x.name}</label>`)}</div>
          <div class="row"><button type="button" class="btn ghost sm" id="ball">全选</button></div></div>`,
        onMount: f => { $('#ball', f).onclick = () => $$('input[name=cls]', f).forEach(x => x.checked = true); },
        onSubmit: f => {
          const ids = $$('input[name=cls]:checked', f).map(x => x.value);
          if (!ids.length) throw new Error('请选择班级');
          return api('/api/announcements/broadcast', { method: 'POST', body: { classIds: ids, ...announcementBody(f) } });
        }
      });
      if (r) { toast(`已发布到 ${r.count} 个班级`); route(); }
    },
    'edit-notice': async el => { const a = find(el.dataset.id); if (await modal({ title: '编辑公告', submit: '保存', wide: true, body: announcementForm(a), onSubmit: f => api(`/api/classes/${a.classId}/announcements/${a.id}`, { method: 'PUT', body: announcementBody(f) }) })) { toast('公告已更新'); route(); } },
    'del-notice': async el => { const a = find(el.dataset.id); if (!await confirmDialog({ title: '删除公告', message: `删除“${a.title}”（${a.className}）？`, confirm: '删除' })) return; await api(`/api/classes/${a.classId}/announcements/${a.id}`, { method: 'DELETE' }); toast('已删除'); route(); }
  });
}

// ============================================================ 设置
async function viewSettings() {
  const page = shell({ nav: 'settings', title: '设置' });
  const me = state.me;
  const current = theme.get();
  setPage(page, html`<div class="stack" style="max-width:760px">
    <section class="card"><div class="card-head">${avatar(me.displayName)}<div class="grow"><h3>${me.displayName}</h3><span class="faint" style="font-size:12.5px">${me.userName} · ${roleLabel(me.role)}</span></div></div>
      <form class="card-body stack" id="pw" novalidate>
        <h4>修改密码</h4>
        <label class="field"><span>当前密码</span><input class="input" type="password" name="cur" autocomplete="current-password"></label>
        <div class="grid-2" style="gap:14px">
          <label class="field"><span>新密码</span><input class="input" type="password" name="next" autocomplete="new-password"><small>至少 8 位，不要与账号相同。</small></label>
          <label class="field"><span>确认新密码</span><input class="input" type="password" name="again" autocomplete="new-password"></label>
        </div>
        <div class="form-error" id="pwe"></div>
        <div><button class="btn primary" type="submit">更新密码</button></div>
      </form>
    </section>
    <section class="card"><div class="card-head"><h3 class="grow">外观</h3></div><div class="card-body row wrap">
      <span class="muted grow">界面主题</span>
      <div class="segmented" id="th">${[['system', '跟随系统'], ['light', '浅色'], ['dark', '深色']].map(([k, l]) => html`<button type="button" data-v="${k}" class="${current === k ? 'active' : ''}">${l}</button>`)}</div>
    </div></section>
    <section class="card"><div class="card-head"><h3 class="grow">服务器</h3><button class="btn ghost sm" data-act="copy" data-text="${location.origin}">${icon('copy')}复制地址</button></div><div class="card-body" id="srv">${loading()}</div></section>
    ${isAdmin() ? html`<section class="card"><div class="card-head"><h3>备份</h3></div><div class="card-body row wrap">
      <p class="muted grow">下载完整的 SQLite 数据库副本（班级、账号、项目及历史）。服务运行时即可安全备份。</p>
      <button class="btn" data-act="backup">${icon('db')}下载数据库备份</button></div></section>` : ''}
  </div>`);
  $$('#th button').forEach(b => b.onclick = () => { theme.set(b.dataset.v); $$('#th button').forEach(x => x.classList.toggle('active', x === b)); });
  const f = $('#pw');
  f.onsubmit = async e => {
    e.preventDefault(); $('#pwe').textContent = '';
    if (f.next.value !== f.again.value) { $('#pwe').textContent = '两次输入的新密码不一致'; return; }
    try {
      await api('/api/auth/password', { method: 'POST', body: { currentPassword: f.cur.value, newPassword: f.next.value } });
      f.reset(); state.me = null; await ensureMe(); toast('密码已更新，其他设备上的登录已失效');
    } catch (err) { $('#pwe').textContent = err.message; }
  };
  viewActions.backup = async () => { await download('/api/admin/backup'); toast('备份已开始下载'); };
  try {
    const h = await api('/api/health', { auth: false });
    $('#srv').innerHTML = show(html`<dl class="kv">
      <dt>地址</dt><dd class="mono">${location.origin}</dd>
      <dt>状态</dt><dd><span class="badge ok"><span class="dot ok"></span>运行中</span></dd>
      <dt>版本</dt><dd>${h.version || '—'}</dd>
      <dt>已运行</dt><dd>${duration(h.uptimeSeconds)}（自 ${fullTime(h.startedAt)}）</dd>
      <dt>实时连接</dt><dd>${h.connections}</dd>
      <dt>登录会话</dt><dd>${h.sessions}</dd>
      <dt>本次登录</dt><dd>有效期至 ${fullTime(me.expiresAt)}</dd>
    </dl>`);
  } catch (err) { $('#srv').innerHTML = show(empty('alert', '无法获取服务器状态', err.message)); }
}

// ============================================================ 启动
(function start() {
  const code = new URLSearchParams(location.search).get('code');
  if (code) { history.replaceState(null, '', location.pathname + '#/c/' + encodeURIComponent(code.trim().toLowerCase())); }
  route();
})();

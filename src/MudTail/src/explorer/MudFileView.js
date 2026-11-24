// MudFileView: the items of a folder, as Windows 11's Explorer shows them.
//   views:      'details' (sortable, resizable columns), 'list', 'tiles', 'smallIcons', 'icons' (medium), 'largeIcons'
//   selection:  click, Ctrl+click, Shift+click, a marquee from the empty space, Ctrl+A; arrow keys, Home / End,
//               Ctrl+arrows (focus only), Ctrl+Space, type-ahead; right-click keeps a selection it lands in
//   rename:     F2 or the host's rename(): in place, the name's stem selected, Windows' naming rules
//   drag:       items onto a folder (move; Ctrl copies), files from the desktop onto a folder or the view
//   groups:     by type, date modified or kind, with collapsible headers; folders first in every sort
// It is mirrored in RTL (arrow keys too). It raises events; the shell (MudExplorer) or the host does the work.
//
//   const view = new MudFileView({
//     items: [{ id, name, kind: 'folder' | 'file', size, modified, contentType, thumbnail, hidden, cut }],
//     view: 'details', sort: { key: 'name', direction: 'asc' },
//     onOpen: (item) => …, onSelectionChange: (items) => …, onContextMenu: ({ x, y, items, keyboard }) => …,
//     onRename: async (item, name) => true | 'error message', onDrop: ({ ids, target, copy }) => …,
//     onDropFiles: ({ files, target }) => …, onSortChange: (sort) => …, onKey: (command, e) => …,
//   });

import { t, formatNumber, getLocale } from '../core/i18n.js';
import { getIconSvg } from '../core/icons.js';
import {
  sortItems, groupItems, SelectionModel, typeAhead, formatSize, formatModified, typeLabel, isFolder, nameError,
  renameSelection,
} from './model.js';
import { itemIconSvg } from './icons.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const DRAG_TYPE = 'application/x-mudtail-items';
const ICON_SIZE = { details: 20, list: 16, smallIcons: 16, tiles: 48, icons: 48, largeIcons: 96 };

/** The default Details columns; add your own: { key, title, width, align, format(item) }. */
export const DEFAULT_COLUMNS = () => [
  { key: 'name', title: t('explorer.col.name', {}, 'Name'), width: 300, min: 120 },
  { key: 'modified', title: t('explorer.col.modified', {}, 'Date modified'), width: 170, format: (i) => formatModified(i.modified) },
  { key: 'type', title: t('explorer.col.type', {}, 'Type'), width: 140, format: (i) => typeLabel(i) },
  { key: 'size', title: t('explorer.col.size', {}, 'Size'), width: 120, align: 'end', format: (i) => (isFolder(i) ? '' : formatSize(i.size, { column: true })) },
];

export class MudFileView {
  constructor(o = {}) {
    this.o = { view: 'details', sort: { key: 'name', direction: 'asc' }, groupBy: null, ...o };
    this.columns = o.columns ?? DEFAULT_COLUMNS();
    this.widths = Object.fromEntries(this.columns.map((c) => [c.key, c.width ?? 140]));
    this.raw = o.items ?? [];
    this.selection = new SelectionModel();
    this.collapsed = new Set();
    this.cut = new Set();
    this.typed = '';
    this.el = document.createElement('div');
    this.el.className = 'mx-files';
    this.el.tabIndex = 0;
    this.el.setAttribute('role', 'grid');
    this.el.setAttribute('aria-multiselectable', 'true');
    this.el.setAttribute('aria-label', o.label ?? t('explorer.items', {}, 'Items'));
    this._wire();
    this._compute();
    this.render();
  }

  // ───────── public API ─────────

  getElement() { return this.el; }

  /** New items (a refresh keeps the selection of the ids still there; `select` picks ids instead). */
  setItems(items, { select = null, keepScroll = false } = {}) {
    const top = this.el.scrollTop;
    this.raw = items ?? [];
    this.loading = false;
    this.error = null;
    this._compute();
    if (select) this.selection.set(select.map(String));
    this.render();
    if (keepScroll) this.el.scrollTop = top;
    else this.el.scrollTop = 0;
    if (select?.length) this._scrollIntoView(this.selection.focus);
    this._emitSelection();
  }

  /** The loading state (a spinner instead of the items). */
  setLoading(loading = true) { this.loading = loading; this.error = null; this.render(); }

  /** An error instead of the items, with a retry button (raises onRetry). */
  setError(message) { this.loading = false; this.error = message; this.render(); }

  setView(view) {
    if (view === this.o.view) return;
    this.o.view = view;
    this.render();
    this._scrollIntoView(this.selection.focus);
  }

  getView() { return this.o.view; }

  setSort(sort) { this.o.sort = { ...this.o.sort, ...sort }; this._compute(); this.render(); }
  getSort() { return { ...this.o.sort }; }

  setGroupBy(groupBy) { this.o.groupBy = groupBy || null; this.collapsed.clear(); this._compute(); this.render(); }
  getGroupBy() { return this.o.groupBy; }

  /** The items shown as cut (dimmed) until the paste. */
  setCut(ids) { this.cut = new Set((ids ?? []).map(String)); this._paintSelection(); }

  getItems() { return this.items; }
  getSelected() { return this.selection.list().map((id) => this.byId.get(id)).filter(Boolean); }
  select(ids) { this.selection.set((ids ?? []).map(String)); this._paintSelection(); this._scrollIntoView(this.selection.focus); this._emitSelection(); }
  selectAll() { this.selection.all(); this._paintSelection(); this._emitSelection(); }
  selectNone() { this.selection.none(); this._paintSelection(); this._emitSelection(); }
  invertSelection() { this.selection.invert(); this._paintSelection(); this._emitSelection(); }

  focus() { this.el.focus({ preventScroll: true }); }

  /** Starts renaming an item (the focused one by default). */
  rename(id = this.selection.focus) {
    const item = this.byId.get(String(id));
    const cell = item && this._cell(item.id);
    if (!cell || this.renaming) return;
    this._scrollIntoView(item.id);
    this._startRename(item, cell);
  }

  dispose() {
    this._cleanup?.();
    this.el.remove();
  }

  // ───────── data ─────────

  _compute() {
    const items = this.raw.map((i) => ({ ...i, id: String(i.id) }));
    // sort.key 'none' keeps the given order (Home: the pinned order, recent files newest first).
    const sorted = this.o.sort?.key === 'none' ? items : sortItems(items, { ...this.o.sort, locale: getLocale() });
    this.groups = groupItems(sorted, this.o.groupBy);
    this.items = this.groups.flatMap((g) => g.items);
    this.byId = new Map(this.items.map((i) => [i.id, i]));
    this.selection.setIds(this._visibleIds());
  }

  _visibleIds() {
    return this.groups.filter((g) => !this.collapsed.has(g.key)).flatMap((g) => g.items.map((i) => i.id));
  }

  // ───────── rendering ─────────

  render() {
    const view = this.o.view;
    this.el.dataset.view = view;
    this.el.setAttribute('aria-busy', String(!!this.loading));
    this.el.innerHTML = '';
    if (this.loading) {
      this.el.innerHTML = `<div class="mx-files__state"><span class="mx-files__spinner" aria-hidden="true"></span>${esc(t('explorer.working', {}, 'Working on it…'))}</div>`;
      return;
    }
    if (this.error) {
      this.el.innerHTML = `<div class="mx-files__state mx-files__state--error" role="alert">${getIconSvg('error', 32)}<p>${esc(this.error)}</p>
        <button type="button" class="mx-btn-subtle" data-retry>${esc(t('common.retry', {}, 'Try again'))}</button></div>`;
      this.el.querySelector('[data-retry]').addEventListener('click', () => this.o.onRetry?.());
      return;
    }
    if (view === 'details') this.el.appendChild(this._header());
    const body = document.createElement('div');
    body.className = 'mx-files__body';
    body.setAttribute('role', 'rowgroup');
    if (!this.items.length) {
      const empty = document.createElement('div');
      empty.className = 'mx-files__empty';
      empty.textContent = this.o.emptyText ?? t('explorer.empty', {}, 'This folder is empty.');
      body.appendChild(empty);
    }
    for (const group of this.groups) {
      if (group.label != null) body.appendChild(this._groupHeader(group));
      if (this.collapsed.has(group.key)) continue;
      const wrap = document.createElement('div');
      wrap.className = 'mx-files__items';
      for (const item of group.items) wrap.appendChild(this._row(item));
      body.appendChild(wrap);
    }
    this.el.appendChild(body);
    this._applyWidths();
    this._paintSelection();
  }

  _header() {
    const head = document.createElement('div');
    head.className = 'mx-files__head';
    head.setAttribute('role', 'row');
    for (const col of this.columns) {
      const cell = document.createElement('div');
      cell.className = `mx-files__th${col.align === 'end' ? ' is-end' : ''}`;
      cell.setAttribute('role', 'columnheader');
      const sorted = this.o.sort.key === col.key;
      cell.setAttribute('aria-sort', sorted ? (this.o.sort.direction === 'desc' ? 'descending' : 'ascending') : 'none');
      cell.innerHTML = `<button type="button" class="mx-files__sort">${esc(col.title)}</button>`
        + (sorted ? `<span class="mx-files__sort-mark ${this.o.sort.direction === 'desc' ? 'is-desc' : ''}" aria-hidden="true">${getIconSvg('chevronDown', 12)}</span>` : '')
        + '<span class="mx-files__resize" aria-hidden="true"></span>';
      cell.querySelector('button').addEventListener('click', () => {
        const direction = sorted && this.o.sort.direction === 'asc' ? 'desc' : 'asc';
        this.setSort({ key: col.key, direction });
        this.o.onSortChange?.(this.getSort());
      });
      cell.querySelector('.mx-files__resize').addEventListener('pointerdown', (e) => this._resizeColumn(e, col));
      cell.querySelector('.mx-files__resize').addEventListener('dblclick', () => this._autoWidth(col));
      head.appendChild(cell);
    }
    return head;
  }

  _groupHeader(group) {
    const h = document.createElement('button');
    h.type = 'button';
    h.className = 'mx-files__group';
    const collapsed = this.collapsed.has(group.key);
    h.setAttribute('aria-expanded', String(!collapsed));
    h.innerHTML = `<span class="mx-files__group-chevron">${getIconSvg('chevronDown', 14)}</span><span>${esc(group.label)}</span>
      <span class="mx-files__group-count">(${formatNumber(group.items.length)})</span><span class="mx-files__group-line"></span>`;
    h.addEventListener('click', () => {
      if (collapsed) this.collapsed.delete(group.key);
      else this.collapsed.add(group.key);
      this.selection.setIds(this._visibleIds());
      this.render();
    });
    return h;
  }

  _row(item) {
    const view = this.o.view;
    const row = document.createElement('div');
    row.className = 'mx-file';
    row.dataset.id = item.id;
    row.setAttribute('role', 'row');
    row.draggable = this.o.draggable !== false && !item.locked;
    if (item.hidden) row.classList.add('is-hidden');
    const size = ICON_SIZE[view] ?? 20;
    const thumb = item.thumbnail && size >= 48
      ? `<img class="mx-file__thumb" src="${esc(item.thumbnail)}" alt="" loading="lazy" draggable="false">`
      : (item.iconSvg ?? (item.icon ? getIconSvg(item.icon, size) : itemIconSvg(item, size)));
    if (view === 'details') {
      row.innerHTML = this.columns.map((col, i) => {
        const value = col.format ? col.format(item) : (item[col.key] ?? '');
        if (i === 0) {
          return `<div class="mx-file__cell mx-file__cell--name" role="gridcell"><span class="mx-file__icon">${thumb}</span><span class="mx-file__name">${esc(item.name)}</span></div>`;
        }
        return `<div class="mx-file__cell${col.align === 'end' ? ' is-end' : ''}" role="gridcell">${esc(value)}</div>`;
      }).join('');
    } else if (view === 'tiles') {
      const meta = isFolder(item) ? (item.meta ?? typeLabel(item)) : `${typeLabel(item)}<br>${formatSize(item.size)}`;
      row.innerHTML = `<span class="mx-file__icon">${thumb}</span><span class="mx-file__text" role="gridcell"><span class="mx-file__name">${esc(item.name)}</span><span class="mx-file__meta">${meta}</span></span>`;
    } else {
      row.innerHTML = `<span class="mx-file__icon">${thumb}</span><span class="mx-file__name" role="gridcell">${esc(item.name)}</span>`;
    }
    row.title = item.tooltip ?? (view === 'details' ? '' : `${item.name}\n${typeLabel(item)}${isFolder(item) ? '' : `\n${formatSize(item.size)}`}`);
    return row;
  }

  _rows() { return [...this.el.querySelectorAll('.mx-file')]; }
  _cell(id) { return this.el.querySelector(`.mx-file[data-id="${CSS.escape(String(id))}"]`); }

  _paintSelection() {
    const focus = this.selection.focus;
    for (const row of this._rows()) {
      const id = row.dataset.id;
      const on = this.selection.has(id);
      row.classList.toggle('is-selected', on);
      row.setAttribute('aria-selected', String(on));
      row.classList.toggle('is-focused', id === focus);
      row.classList.toggle('is-cut', this.cut.has(id) || !!this.byId.get(id)?.cut);
      row.id = `${this.el.id || 'mx-files'}-${id}`;
    }
    const focusRow = focus != null && this._cell(focus);
    if (focusRow) this.el.setAttribute('aria-activedescendant', focusRow.id);
    else this.el.removeAttribute('aria-activedescendant');
  }

  _applyWidths() {
    const template = this.columns.map((c) => `${this.widths[c.key]}px`).join(' ');
    this.el.style.setProperty('--mx-columns', template);
    this.el.style.setProperty('--mx-columns-width', `${this.columns.reduce((a, c) => a + this.widths[c.key], 0)}px`);
  }

  _resizeColumn(e, col) {
    e.preventDefault();
    e.stopPropagation();
    const rtl = getComputedStyle(this.el).direction === 'rtl';
    const startX = e.clientX;
    const start = this.widths[col.key];
    const move = (ev) => {
      const delta = (ev.clientX - startX) * (rtl ? -1 : 1);
      this.widths[col.key] = Math.max(col.min ?? 48, start + delta);
      this._applyWidths();
    };
    const up = () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); this.o.onColumnsChange?.({ ...this.widths }); };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  /** Double-click on a column edge: fits its content, as in Explorer. */
  _autoWidth(col) {
    const index = this.columns.indexOf(col);
    let width = 60;
    const probe = document.createElement('span');
    probe.className = 'mx-files__probe';
    this.el.appendChild(probe);
    for (const item of this.items) {
      probe.textContent = index === 0 ? item.name : (col.format ? col.format(item) : String(item[col.key] ?? ''));
      width = Math.max(width, probe.offsetWidth + (index === 0 ? 56 : 24));
    }
    probe.remove();
    this.widths[col.key] = Math.min(width, 800);
    this._applyWidths();
  }

  // ───────── events ─────────

  _wire() {
    const el = this.el;
    const on = (type, fn, opts) => { el.addEventListener(type, fn, opts); return () => el.removeEventListener(type, fn, opts); };
    const offs = [
      on('pointerdown', (e) => this._onPointerDown(e)),
      on('dblclick', (e) => this._onDoubleClick(e)),
      on('contextmenu', (e) => this._onContextMenu(e)),
      on('keydown', (e) => this._onKey(e)),
      on('dragstart', (e) => this._onDragStart(e)),
      on('dragover', (e) => this._onDragOver(e)),
      on('dragleave', (e) => this._onDragLeave(e)),
      on('drop', (e) => this._onDrop(e)),
      on('dragend', () => { this._clearDrop(); this.dragging = null; }),
      on('focus', () => { if (this.selection.focus == null && this.selection.ids.length) { this.selection.focus = this.selection.ids[0]; this._paintSelection(); } }),
    ];
    this._cleanup = () => offs.forEach((off) => off());
  }

  _rowOf(target) { return target?.closest?.('.mx-file'); }

  _onPointerDown(e) {
    if (e.button !== 0 && e.button !== 2) return;
    if (e.target.closest('.mx-files__head, .mx-files__group, .mx-rename, [data-retry]')) return;
    const row = this._rowOf(e.target);
    if (e.button === 2) return; // handled by contextmenu
    if (row) {
      const id = row.dataset.id;
      // A plain press on a selected item keeps the selection until the click ends, so it can be dragged.
      if (this.selection.has(id) && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
        const up = () => { row.removeEventListener('pointerup', up); if (!this.dragging) { this.selection.pick(id); this._paintSelection(); this._emitSelection(); } };
        row.addEventListener('pointerup', up);
        this.selection.focus = id;
        this._paintSelection();
        return;
      }
      this.selection.pick(id, { ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey });
      this._paintSelection();
      this._emitSelection();
      return;
    }
    // Empty space: clear (unless Ctrl) and start a marquee.
    if (!e.ctrlKey && !e.metaKey && !e.shiftKey) { this.selection.none(); this._paintSelection(); this._emitSelection(); }
    this._marquee(e);
  }

  _marquee(e) {
    if (e.pointerType === 'touch') return;
    const el = this.el;
    const box = document.createElement('div');
    box.className = 'mx-marquee';
    const origin = el.getBoundingClientRect();
    const start = { x: e.clientX - origin.left + el.scrollLeft, y: e.clientY - origin.top + el.scrollTop };
    const base = new Set(e.ctrlKey || e.metaKey ? this.selection.list() : []);
    let started = false;
    let last = e;
    let raf = 0;
    const update = () => {
      const r = el.getBoundingClientRect();
      const now = { x: last.clientX - r.left + el.scrollLeft, y: last.clientY - r.top + el.scrollTop };
      const rect = { left: Math.min(start.x, now.x), top: Math.min(start.y, now.y), right: Math.max(start.x, now.x), bottom: Math.max(start.y, now.y) };
      Object.assign(box.style, { left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.right - rect.left}px`, height: `${rect.bottom - rect.top}px` });
      const hit = [];
      for (const row of this._rows()) {
        const b = row.getBoundingClientRect();
        const rr = { left: b.left - r.left + el.scrollLeft, top: b.top - r.top + el.scrollTop };
        rr.right = rr.left + b.width;
        rr.bottom = rr.top + b.height;
        if (rr.left < rect.right && rr.right > rect.left && rr.top < rect.bottom && rr.bottom > rect.top) hit.push(row.dataset.id);
      }
      this.selection.selected = new Set([...base, ...hit]);
      if (hit.length) this.selection.focus = hit.at(-1);
      this._paintSelection();
      // Auto-scroll near the edges.
      if (last.clientY > r.bottom - 24) el.scrollTop += 12;
      else if (last.clientY < r.top + 24) el.scrollTop -= 12;
      raf = started ? requestAnimationFrame(update) : 0;
    };
    const move = (ev) => {
      last = ev;
      if (!started && Math.hypot(ev.clientX - e.clientX, ev.clientY - e.clientY) < 4) return;
      if (!started) { started = true; el.appendChild(box); el.setPointerCapture?.(e.pointerId); raf = requestAnimationFrame(update); }
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      cancelAnimationFrame(raf);
      box.remove();
      if (started) this._emitSelection();
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
  }

  _onDoubleClick(e) {
    const row = this._rowOf(e.target);
    if (!row || e.target.closest('.mx-rename')) return;
    const item = this.byId.get(row.dataset.id);
    if (item) this.o.onOpen?.(item, { newTab: e.ctrlKey || e.metaKey });
  }

  _onContextMenu(e) {
    if (e.target.closest('.mx-rename')) return;
    e.preventDefault();
    const row = this._rowOf(e.target);
    if (row) this.selection.contextPick(row.dataset.id);
    else if (!e.ctrlKey) this.selection.none();
    this._paintSelection();
    this._emitSelection();
    this.o.onContextMenu?.({ x: e.clientX, y: e.clientY, items: row ? this.getSelected() : [], background: !row, keyboard: false, shift: e.shiftKey });
  }

  /** How many items per visual row (1 in Details), from the layout. */
  _columnsPerRow() {
    if (this.o.view === 'details') return 1;
    const rows = this._rows();
    if (rows.length < 2) return 1;
    const top = rows[0].offsetTop;
    let n = 0;
    while (n < rows.length && rows[n].offsetTop === top) n++;
    return Math.max(1, n);
  }

  _onKey(e) {
    if (e.target.closest('.mx-rename')) return;
    const mods = { ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey };
    const rtl = getComputedStyle(this.el).direction === 'rtl';
    const cols = this._columnsPerRow();
    const grid = this.o.view !== 'details';
    const page = Math.max(1, Math.floor(this.el.clientHeight / 32)) * cols;
    let delta = null;
    switch (e.key) {
      case 'ArrowDown': delta = cols; break;
      case 'ArrowUp': delta = -cols; break;
      case 'ArrowRight': if (grid) delta = rtl ? -1 : 1; break;
      case 'ArrowLeft': if (grid) delta = rtl ? 1 : -1; break;
      case 'PageDown': delta = page; break;
      case 'PageUp': delta = -page; break;
      case 'Home': this.selection.focusAt(0, mods); this._afterKeyMove(); e.preventDefault(); return;
      case 'End': this.selection.focusAt(this.selection.ids.length - 1, mods); this._afterKeyMove(); e.preventDefault(); return;
      case 'Enter': {
        if (e.altKey) { this.o.onKey?.('properties', e); e.preventDefault(); return; }
        const items = this.getSelected();
        if (items.length) { this.o.onOpen?.(items[0], { newTab: mods.ctrl, items }); e.preventDefault(); }
        return;
      }
      case ' ':
        if (mods.ctrl) { this.selection.toggleFocused(); this._afterKeyMove(); e.preventDefault(); return; }
        break;
      case 'F2': this.rename(); e.preventDefault(); return;
      case 'ContextMenu': this._keyboardMenu(); e.preventDefault(); return;
      case 'F10': if (e.shiftKey) { this._keyboardMenu(true); e.preventDefault(); return; } break;
      case 'Escape': if (this.selection.size) { this.selectNone(); e.preventDefault(); } return;
      default: break;
    }
    if (delta != null) {
      this.selection.moveFocus(delta, mods);
      this._afterKeyMove();
      e.preventDefault();
      return;
    }
    if (mods.ctrl && (e.key === 'a' || e.key === 'A')) { this.selectAll(); e.preventDefault(); return; }
    // Type-ahead: printable keys without Ctrl / Alt.
    if (e.key.length === 1 && !mods.ctrl && !e.altKey && e.key !== ' ') {
      clearTimeout(this._typedTimer);
      this.typed += e.key;
      this._typedTimer = setTimeout(() => { this.typed = ''; }, 900);
      const visible = this.items.filter((i) => this.selection.ids.includes(i.id));
      const id = typeAhead(visible, this.typed, this.selection.focus, getLocale());
      if (id) { this.selection.pick(id); this._afterKeyMove(); }
      e.preventDefault();
    }
  }

  _afterKeyMove() {
    this._paintSelection();
    this._scrollIntoView(this.selection.focus);
    this._emitSelection();
  }

  _keyboardMenu(shift = false) {
    const row = this.selection.focus != null && this._cell(this.selection.focus);
    const items = this.getSelected();
    const r = (row && this.selection.has(this.selection.focus) ? row : this.el).getBoundingClientRect();
    const rtl = getComputedStyle(this.el).direction === 'rtl';
    this.o.onContextMenu?.({ x: rtl ? r.right - 16 : r.left + 16, y: row ? r.bottom : r.top + 16, items, background: !items.length, keyboard: true, shift });
  }

  _scrollIntoView(id) {
    const row = id != null && this._cell(id);
    if (!row) return;
    const head = this.el.querySelector('.mx-files__head')?.offsetHeight ?? 0;
    const top = row.offsetTop - head;
    const bottom = row.offsetTop + row.offsetHeight;
    if (top < this.el.scrollTop) this.el.scrollTop = top;
    else if (bottom > this.el.scrollTop + this.el.clientHeight) this.el.scrollTop = bottom - this.el.clientHeight;
  }

  _emitSelection() {
    const key = this.selection.list().join('\u0000');
    if (key === this._lastSelection) return;
    this._lastSelection = key;
    this.o.onSelectionChange?.(this.getSelected());
  }

  // ───────── rename ─────────

  _startRename(item, row) {
    const nameEl = row.querySelector('.mx-file__name');
    if (!nameEl) return;
    this.renaming = item.id;
    const multiline = this.o.view !== 'details' && this.o.view !== 'list' && this.o.view !== 'smallIcons' && this.o.view !== 'tiles';
    const input = document.createElement(multiline ? 'textarea' : 'input');
    input.className = 'mx-rename';
    input.value = item.name;
    input.spellcheck = false;
    input.setAttribute('aria-label', t('explorer.renameLabel', { name: item.name }, 'New name for {name}'));
    if (multiline) input.rows = 2;
    nameEl.replaceWith(input);
    input.focus();
    const [s, eIdx] = renameSelection(item);
    input.setSelectionRange(s, eIdx);
    const tip = document.createElement('div');
    tip.className = 'mx-rename__tip';
    tip.setAttribute('role', 'alert');
    let done = false;
    const finish = async (commit) => {
      if (done) return;
      const value = input.value.trim().replace(/[ .]+$/, '');
      if (commit && value !== item.name) {
        const err = nameError(value);
        if (err) { this._showTip(tip, input, err); input.focus(); return; }
        done = true;
        input.disabled = true;
        let result = true;
        try { result = (await this.o.onRename?.(item, value)) ?? true; } catch (ex) { result = ex?.message ?? String(ex); }
        if (result !== true) {
          done = false;
          input.disabled = false;
          this._showTip(tip, input, typeof result === 'string' ? result : t('explorer.renameFailed', {}, 'The item could not be renamed.'));
          input.focus();
          return;
        }
        item.name = value;
        const rawItem = this.raw.find((r) => String(r.id) === item.id);
        if (rawItem) rawItem.name = value;
      }
      done = true;
      tip.remove();
      this.renaming = null;
      const span = document.createElement('span');
      span.className = 'mx-file__name';
      span.textContent = item.name;
      input.replaceWith(span);
      this.focus();
    };
    input.addEventListener('keydown', (e) => {
      e.stopPropagation();
      if (e.key === 'Enter') { e.preventDefault(); finish(true); }
      else if (e.key === 'Escape') { e.preventDefault(); finish(false); }
      else if (/[\\/:*?"<>|]/.test(e.key)) { e.preventDefault(); this._showTip(tip, input, nameError(e.key)); }
    });
    input.addEventListener('blur', () => setTimeout(() => { if (!done && document.activeElement !== input) finish(true); }, 0));
    input.addEventListener('pointerdown', (e) => e.stopPropagation());
    input.addEventListener('dblclick', (e) => e.stopPropagation());
  }

  _showTip(tip, input, message) {
    tip.textContent = message;
    if (!tip.isConnected) document.body.appendChild(tip);
    const r = input.getBoundingClientRect();
    tip.style.left = `${Math.max(4, Math.min(r.left, window.innerWidth - tip.offsetWidth - 4))}px`;
    tip.style.top = `${r.bottom + 6}px`;
    clearTimeout(tip._timer);
    tip._timer = setTimeout(() => tip.remove(), 5000);
  }

  // ───────── drag and drop ─────────

  _onDragStart(e) {
    const row = this._rowOf(e.target);
    if (!row) return;
    if (!this.selection.has(row.dataset.id)) { this.selection.pick(row.dataset.id); this._paintSelection(); this._emitSelection(); }
    const items = this.getSelected();
    this.dragging = items.map((i) => i.id);
    e.dataTransfer.effectAllowed = 'copyMove';
    e.dataTransfer.setData(DRAG_TYPE, JSON.stringify({ source: this.o.sourceId ?? null, ids: this.dragging }));
    e.dataTransfer.setData('text/plain', items.map((i) => i.name).join('\n'));
    // A drag image with a count badge, like Explorer's.
    const ghost = document.createElement('div');
    ghost.className = 'mx-drag-ghost';
    ghost.innerHTML = `${itemIconSvg(items[0], 48)}${items.length > 1 ? `<span class="mx-drag-ghost__count">${formatNumber(items.length)}</span>` : ''}`;
    document.body.appendChild(ghost);
    e.dataTransfer.setDragImage(ghost, 24, 24);
    setTimeout(() => ghost.remove(), 0);
  }

  _dropTarget(e) {
    const row = this._rowOf(e.target);
    const item = row && this.byId.get(row.dataset.id);
    return item && isFolder(item) && !item.readOnly ? { row, item } : null;
  }

  _onDragOver(e) {
    const types = [...(e.dataTransfer?.types ?? [])];
    const internal = types.includes(DRAG_TYPE);
    const files = types.includes('Files');
    if (!internal && !files) return;
    const target = this._dropTarget(e);
    // Items from this view need a folder that is not one of them; items from elsewhere (another tab, the navigation
    // pane) and files from the desktop may also land on the view itself: the current folder.
    const own = internal && this.dragging;
    if (own && (!target || this.dragging.includes(target.item.id))) {
      this._highlight(null, false);
      e.dataTransfer.dropEffect = 'none';
      return;
    }
    e.preventDefault();
    const copy = e.ctrlKey || e.metaKey || (!internal && files);
    e.dataTransfer.dropEffect = copy ? 'copy' : 'move';
    this._highlight(target?.row ?? null, !target);
    const label = target ? target.item.name : (this.o.folderName ?? '');
    this.el.dataset.dropHint = label ? t(copy ? 'explorer.copyTo' : 'explorer.moveTo', { name: label }, copy ? 'Copy to {name}' : 'Move to {name}') : '';
  }

  _onDragLeave(e) {
    if (!this.el.contains(e.relatedTarget)) this._clearDrop();
  }

  _highlight(row, view) {
    if (this._dropRow !== row) { this._dropRow?.classList.remove('is-drop-target'); this._dropRow = row; row?.classList.add('is-drop-target'); }
    this.el.classList.toggle('is-drop-view', !!view);
  }

  _clearDrop() {
    this._dropRow?.classList.remove('is-drop-target');
    this._dropRow = null;
    this.el.classList.remove('is-drop-view');
    delete this.el.dataset.dropHint;

  }

  _onDrop(e) {
    const target = this._dropTarget(e);
    const copy = e.ctrlKey || e.metaKey;
    const raw = e.dataTransfer.getData(DRAG_TYPE);
    const files = [...(e.dataTransfer.files ?? [])];
    const dragging = this.dragging;
    this.dragging = null;
    this._clearDrop();
    if (raw) {
      e.preventDefault();
      const { source, ids } = JSON.parse(raw);
      if (target && ids.includes(target.item.id)) return;
      if (!target && dragging) return; // dropped back on its own view
      this.o.onDrop?.({ ids, source, target: target?.item ?? null, copy });
    } else if (files.length) {
      e.preventDefault();
      this.o.onDropFiles?.({ files, items: [...(e.dataTransfer.items ?? [])], target: target?.item ?? null });
    }
  }
}

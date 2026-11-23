// MudExplorer: a file explorer that looks and works like Windows 11's File Explorer.
//
//   ┌ tabs ─────────────────────────────────────────────────────────────┐
//   │ ← → ↑ ⟳  [ 📁 › Files › Reports › 2026      ⌄ ]  [ 🔍 Search 2026 ] │  toolbar: history, address bar, search
//   │ (+) New ▾ │ ✂ ⧉ 📋 ✎ ↗ 🗑 │ ⇅ Sort ▾  ☰ View ▾  …        ▯ Details │  command bar
//   │ Home      │ Name            Date modified   Type      Size │ details  │
//   │ Gallery   │ 📁 Drafts        …                              │ pane     │
//   │ ★ pinned  │ 📄 budget.xlsx   …                              │          │
//   │ ▸ Files   │                                                 │          │
//   │ 3 items │ 1 item selected 24 KB                       ☰ ▦ │  status bar
//   └───────────────────────────────────────────────────────────────────┘
//
// It knows nothing about where the files are: a provider lists folders, and every change (new folder, rename, move,
// copy, delete, upload, …) is a command the host carries out, then the Explorer refreshes.
//
//   const explorer = new MudExplorer({
//     provider: {
//       places: async () => [{ id: 'root', name: 'Files', hasChildren: true, icon: 'drive' }],   // the tree's roots
//       list: async (folderId, { search }) => ({ folder: { id, name }, path: [{ id, name }], items: [...] }),
//       children: async (folderId) => [{ id, name, hasChildren }],                                 // folders only
//       home: async () => ({ recent: [...files] }),            // optional: the Home page's "Recent" list
//       gallery: async () => ({ items: [...images] }),         // optional: the Gallery
//       resolvePath: async (text) => folderId | null,          // optional: typing a path in the address bar
//       rename: async (item, name) => true | 'error',          // optional: in-place rename (else a "rename" command)
//       preview: async (item) => url | null,                   // optional: the details pane's preview
//     },
//     start: 'root',                                           // a folder id, or MudExplorer.HOME
//     pinned: [{ id, name }], onPinnedChange: (pinned) => …,   // Quick access
//     can: (command, ctx) => boolean | undefined,              // narrows the defaults (rights, read-only places)
//     // …or as data: an item's `deny: ['delete', 'rename']` refuses those commands on it, the folder's `deny`
//     // refuses what changes the folder ('newFolder', 'upload', 'paste', 'moveTo', 'copyTo').
//     menuItems: (ctx) => [...],                                // extra context-menu entries for the items
//     onCommand: async (command, ctx) => ({ refresh: true, select: [id] } | void),
//   });
//
// Commands: open, newFolder, upload, cut / copy / paste (the Explorer keeps the clipboard; paste brings
// { items, mode: 'cut'|'copy', folder }), moveTo / copyTo (drag and drop), rename, delete (permanent with
// Shift+Delete), share, download, edit, properties, security, and the ids of your own menu entries.

import { t, getLocale, formatNumber } from '../core/i18n.js';
import { getIconSvg } from '../core/icons.js';
import { MudTreeView } from '../components/MudTreeView.js';
import { MudFileView } from './MudFileView.js';
import { MudCommandBar } from './MudCommandBar.js';
import { MudAddressBar } from './MudAddressBar.js';
import { MudContextMenu } from './MudContextMenu.js';
import { NavHistory, isFolder, fileKind, formatSize, formatModified, typeLabel, uniqueName } from './model.js';
import { kindIconSvg, itemIconSvg, driveIconSvg } from './icons.js';
import './messages.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const HOME = '::home';
const GALLERY = '::gallery';
const DRAG_TYPE = 'application/x-mudtail-items';
const VIEWS = ['largeIcons', 'icons', 'smallIcons', 'list', 'details', 'tiles'];
const VIEW_ICONS = { largeIcons: 'viewTiles', icons: 'viewTiles', smallIcons: 'viewTiles', list: 'viewList', details: 'viewDetails', tiles: 'viewTiles' };
const LOADING = '::loading';
let tabSeq = 0;

export class MudExplorer {
  static HOME = HOME;
  static GALLERY = GALLERY;

  constructor(o = {}) {
    this.o = { view: 'details', sort: { key: 'name', direction: 'asc' }, groupBy: null, detailsPane: false, navWidth: 220, ...o };
    this.provider = o.provider ?? {};
    this.pinned = [...(o.pinned ?? [])];
    this.clipboard = null; // { mode, items, from }
    this.tabs = [];
    this.active = null;
    this.selected = [];
    this.el = document.createElement('div');
    this.el.className = 'mud-explorer';
    this.el.tabIndex = -1;
    this._build();
    this.newTab(o.start ?? HOME, { activate: true });
    this._loadPlaces();
  }

  // ───────── public API ─────────

  getElement() { return this.el; }

  /** Goes to a folder (or MudExplorer.HOME / GALLERY) in the active tab. */
  navigate(id, { search = '', newTab = false } = {}) {
    if (newTab) return this.newTab(id, { activate: true });
    const tab = this.active;
    tab.history.push({ id, search });
    return this._load(tab);
  }

  back() { const l = this.active.history.goBack(); return l ? this._load(this.active) : null; }
  forward() { const l = this.active.history.goForward(); return l ? this._load(this.active) : null; }

  /** Up to the parent folder (Alt+↑). */
  up() {
    const path = this.active.data?.path ?? [];
    if (this.active.location.search) return this.navigate(this.active.location.id);
    if (path.length > 1) return this.navigate(path.at(-2).id);
    return null;
  }

  /** Lists the current folder again, keeping the selection and the scroll position. */
  refresh({ select = null } = {}) { return this._load(this.active, { keepScroll: true, select }); }

  /** Refreshes the tree under a folder (after a change made elsewhere). */
  async refreshTree(folderId) {
    const hit = this.tree.find(folderId);
    if (!hit || !this.provider.children) return;
    const kids = await this.provider.children(folderId);
    this.tree.updateNode(folderId, { children: kids.map((k) => this._treeNode(k)), lazy: false, expanded: hit.node.expanded });
  }

  newTab(id = HOME, { activate = true } = {}) {
    const tab = { id: `tab-${++tabSeq}`, history: new NavHistory(), view: this.o.view, sort: { ...this.o.sort }, groupBy: this.o.groupBy, data: null, title: '' };
    tab.history.push({ id, search: '' });
    this.tabs.push(tab);
    this._renderTabs();
    if (activate) this.activateTab(tab.id);
    return tab.id;
  }

  activateTab(tabId) {
    const tab = this.tabs.find((x) => x.id === tabId);
    if (!tab) return;
    if (this.active && this.active !== tab) this._saveTab(this.active);
    this.active = tab;
    this._renderTabs();
    Object.assign(this.files.o, { view: tab.view, sort: { ...tab.sort }, groupBy: tab.groupBy });
    if (tab.data) this._show(tab, { select: tab.selection ?? [], scroll: tab.scroll });
    else this._load(tab);
  }

  closeTab(tabId = this.active?.id) {
    if (this.tabs.length === 1) return;
    const i = this.tabs.findIndex((x) => x.id === tabId);
    if (i < 0) return;
    const [tab] = this.tabs.splice(i, 1);
    if (tab === this.active) { this.active = null; this.activateTab(this.tabs[Math.min(i, this.tabs.length - 1)].id); }
    else this._renderTabs();
  }

  setView(view) {
    if (!VIEWS.includes(view)) return;
    if (!this.active.data?.view) this.active.view = view;
    this.files.setView(view);
    this._updateCommands();
    this._renderStatus();
    this.o.onViewChange?.(view);
  }

  setSort(sort) { this.active.sort = { ...this.active.sort, ...sort }; this.files.setSort(this.active.sort); this._updateCommands(); }
  setGroupBy(by) { if (this.active.data?.groupBy === undefined) this.active.groupBy = by || null; this.files.setGroupBy(this.active.groupBy); this._updateCommands(); }

  /** Shows or hides the details pane (Alt+Shift+P). */
  toggleDetails(show = !this.o.detailsPane) {
    this.o.detailsPane = show;
    this.el.classList.toggle('has-details', show);
    this.commands.update('details', { pressed: show });
    this._renderDetails();
  }

  /** The current location, selection and view. */
  getState() {
    return {
      location: { ...this.active.location }, folder: this.active.data?.folder ?? null, path: this.active.data?.path ?? [],
      selection: this.selected.map((i) => i.id), view: this.active.view, sort: { ...this.active.sort }, groupBy: this.active.groupBy,
      tabs: this.tabs.map((x) => ({ id: x.id, title: x.title, location: x.history.current })), pinned: [...this.pinned],
    };
  }

  getSelected() { return [...this.selected]; }
  select(ids) { this.files.select(ids); }
  rename(id) { this.files.rename(id); }

  /** The status bar's progress: { label, value (0–100) or null for indeterminate } or null to hide it. */
  setProgress(progress) { this.progress = progress; this._renderStatus(); }

  setPinned(pinned) { this.pinned = [...(pinned ?? [])]; this._renderPlaces(); }

  focus() { this.files.focus(); }

  dispose() {
    MudContextMenu.closeAll();
    this.files.dispose();
    this.commands.dispose();
    this.address.dispose();
    this.tree.dispose();
    this._off?.();
    this.el.remove();
  }

  // ───────── layout ─────────

  _build() {
    const el = this.el;
    el.innerHTML = `
      <div class="mx-tabs" role="tablist" aria-label="${esc(t('explorer.tabs', {}, 'Tabs'))}"></div>
      <div class="mx-toolbar">
        <div class="mx-toolbar__nav">
          <button type="button" class="mx-tool mx-flip" data-act="back">${getIconSvg('back', 18)}</button>
          <button type="button" class="mx-tool mx-flip" data-act="forward">${getIconSvg('forward', 18)}</button>
          <button type="button" class="mx-tool" data-act="up">${getIconSvg('arrowUp', 18)}</button>
          <button type="button" class="mx-tool" data-act="refresh">${getIconSvg('refresh', 16)}</button>
        </div>
        <div class="mx-toolbar__address"></div>
        <div class="mx-search" role="search">
          <input type="search" class="mx-search__input" spellcheck="false">
          <span class="mx-search__icon" aria-hidden="true">${getIconSvg('search', 16)}</span>
        </div>
      </div>
      <div class="mx-toolbar2"></div>
      <div class="mx-main" role="tabpanel">
        <nav class="mx-nav" aria-label="${esc(t('explorer.navigation', {}, 'Navigation pane'))}">
          <ul class="mx-places"></ul>
          <div class="mx-nav__tree"></div>
        </nav>
        <div class="mx-splitter" role="separator" aria-orientation="vertical" tabindex="0" aria-label="${esc(t('explorer.resizeNav', {}, 'Resize the navigation pane'))}"></div>
        <section class="mx-content"></section>
        <aside class="mx-details" aria-label="${esc(t('explorer.detailsPane', {}, 'Details'))}"></aside>
      </div>
      <footer class="mx-status" role="status"></footer>`;
    const $ = (s) => el.querySelector(s);
    this.$ = { tabs: $('.mx-tabs'), search: $('.mx-search__input'), places: $('.mx-places'), nav: $('.mx-nav'), content: $('.mx-content'), details: $('.mx-details'), status: $('.mx-status'), main: $('.mx-main') };
    this.$.main.style.setProperty('--mx-nav-width', `${this.o.navWidth}px`);
    el.classList.toggle('has-details', !!this.o.detailsPane);

    const tip = (act, label, keys) => { const b = el.querySelector(`[data-act="${act}"]`); b.title = keys ? `${label} (${keys})` : label; b.setAttribute('aria-label', label); return b; };
    tip('back', t('explorer.back', {}, 'Back'), 'Alt+←').addEventListener('click', () => this.back());
    tip('forward', t('explorer.forward', {}, 'Forward'), 'Alt+→').addEventListener('click', () => this.forward());
    tip('up', t('explorer.up', {}, 'Up'), 'Alt+↑').addEventListener('click', () => this.up());
    tip('refresh', t('explorer.refresh', {}, 'Refresh'), 'F5').addEventListener('click', () => this.refresh());
    el.querySelector('[data-act="back"]').addEventListener('contextmenu', (e) => { e.preventDefault(); this._historyMenu(e.currentTarget); });

    this.address = new MudAddressBar({
      onNavigate: (id) => { if (!String(id).endsWith('?search')) this.navigate(id); },
      children: this.provider.children ? (id) => this.provider.children(id) : null,
      onSubmit: this.provider.resolvePath ? (text) => this._goToPath(text) : null,
    });
    $('.mx-toolbar__address').appendChild(this.address.getElement());

    let searchTimer;
    this.$.search.addEventListener('input', () => {
      clearTimeout(searchTimer);
      searchTimer = setTimeout(() => this._search(this.$.search.value), 450);
    });
    this.$.search.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { clearTimeout(searchTimer); this._search(this.$.search.value); }
      else if (e.key === 'Escape') { this.$.search.value = ''; clearTimeout(searchTimer); this._search(''); this.files.focus(); }
    });

    this.commands = new MudCommandBar({ items: this._commandItems(), moreMenu: () => this._moreMenu(), onCommand: (id, item) => this._onCommand(id, item) });
    $('.mx-toolbar2').appendChild(this.commands.getElement());

    this.files = new MudFileView({
      view: this.o.view, sort: this.o.sort, groupBy: this.o.groupBy, columns: this.o.columns,
      onOpen: (item, opts) => this._open(item, opts),
      onSelectionChange: (items) => { this.selected = items; this._updateCommands(); this._renderStatus(); this._renderDetails(); this.o.onSelectionChange?.(items); },
      onContextMenu: (e) => this._contextMenu(e),
      onRename: (item, name) => this._rename(item, name),
      onDrop: ({ ids, target, copy }) => this._dropItems(ids, target, copy),
      onDropFiles: ({ files, target }) => this._run('upload', { files, folder: target ?? this.active.data?.folder }),
      onSortChange: (sort) => { this.active.sort = sort; this._updateCommands(); },
      onRetry: () => this.refresh(),
    });
    this.$.content.appendChild(this.files.getElement());

    this.tree = new MudTreeView({
      nodes: [], label: t('explorer.folders', {}, 'Folders'), dragMode: 'none', orderButtons: false, dense: true, lines: false,
      onSelect: (node) => { if (node.id !== LOADING && node.id !== this.active.location.id) this.navigate(node.id); },
      onToggle: (node, expanded) => { if (expanded && node.lazy) this._expandTree(node.id); },
    });
    el.querySelector('.mx-nav__tree').appendChild(this.tree.getElement());

    this._wireSplitter(el.querySelector('.mx-splitter'));
    this._wireNavDrop();
    const key = (e) => this._onKey(e);
    el.addEventListener('keydown', key);
    this._off = () => el.removeEventListener('keydown', key);
  }

  _wireSplitter(splitter) {
    const set = (w) => { this.o.navWidth = Math.max(140, Math.min(480, w)); this.$.main.style.setProperty('--mx-nav-width', `${this.o.navWidth}px`); };
    splitter.addEventListener('pointerdown', (e) => {
      e.preventDefault();
      const rtl = getComputedStyle(this.el).direction === 'rtl';
      const x = e.clientX;
      const w = this.o.navWidth;
      const move = (ev) => set(w + (ev.clientX - x) * (rtl ? -1 : 1));
      const up = () => { window.removeEventListener('pointermove', move); window.removeEventListener('pointerup', up); };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up);
    });
    splitter.addEventListener('keydown', (e) => {
      const rtl = getComputedStyle(this.el).direction === 'rtl';
      const step = { ArrowLeft: rtl ? 16 : -16, ArrowRight: rtl ? -16 : 16 }[e.key];
      if (step) { set(this.o.navWidth + step); e.preventDefault(); }
    });
  }

  /** Items and desktop files dropped on a folder of the navigation pane. */
  _wireNavDrop() {
    const nav = this.$.nav;
    let row = null;
    const target = (e) => {
      const li = e.target.closest('[role="treeitem"], .mx-place[data-id]');
      const id = li?.dataset.id;
      if (!id || id === HOME || id === GALLERY || id === LOADING) return null;
      return { el: li.matches('[role="treeitem"]') ? li.querySelector('.mud-tree__row') : li, id, name: li.querySelector('.mud-tree__label, .mx-place__label')?.textContent ?? '' };
    };
    const clear = () => { row?.classList.remove('is-drop-target'); row = null; };
    nav.addEventListener('dragover', (e) => {
      const types = [...(e.dataTransfer?.types ?? [])];
      const hit = target(e);
      if (!hit || (!types.includes(DRAG_TYPE) && !types.includes('Files'))) { clear(); return; }
      e.preventDefault();
      e.dataTransfer.dropEffect = e.ctrlKey || !types.includes(DRAG_TYPE) ? 'copy' : 'move';
      if (row !== hit.el) { clear(); row = hit.el; row.classList.add('is-drop-target'); }
    });
    nav.addEventListener('dragleave', (e) => { if (!nav.contains(e.relatedTarget)) clear(); });
    nav.addEventListener('drop', (e) => {
      const hit = target(e);
      clear();
      if (!hit) return;
      e.preventDefault();
      const folder = { id: hit.id, name: hit.name, kind: 'folder' };
      const raw = e.dataTransfer.getData(DRAG_TYPE);
      if (raw) {
        const { ids } = JSON.parse(raw);
        if (!ids.includes(hit.id)) this._dropItems(ids, folder, e.ctrlKey);
      } else if (e.dataTransfer.files?.length) {
        this._run('upload', { files: [...e.dataTransfer.files], folder });
      }
    });
  }

  // ───────── loading ─────────

  async _load(tab, { keepScroll = false, select = null } = {}) {
    const location = tab.history.current;
    tab.location = location;
    const seq = (tab.seq = (tab.seq ?? 0) + 1);
    if (tab === this.active) { this._renderToolbar(); this.files.setLoading(true); }
    try {
      const data = await this._fetch(location);
      if (seq !== tab.seq) return; // a newer navigation won
      tab.data = data;
      tab.title = location.search ? t('explorer.searchTitle', { query: location.search, name: data.folder?.name ?? '' }, 'Search results in {name}') : (data.folder?.name ?? '');
      if (tab === this.active) this._show(tab, { keepScroll, select });
      else this._renderTabs();
      this.o.onNavigate?.({ location, folder: data.folder, path: data.path });
    } catch (err) {
      if (seq !== tab.seq) return;
      tab.data = null;
      if (tab === this.active) { this.files.setError(err?.message ?? String(err)); this._renderToolbar(); }
    }
  }

  async _fetch({ id, search }) {
    if (id === HOME) {
      const home = (await this.provider.home?.()) ?? {};
      const quick = this.pinned.map((p) => ({ ...p, kind: 'folder', section: t('explorer.quickAccess', {}, 'Quick access'), pinned: true }));
      const recent = (home.recent ?? []).map((i) => ({ ...i, section: t('explorer.recent', {}, 'Recent') }));
      return { folder: { id: HOME, name: t('explorer.home', {}, 'Home'), virtual: true }, path: [{ id: HOME, name: t('explorer.home', {}, 'Home') }], items: [...quick, ...recent], groupBy: 'section', sort: { key: 'none' } };
    }
    if (id === GALLERY) {
      const gallery = (await this.provider.gallery?.()) ?? { items: [] };
      return { folder: { id: GALLERY, name: t('explorer.gallery', {}, 'Gallery'), virtual: true }, path: [{ id: GALLERY, name: t('explorer.gallery', {}, 'Gallery') }], items: gallery.items ?? [], view: 'largeIcons', groupBy: 'modified' };
    }
    const data = await this.provider.list(id, { search: search || '' });
    return { folder: data.folder ?? { id, name: data.path?.at(-1)?.name ?? '' }, path: data.path ?? [], items: data.items ?? [], rights: data.rights };
  }

  /** Puts the tab's data on screen. */
  _show(tab, { keepScroll = false, select = null, scroll = null } = {}) {
    const data = tab.data;
    // Home and Gallery bring their own grouping and view; folders use the tab's.
    Object.assign(this.files.o, { view: data.view ?? tab.view, groupBy: data.groupBy !== undefined ? data.groupBy : tab.groupBy, sort: data.sort ?? { ...tab.sort } });
    // A new place starts with nothing selected; a refresh of the same place keeps the selection.
    const place = `${tab.id}|${tab.location.id}|${tab.location.search ?? ''}`;
    if (select == null && this.shownPlace !== place) select = [];
    this.shownPlace = place;
    this.files.o.folderName = data.folder?.name;
    this.files.o.emptyText = tab.location.search ? t('explorer.noResults', {}, 'No items match your search.') : undefined;
    this.files.setItems(data.items, { select, keepScroll });
    if (scroll != null) this.files.getElement().scrollTop = scroll;
    this.files.setCut(this.clipboard?.mode === 'cut' ? this.clipboard.items.map((i) => i.id) : []);
    this._renderToolbar();
    this._renderTabs();
    this._syncTree();
    this._updateCommands();
    this._renderStatus();
    this._renderDetails();
  }

  _saveTab(tab) {
    tab.selection = this.selected.map((i) => i.id);
    tab.scroll = this.files.getElement().scrollTop;
    tab.view = this.files.getView();
    tab.sort = this.files.getSort();
  }

  // ───────── toolbar, tabs, places ─────────

  _renderToolbar() {
    const tab = this.active;
    const h = tab.history;
    const set = (act, on) => { this.el.querySelector(`[data-act="${act}"]`).disabled = !on; };
    set('back', h.canGoBack());
    set('forward', h.canGoForward());
    const path = tab.data?.path ?? [];
    set('up', !!tab.location?.search || path.length > 1);
    const id = tab.location?.id;
    const icon = id === HOME ? getIconSvg('home', 16) : id === GALLERY ? getIconSvg('gallery', 16) : kindIconSvg('folder', 16);
    const crumbs = tab.location?.search ? [...path, { id: `${id}?search`, name: tab.title }] : path;
    this.address.setPath(crumbs, icon);
    const name = tab.data?.folder?.name ?? '';
    this.$.search.placeholder = t('explorer.searchIn', { name }, 'Search {name}');
    this.$.search.setAttribute('aria-label', this.$.search.placeholder);
    this.$.search.disabled = !!tab.data?.folder?.virtual || !this.provider.list;
    if (document.activeElement !== this.$.search) this.$.search.value = tab.location?.search ?? '';
  }

  _renderTabs() {
    const host = this.$.tabs;
    host.innerHTML = '';
    for (const tab of this.tabs) {
      const on = tab === this.active;
      const b = document.createElement('div');
      b.className = `mx-tab${on ? ' is-active' : ''}`;
      b.setAttribute('role', 'tab');
      b.setAttribute('aria-selected', String(on));
      b.tabIndex = on ? 0 : -1;
      const id = tab.history.current?.id;
      const icon = id === HOME ? getIconSvg('home', 16) : id === GALLERY ? getIconSvg('gallery', 16) : kindIconSvg('folder', 16);
      const title = tab.title || (id === HOME ? t('explorer.home', {}, 'Home') : '…');
      b.innerHTML = `<span class="mx-tab__icon">${icon}</span><span class="mx-tab__title">${esc(title)}</span>`
        + (this.tabs.length > 1 ? `<button type="button" class="mx-tab__close" tabindex="-1" aria-label="${esc(t('explorer.closeTab', { name: title }, 'Close {name}'))}" title="${esc(t('explorer.closeTabKey', {}, 'Close tab (Ctrl+W)'))}">${getIconSvg('close', 14)}</button>` : '');
      b.title = title;
      b.addEventListener('click', (e) => { if (e.target.closest('.mx-tab__close')) this.closeTab(tab.id); else this.activateTab(tab.id); });
      b.addEventListener('auxclick', (e) => { if (e.button === 1) this.closeTab(tab.id); });
      b.addEventListener('keydown', (e) => {
        const rtl = getComputedStyle(this.el).direction === 'rtl';
        const step = { ArrowRight: rtl ? -1 : 1, ArrowLeft: rtl ? 1 : -1 }[e.key];
        if (!step) return;
        const i = this.tabs.indexOf(tab);
        const next = this.tabs[(i + step + this.tabs.length) % this.tabs.length];
        this.activateTab(next.id);
        host.querySelector('.mx-tab.is-active')?.focus();
        e.preventDefault();
      });
      host.appendChild(b);
    }
    const add = document.createElement('button');
    add.type = 'button';
    add.className = 'mx-tabs__add';
    add.title = t('explorer.newTab', {}, 'New tab (Ctrl+T)');
    add.setAttribute('aria-label', add.title);
    add.innerHTML = getIconSvg('plus', 16);
    add.addEventListener('click', () => this.newTab(HOME));
    host.appendChild(add);
  }

  async _loadPlaces() {
    this._renderPlaces();
    try {
      const places = (await this.provider.places?.()) ?? [];
      this.tree.setData(places.map((p) => this._treeNode(p, true)));
      this._syncTree();
    } catch (err) {
      console.error('MudExplorer: places', err);
    }
  }

  _renderPlaces() {
    const list = this.$.places;
    const entry = (id, label, iconSvg, extra = '') => `<li><button type="button" class="mx-place" data-id="${esc(id)}">
      <span class="mx-place__icon">${iconSvg}</span><span class="mx-place__label">${esc(label)}</span>${extra}</button></li>`;
    list.innerHTML = entry(HOME, t('explorer.home', {}, 'Home'), getIconSvg('home', 18))
      + (this.provider.gallery ? entry(GALLERY, t('explorer.gallery', {}, 'Gallery'), getIconSvg('gallery', 18)) : '')
      + (this.pinned.length ? '<li class="mx-places__sep" role="separator"></li>' : '')
      + this.pinned.map((p) => entry(p.id, p.name, kindIconSvg('folder', 18), `<span class="mx-place__pin" title="${esc(t('explorer.pinned', {}, 'Pinned'))}">${getIconSvg('pin', 12)}</span>`)).join('')
      + '<li class="mx-places__sep" role="separator"></li>';
    list.querySelectorAll('.mx-place').forEach((b) => {
      b.addEventListener('click', () => this.navigate(b.dataset.id));
      b.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        const id = b.dataset.id;
        if (id === HOME || id === GALLERY) return;
        const pin = this.pinned.find((p) => p.id === id);
        MudContextMenu.open({
          x: e.clientX, y: e.clientY, focusFirst: false,
          items: [
            { id: 'open', label: t('explorer.cmd.open', {}, 'Open'), bold: true },
            { id: 'openTab', label: t('explorer.cmd.openTab', {}, 'Open in new tab'), icon: 'plus' },
            { separator: true },
            { id: 'unpin', label: t('explorer.cmd.unpin', {}, 'Unpin from Quick access'), icon: 'pinOff' },
          ],
          onSelect: (cmd) => {
            if (cmd === 'open') this.navigate(id);
            else if (cmd === 'openTab') this.newTab(id);
            else if (cmd === 'unpin' && pin) this._pin([pin], false);
          },
        });
      });
    });
    this._syncPlaces();
  }

  _syncPlaces() {
    const id = this.active?.location?.id;
    this.$.places.querySelectorAll('.mx-place').forEach((b) => b.classList.toggle('is-selected', b.dataset.id === id));
  }

  _treeNode(p, top = false) {
    const lazy = p.hasChildren !== false;
    return {
      id: String(p.id), label: p.name, lazy,
      iconSvg: p.icon === 'drive' ? driveIconSvg(18) : p.icon ? getIconSvg(p.icon, 18) : kindIconSvg('folder', 18),
      expanded: false, top,
      children: lazy ? [{ id: `${LOADING}:${p.id}`, label: t('common.loading', {}, 'Loading…'), disabled: true, icon: 'loader' }] : [],
    };
  }

  async _expandTree(id) {
    try {
      const kids = (await this.provider.children?.(id)) ?? [];
      this.tree.updateNode(id, { lazy: false, expanded: true, children: kids.map((k) => this._treeNode(k)) });
    } catch (err) {
      this.tree.updateNode(id, { lazy: true, expanded: false });
      console.error('MudExplorer: children', err);
    }
  }

  /** Highlights the current folder in the tree, opening its ancestors when they are loaded. */
  async _syncTree() {
    this._syncPlaces();
    const path = this.active?.data?.path ?? [];
    const id = this.active?.location?.id;
    if (!path.length || id === HOME || id === GALLERY) { this.tree.selectedId = null; this.tree.render(); return; }
    for (const crumb of path.slice(0, -1)) {
      const hit = this.tree.find(crumb.id);
      if (!hit) break;
      if (hit.node.lazy) await this._expandTree(crumb.id);
      else if (!hit.node.expanded) this.tree.toggle(crumb.id, true);
    }
    const here = this.tree.find(path.at(-1).id);
    this.tree.selectedId = here ? here.node.id : null;
    this.tree.render();
  }

  // ───────── commands ─────────

  /** Whether a command applies now: the defaults, narrowed by o.can. */
  _can(command, ctx = this._ctx()) {
    const folder = ctx.folder;
    const real = folder && !folder.virtual && !this.active.location.search;
    const n = ctx.items.length;
    const allFiles = n > 0 && ctx.items.every((i) => !isFolder(i));
    const base = {
      newFolder: real, upload: real, paste: real && !!this.clipboard,
      cut: n > 0, copy: n > 0, delete: n > 0, rename: n === 1, share: n > 0,
      open: n > 0, download: allFiles, edit: n === 1 && fileKind(ctx.items[0]) === 'image',
      properties: true, security: n <= 1 && (n === 1 || real), pin: n > 0 && ctx.items.every(isFolder),
      selectAll: true, selectNone: n > 0, invertSelection: true,
    }[command] ?? true;
    if (!base) return false;
    if (n > 0 && ctx.items.some((i) => i.deny?.includes(command))) return false;
    if (['newFolder', 'upload', 'paste', 'moveTo', 'copyTo'].includes(command) && folder?.deny?.includes(command)) return false;
    const custom = this.o.can?.(command, ctx);
    return custom === undefined ? true : !!custom;
  }

  _ctx(items = this.selected) {
    return { items, folder: this.active.data?.folder ?? null, path: this.active.data?.path ?? [], location: this.active.location, rights: this.active.data?.rights };
  }

  _commandItems() {
    const L = (k, d) => t(`explorer.cmd.${k}`, {}, d);
    return [
      { id: 'new', icon: 'plus', label: L('new', 'New'), showLabel: true, primary: true, menu: () => this._newMenu() },
      { separator: true },
      { id: 'cut', icon: 'cut', label: L('cut', 'Cut'), shortcut: 'Ctrl+X' },
      { id: 'copy', icon: 'copy', label: L('copy', 'Copy'), shortcut: 'Ctrl+C' },
      { id: 'paste', icon: 'paste', label: L('paste', 'Paste'), shortcut: 'Ctrl+V' },
      { id: 'rename', icon: 'rename', label: L('rename', 'Rename'), shortcut: 'F2' },
      { id: 'share', icon: 'share', label: L('share', 'Share') },
      { id: 'delete', icon: 'delete', label: L('delete', 'Delete'), shortcut: 'Del' },
      { separator: true },
      { id: 'sort', icon: 'sort', label: L('sort', 'Sort'), showLabel: true, menu: () => this._sortMenu() },
      { id: 'view', icon: VIEW_ICONS[this.o.view], label: L('view', 'View'), showLabel: true, menu: () => this._viewMenu() },
      { id: 'details', icon: 'detailsPane', label: L('details', 'Details'), showLabel: true, toggle: true, pressed: !!this.o.detailsPane, end: true, shortcut: 'Alt+Shift+P' },
    ];
  }

  _updateCommands() {
    if (!this.active) return;
    const ctx = this._ctx();
    const patch = {};
    for (const id of ['cut', 'copy', 'paste', 'rename', 'share', 'delete']) patch[id] = { disabled: !this._can(id, ctx) };
    patch.new = { disabled: !this._can('newFolder', ctx) && !this._can('upload', ctx) };
    this.commands.updateAll(patch);
    const view = this.files.getView();
    const viewButton = this.commands.items.find((i) => i.id === 'view');
    if (viewButton && viewButton.icon !== VIEW_ICONS[view]) this.commands.update('view', { icon: VIEW_ICONS[view] });
  }

  _newMenu() {
    const L = (k, d) => t(`explorer.cmd.${k}`, {}, d);
    const ctx = this._ctx();
    return [
      { id: 'newFolder', label: L('folder', 'Folder'), iconSvg: kindIconSvg('folder', 16), shortcut: 'Ctrl+Shift+N', disabled: !this._can('newFolder', ctx) },
      ...(this.o.newItems?.(ctx) ?? []),
      { separator: true },
      { id: 'uploadFiles', label: L('uploadFiles', 'Upload files'), icon: 'upload', disabled: !this._can('upload', ctx) },
      { id: 'uploadFolder', label: L('uploadFolder', 'Upload folder'), icon: 'folderPlus', disabled: !this._can('upload', ctx) },
    ];
  }

  _sortMenu() {
    const s = this.files.getSort();
    const g = this.files.getGroupBy();
    const L = (k, d) => t(`explorer.${k}`, {}, d);
    const keys = [['name', L('col.name', 'Name')], ['modified', L('col.modified', 'Date modified')], ['type', L('col.type', 'Type')], ['size', L('col.size', 'Size')]];
    return [
      ...keys.map(([k, label]) => ({ id: `sort:${k}`, label, radio: true, checked: s.key === k })),
      { separator: true },
      { id: 'dir:asc', label: L('ascending', 'Ascending'), radio: true, checked: s.direction === 'asc' },
      { id: 'dir:desc', label: L('descending', 'Descending'), radio: true, checked: s.direction === 'desc' },
      { separator: true },
      {
        id: 'groupBy', label: L('groupBy', 'Group by'), icon: 'layers', children: [
          ...[['type', L('col.type', 'Type')], ['modified', L('col.modified', 'Date modified')], ['kind', L('kind', 'Kind')]]
            .map(([k, label]) => ({ id: `group:${k}`, label, radio: true, checked: g === k })),
          { separator: true },
          { id: 'group:none', label: L('groupNone', '(None)'), radio: true, checked: !g },
        ],
      },
    ];
  }

  _viewMenu() {
    const v = this.files.getView();
    const L = (k, d) => t(`explorer.view.${k}`, {}, d);
    return [
      { id: 'view:largeIcons', label: L('largeIcons', 'Large icons'), radio: true, checked: v === 'largeIcons', shortcut: 'Ctrl+Shift+2' },
      { id: 'view:icons', label: L('icons', 'Medium icons'), radio: true, checked: v === 'icons', shortcut: 'Ctrl+Shift+3' },
      { id: 'view:smallIcons', label: L('smallIcons', 'Small icons'), radio: true, checked: v === 'smallIcons', shortcut: 'Ctrl+Shift+4' },
      { id: 'view:list', label: L('list', 'List'), radio: true, checked: v === 'list', shortcut: 'Ctrl+Shift+5' },
      { id: 'view:details', label: L('details', 'Details'), radio: true, checked: v === 'details', shortcut: 'Ctrl+Shift+6' },
      { id: 'view:tiles', label: L('tiles', 'Tiles'), radio: true, checked: v === 'tiles', shortcut: 'Ctrl+Shift+8' },
      { separator: true },
      { id: 'details', label: t('explorer.cmd.detailsPane', {}, 'Details pane'), checked: !!this.o.detailsPane, shortcut: 'Alt+Shift+P' },
    ];
  }

  _moreMenu() {
    const L = (k, d) => t(`explorer.cmd.${k}`, {}, d);
    const ctx = this._ctx();
    return [
      { id: 'selectAll', label: L('selectAll', 'Select all'), icon: 'selectAll', shortcut: 'Ctrl+A' },
      { id: 'selectNone', label: L('selectNone', 'Select none'), disabled: !ctx.items.length },
      { id: 'invertSelection', label: L('invert', 'Invert selection') },
      { separator: true },
      { id: 'pin', label: L('pin', 'Pin to Quick access'), icon: 'pin', disabled: !this._can('pin', ctx) },
      { id: 'security', label: L('security', 'Security'), icon: 'shield', disabled: !this._can('security', ctx) },
      { id: 'properties', label: L('properties', 'Properties'), icon: 'info', shortcut: 'Alt+Enter', disabled: !this._can('properties', ctx) },
    ];
  }

  /** Command bar, menus and keyboard all end here. */
  _onCommand(id, item = {}) {
    if (id.startsWith('sort:')) return this.setSort({ key: id.slice(5) });
    if (id.startsWith('dir:')) return this.setSort({ direction: id.slice(4) });
    if (id.startsWith('group:')) return this.setGroupBy(id === 'group:none' ? null : id.slice(6));
    if (id.startsWith('view:')) return this.setView(id.slice(5));
    const ctx = this._ctx();
    switch (id) {
      case 'details': return this.toggleDetails(item.pressed ?? !this.o.detailsPane);
      case 'selectAll': return this.files.selectAll();
      case 'selectNone': return this.files.selectNone();
      case 'invertSelection': return this.files.invertSelection();
      case 'cut': case 'copy': return this._clip(id);
      case 'paste': return this._paste();
      case 'rename': if (this._can('rename', ctx)) this.files.rename(ctx.items[0].id); return null;
      case 'newFolder': return this._newFolder();
      case 'uploadFiles': return this._pickFiles(false);
      case 'uploadFolder': return this._pickFiles(true);
      case 'open': return ctx.items[0] && this._open(ctx.items[0], { items: ctx.items });
      case 'openTab': return ctx.items.filter(isFolder).forEach((f) => this.newTab(f.id, { activate: false }));
      case 'pin': return this._pin(ctx.items.filter(isFolder), true);
      case 'unpin': return this._pin(ctx.items, false);
      case 'refresh': return this.refresh();
      default:
        if (['delete', 'share', 'download', 'edit', 'properties', 'security'].includes(id) && !this._can(id, ctx)) return null;
        return this._run(id, { ...ctx, ...(item.payload ?? {}) });
    }
  }

  /** Hands a command to the host and refreshes when it says so (or always, for changes). */
  async _run(command, ctx) {
    if (!this.o.onCommand) return null;
    let result;
    try {
      result = await this.o.onCommand(command, ctx);
    } catch (err) {
      console.error(`MudExplorer: ${command}`, err);
      return null;
    }
    const changes = ['newFolder', 'paste', 'moveTo', 'copyTo', 'delete', 'upload', 'rename'];
    if (result?.refresh || (result !== false && changes.includes(command))) {
      await this.refresh({ select: result?.select ?? null });
      if (ctx.folder?.id && this.tree.find(ctx.folder.id)) this.refreshTree(ctx.folder.id);
    }
    return result;
  }

  _open(item, { newTab = false } = {}) {
    if (isFolder(item)) return newTab ? this.newTab(item.id) : this.navigate(item.id);
    return this._run('open', this._ctx([item]));
  }

  async _rename(item, name) {
    if (this.provider.rename) {
      const result = await this.provider.rename(item, name);
      if (result === true) { if (isFolder(item)) this.refreshTree(this.active.data?.folder?.id); this.o.onRenamed?.(item, name); }
      return result;
    }
    const result = await this.o.onCommand?.('rename', { ...this._ctx([item]), name });
    if (result === false || typeof result === 'string') return result || false;
    return true;
  }

  async _newFolder() {
    const ctx = this._ctx();
    if (!this._can('newFolder', ctx)) return;
    const name = uniqueName(t('explorer.newFolderName', {}, 'New folder'), this.files.getItems().map((i) => i.name), getLocale());
    const result = await this._run('newFolder', { ...ctx, name });
    const id = result?.select?.[0] ?? result?.id;
    if (id != null) requestAnimationFrame(() => this.files.rename(String(id)));
  }

  _pickFiles(folder) {
    const input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;
    if (folder) input.webkitdirectory = true;
    input.addEventListener('change', () => {
      const files = [...(input.files ?? [])];
      if (files.length) this._run('upload', { ...this._ctx(), files, folder: this.active.data?.folder });
    });
    input.click();
  }

  _clip(mode) {
    const items = this.selected;
    if (!items.length) return;
    this.clipboard = { mode, items: [...items], from: this.active.data?.folder ?? null };
    this.files.setCut(mode === 'cut' ? items.map((i) => i.id) : []);
    this._updateCommands();
    this.o.onClipboardChange?.(this.clipboard);
  }

  async _paste() {
    const clip = this.clipboard;
    if (!clip || !this._can('paste')) return;
    const result = await this._run('paste', { ...this._ctx(clip.items), mode: clip.mode, from: clip.from });
    if (clip.mode === 'cut' && result !== false) { this.clipboard = null; this.files.setCut([]); this._updateCommands(); }
  }

  _dropItems(ids, target, copy) {
    const known = new Map(this.files.getItems().map((i) => [i.id, i]));
    const items = ids.map((id) => known.get(String(id)) ?? { id: String(id) });
    const folder = target ?? this.active.data?.folder;
    if (!folder || folder.virtual) return null;
    if (folder.deny?.includes(copy ? 'copyTo' : 'moveTo') || (!copy && items.some((i) => i.deny?.includes('moveTo')))) return null;
    return this._run(copy ? 'copyTo' : 'moveTo', { ...this._ctx(items), target: folder });
  }

  _pin(folders, on) {
    if (on) for (const f of folders) { if (!this.pinned.some((p) => p.id === f.id)) this.pinned.push({ id: f.id, name: f.name }); }
    else this.pinned = this.pinned.filter((p) => !folders.some((f) => f.id === p.id));
    this._renderPlaces();
    this.o.onPinnedChange?.([...this.pinned]);
    if (this.active.location.id === HOME) this.refresh();
  }

  async _goToPath(text) {
    const id = await this.provider.resolvePath(text);
    if (id != null) this.navigate(id);
    else this.o.onPathNotFound?.(text);
  }

  _search(query) {
    const loc = this.active.location;
    const q = String(query ?? '').trim();
    if (q === (loc.search ?? '')) return;
    if (!q) { if (loc.search) this.back(); return; }
    // Typing more replaces the search entry instead of stacking one per keystroke.
    if (loc.search) { this.active.history.current = { id: loc.id, search: q }; this._load(this.active); }
    else this.navigate(loc.id, { search: q });
  }

  _historyMenu(anchor) {
    const recent = this.active.history.recent(10);
    if (!recent.length) return;
    MudContextMenu.open({
      anchor, focusFirst: true,
      items: recent.map((l, i) => ({ id: String(i), label: l.search ? `${l.search}` : (l.id === HOME ? t('explorer.home', {}, 'Home') : this._nameOf(l.id)), iconSvg: kindIconSvg('folder', 16) })),
      onSelect: (id) => { for (let i = 0; i <= Number(id); i++) this.active.history.goBack(); this._load(this.active); },
    });
  }

  _nameOf(id) {
    for (const tab of this.tabs) {
      const hit = tab.data?.path?.find((p) => p.id === id);
      if (hit) return hit.name;
    }
    return this.tree.find(id)?.node.label ?? String(id);
  }

  // ───────── context menus ─────────

  _contextMenu({ x, y, items, background, keyboard, shift }) {
    const ctx = this._ctx(items);
    const L = (k, d) => t(`explorer.cmd.${k}`, {}, d);
    const dis = (id) => !this._can(id, ctx);
    if (background) {
      const menu = [
        { id: 'view', label: L('view', 'View'), icon: 'viewDetails', children: this._viewMenu().filter((i) => i.id !== 'details') },
        { id: 'sort', label: L('sortBy', 'Sort by'), icon: 'sort', children: this._sortMenu().filter((i) => i.id !== 'groupBy') },
        { id: 'groupBy', label: L('groupBy', 'Group by'), icon: 'layers', children: this._sortMenu().find((i) => i.id === 'groupBy').children },
        { separator: true },
        { id: 'refresh', label: L('refresh', 'Refresh'), icon: 'refresh', shortcut: 'F5' },
        { separator: true },
        { id: 'paste', label: L('paste', 'Paste'), icon: 'paste', shortcut: 'Ctrl+V', disabled: dis('paste') },
        { id: 'new', label: L('new', 'New'), icon: 'plus', children: this._newMenu() },
        { separator: true },
        { id: 'properties', label: L('properties', 'Properties'), icon: 'info', shortcut: 'Alt+Enter', disabled: dis('properties') },
      ];
      return MudContextMenu.open({ x, y, items: menu, focusFirst: keyboard, onSelect: (id, item) => this._onCommand(id, item) });
    }
    const one = items.length === 1 ? items[0] : null;
    const folders = items.length > 0 && items.every(isFolder);
    const quick = [
      { id: 'cut', icon: 'cut', label: L('cut', 'Cut'), disabled: dis('cut') },
      { id: 'copy', icon: 'copy', label: L('copy', 'Copy'), disabled: dis('copy') },
      { id: 'rename', icon: 'rename', label: L('rename', 'Rename'), disabled: dis('rename') },
      { id: 'share', icon: 'share', label: L('share', 'Share'), disabled: dis('share') },
      { id: 'delete', icon: 'delete', label: L('delete', 'Delete'), disabled: dis('delete') },
    ];
    const extra = this.o.menuItems?.(ctx) ?? [];
    const main = [
      { id: 'open', label: L('open', 'Open'), icon: folders ? 'folderOpen' : 'openExternal', shortcut: 'Enter', bold: true, disabled: dis('open') },
      ...(folders ? [{ id: 'openTab', label: L('openTab', 'Open in new tab'), icon: 'plus' }] : []),
      ...(one && this._can('edit', ctx) ? [{ id: 'edit', label: L('edit', 'Edit'), icon: 'edit' }] : []),
      ...(this._can('download', ctx) ? [{ id: 'download', label: L('download', 'Download'), icon: 'download' }] : []),
      ...(folders ? (items.every((i) => this.pinned.some((p) => p.id === i.id))
        ? [{ id: 'unpin', label: L('unpin', 'Unpin from Quick access'), icon: 'pinOff' }]
        : [{ id: 'pin', label: L('pin', 'Pin to Quick access'), icon: 'pin' }]) : []),
      ...(extra.length ? [{ separator: true }, ...extra] : []),
      { separator: true },
      { id: 'properties', label: L('properties', 'Properties'), icon: 'info', shortcut: 'Alt+Enter', disabled: dis('properties') },
    ];
    // "Show more options": the classic menu, every command with its words.
    const more = [
      ...main.filter((i) => i.id !== 'properties' && !i.separator),
      { separator: true },
      ...quick.map((q) => ({ ...q, shortcut: { cut: 'Ctrl+X', copy: 'Ctrl+C', rename: 'F2', delete: 'Del' }[q.id] })),
      { separator: true },
      { id: 'security', label: L('security', 'Security'), icon: 'shield', disabled: dis('security') },
      { id: 'properties', label: L('properties', 'Properties'), icon: 'info', shortcut: 'Alt+Enter', disabled: dis('properties') },
    ];
    return MudContextMenu.open({
      x, y, quick, items: main, more, showMore: shift, focusFirst: keyboard,
      onSelect: (id, item) => this._onCommand(id, item),
    });
  }

  // ───────── keyboard ─────────

  _onKey(e) {
    if (e.defaultPrevented) return;
    const inText = e.target.matches('input, textarea, [contenteditable="true"]');
    const ctrl = e.ctrlKey || e.metaKey;
    const k = e.key;
    const done = () => { e.preventDefault(); e.stopPropagation(); };
    // Explorer-wide shortcuts that work from the text boxes too.
    if (ctrl && (k === 't' || k === 'T')) { this.newTab(HOME); return done(); }
    if (ctrl && (k === 'w' || k === 'W')) { this.closeTab(); return done(); }
    if (ctrl && k === 'Tab') { const i = this.tabs.indexOf(this.active); this.activateTab(this.tabs[(i + (e.shiftKey ? -1 : 1) + this.tabs.length) % this.tabs.length].id); return done(); }
    if ((ctrl && (k === 'e' || k === 'E' || k === 'f' || k === 'F')) || k === 'F3') { this.$.search.focus(); this.$.search.select(); return done(); }
    if ((e.altKey && (k === 'd' || k === 'D')) || (ctrl && (k === 'l' || k === 'L')) || k === 'F4') { this.address.edit(); return done(); }
    if (k === 'F5' || (ctrl && (k === 'r' || k === 'R'))) { this.refresh(); return done(); }
    if (e.altKey && e.shiftKey && (k === 'p' || k === 'P')) { this.toggleDetails(); return done(); }
    if (e.altKey && !ctrl) {
      const rtl = getComputedStyle(this.el).direction === 'rtl';
      if (k === 'ArrowLeft') { (rtl ? this.forward() : this.back()); return done(); }
      if (k === 'ArrowRight') { (rtl ? this.back() : this.forward()); return done(); }
      if (k === 'ArrowUp') { this.up(); return done(); }
    }
    if (inText) return;
    if (ctrl && e.shiftKey && (k === 'N' || k === 'n')) { this._newFolder(); return done(); }
    if (ctrl && e.shiftKey && /^Digit[1-8]$/.test(e.code)) {
      const view = { 1: 'largeIcons', 2: 'largeIcons', 3: 'icons', 4: 'smallIcons', 5: 'list', 6: 'details', 7: 'tiles', 8: 'tiles' }[e.code.slice(5)];
      this.setView(view);
      return done();
    }
    if (k === 'BrowserBack' || (k === 'Backspace' && !e.altKey)) { this.back(); return done(); }
    if (k === 'BrowserForward') { this.forward(); return done(); }
    if (!this.files.getElement().contains(e.target) && !this.el.isSameNode(e.target)) return;
    if (ctrl && (k === 'c' || k === 'C')) { this._clip('copy'); return done(); }
    if (ctrl && (k === 'x' || k === 'X')) { this._clip('cut'); return done(); }
    if (ctrl && (k === 'v' || k === 'V')) { this._paste(); return done(); }
    if (k === 'Delete') { if (this._can('delete')) this._run('delete', { ...this._ctx(), permanent: e.shiftKey }); return done(); }
    if (e.altKey && k === 'Enter') { this._onCommand('properties'); return done(); }
  }

  // ───────── status bar and details pane ─────────

  _renderStatus() {
    const items = this.files?.getItems() ?? [];
    const sel = this.selected;
    const count = (key, n, d) => t(key, { count: n, n: formatNumber(n) }, d);
    const size = sel.length && sel.every((i) => !isFolder(i)) ? formatSize(sel.reduce((a, i) => a + (i.size ?? 0), 0)) : '';
    const view = this.files?.getView();
    const progress = this.progress
      ? `<span class="mx-status__progress"><span>${esc(this.progress.label ?? '')}</span><span class="mx-status__bar${this.progress.value == null ? ' is-indeterminate' : ''}"><span style="width:${Math.max(0, Math.min(100, this.progress.value ?? 30))}%"></span></span></span>`
      : '';
    this.$.status.innerHTML = `
      <span>${esc(count('explorer.status.items', items.length, '{n} items'))}</span>
      ${sel.length ? `<span class="mx-status__sep"></span><span>${esc(count('explorer.status.selected', sel.length, '{n} items selected'))}${size ? ` <span class="mx-status__size">${esc(size)}</span>` : ''}</span>` : ''}
      ${progress}
      <span class="mx-status__views">
        <button type="button" class="mx-status__view${view === 'details' ? ' is-on' : ''}" data-view="details" aria-pressed="${view === 'details'}" title="${esc(t('explorer.status.detailsTip', {}, 'Display information about each item in the window. (Ctrl+Shift+6)'))}" aria-label="${esc(t('explorer.view.details', {}, 'Details'))}">${getIconSvg('viewDetails', 14)}</button>
        <button type="button" class="mx-status__view${view === 'largeIcons' ? ' is-on' : ''}" data-view="largeIcons" aria-pressed="${view === 'largeIcons'}" title="${esc(t('explorer.status.iconsTip', {}, 'Display items by using large thumbnails. (Ctrl+Shift+2)'))}" aria-label="${esc(t('explorer.view.largeIcons', {}, 'Large icons'))}">${getIconSvg('viewTiles', 14)}</button>
      </span>`;
    this.$.status.querySelectorAll('[data-view]').forEach((b) => b.addEventListener('click', () => this.setView(b.dataset.view)));
  }

  async _renderDetails() {
    if (!this.o.detailsPane || !this.active) { this.$.details.innerHTML = ''; return; }
    const sel = this.selected;
    const pane = this.$.details;
    const seq = (this._detailsSeq = (this._detailsSeq ?? 0) + 1);
    const row = (label, value) => (value ? `<div class="mx-details__row"><dt>${esc(label)}</dt><dd>${esc(value)}</dd></div>` : '');
    if (sel.length > 1) {
      const files = sel.filter((i) => !isFolder(i));
      pane.innerHTML = `<div class="mx-details__preview">${kindIconSvg('file', 96)}</div>
        <h2 class="mx-details__name">${esc(t('explorer.status.selected', { count: sel.length, n: formatNumber(sel.length) }, '{n} items selected'))}</h2>
        <dl class="mx-details__props">${files.length === sel.length ? row(t('explorer.col.size', {}, 'Size'), formatSize(files.reduce((a, i) => a + (i.size ?? 0), 0))) : ''}</dl>`;
      return;
    }
    const item = sel[0] ?? null;
    const folder = this.active.data?.folder;
    if (!item) {
      pane.innerHTML = folder ? `<div class="mx-details__preview">${kindIconSvg('folder', 96)}</div><h2 class="mx-details__name">${esc(folder.name)}</h2>
        <p class="mx-details__hint">${esc(t('explorer.details.hint', {}, 'Select a single file to get more information.'))}</p>` : '';
      return;
    }
    const preview = item.thumbnail ? `<img src="${esc(item.thumbnail)}" alt="">` : itemIconSvg(item, 96);
    const props = [
      row(t('explorer.col.type', {}, 'Type'), typeLabel(item)),
      isFolder(item) ? '' : row(t('explorer.col.size', {}, 'Size'), formatSize(item.size)),
      row(t('explorer.col.modified', {}, 'Date modified'), formatModified(item.modified)),
      row(t('explorer.details.created', {}, 'Date created'), formatModified(item.created)),
      row(t('explorer.details.owner', {}, 'Owner'), item.owner),
      ...(item.properties ?? []).map((p) => row(p.label, p.value)),
    ].join('');
    pane.innerHTML = `<div class="mx-details__preview">${preview}</div><h2 class="mx-details__name">${esc(item.name)}</h2>
      <div class="mx-details__actions"></div><h3 class="mx-details__section">${esc(t('explorer.details.properties', {}, 'Properties'))}</h3><dl class="mx-details__props">${props}</dl>`;
    const actions = pane.querySelector('.mx-details__actions');
    for (const [id, icon, label] of [['share', 'share', t('explorer.cmd.share', {}, 'Share')], ['security', 'shield', t('explorer.cmd.security', {}, 'Security')]]) {
      if (!this._can(id, this._ctx([item]))) continue;
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'mx-btn-subtle';
      b.innerHTML = `${getIconSvg(icon, 16)}<span>${esc(label)}</span>`;
      b.addEventListener('click', () => this._run(id, this._ctx([item])));
      actions.appendChild(b);
    }
    if (!item.thumbnail && fileKind(item) === 'image' && this.provider.preview) {
      const url = await this.provider.preview(item).catch(() => null);
      if (url && seq === this._detailsSeq) pane.querySelector('.mx-details__preview').innerHTML = `<img src="${esc(url)}" alt="">`;
    }
  }
}

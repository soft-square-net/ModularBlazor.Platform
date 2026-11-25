// MudTail interop surface for Blazor (IJSRuntime / IJSObjectReference).
//
//   var mud = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/<Assembly>/js/mudtail.blazor.js");
//   await mud.InvokeVoidAsync("init", new { primary = "#594ae2", isDark = false });
//   var tabs = await mud.InvokeAsync<IJSObjectReference>("create", "tabs", hostElement, options, dotNetRef);
//   await tabs.InvokeVoidAsync("setActive", "scores");
//   await tabs.InvokeVoidAsync("dispose");   // from the component's DisposeAsync
//
// Lifecycle contract:
//   init(options)                     once per app: theme (+ optional dir, lang, autoInit)
//   create / behavior / ripple / tooltip / lightbox / autoInit / observeTheme / observeDirection
//                                     → a handle; C# calls its methods and finally handle.dispose()
//   dispose()                         disposes every handle still alive (module teardown); activeCount() checks leaks
// Every handle's dispose() is idempotent. Component events are forwarded to the .NET object as
// OnMudEvent(string name, object? payload), so the C# side needs:
//   [JSInvokable] public Task OnMudEvent(string name, JsonElement payload)

import {
  initTheme,
  getTheme,
  updateTheme,
  showToast,
  createRipple,
  attachTooltip,
  createMudButton,
  createAutocomplete,
  MudTabs,
  MudAccordion,
  MudDrawer,
  MudDialog,
  MudDataTable,
  toPaginationFilter,
  setDirection,
  attachBehavior,
  openLightbox,
  autoInit as libAutoInit,
  setLocale,
  createSpinner,
  showLoading,
  setButtonLoading,
  pageLoader,
  MudProgressLinear,
  MudProgressCircular,
  createStatCard,
  loadPlugin as libLoadPlugin,
  removePlugin as libRemovePlugin,
  getPlugin,
  hasPlugin,
  listPlugins,
  pluginComponentNames,
} from './core-api.js';
import * as coreApi from './core-api.js';
import { buildComponent, hasComponent, setHandleTracker, reserveComponentNames, setPluginHost } from './core/plugins.js';

// The core API. The large components (dock, tree view, image editor, file explorer) are not in this file: create()
// loads them on first use, and load(area) hands a module their classes. See src/areas/.
export * from './core-api.js';

const EVENT_METHOD = 'OnMudEvent';

/** Calls back into .NET; a disposed DotNetObjectReference must not break the UI. */
function emitter(dotNetRef) {
  return (name, payload = null) => {
    if (!dotNetRef) return;
    dotNetRef.invokeMethodAsync(EVENT_METHOD, name, payload).catch(() => {});
  };
}

/** Appends `el` into a Blazor-rendered host. The host must be an element Blazor renders empty. */
function mountInto(host, el) {
  if (host instanceof Element) host.appendChild(el);
  return el;
}

// ───────────── lifecycle: every handle handed to .NET is tracked until it is disposed ─────────────

const live = new Set();

/** Registers a handle so the module-level dispose() can clean it up; its own dispose() becomes idempotent. */
function track(handle) {
  if (!handle || typeof handle.dispose !== 'function') return handle;
  const inner = handle.dispose.bind(handle);
  let disposed = false;
  handle.dispose = () => {
    if (disposed) return;
    disposed = true;
    live.delete(handle);
    inner();
  };
  live.add(handle);
  return handle;
}

// Handles plugins create with ctx.handle() join this module's lifecycle: dispose() and activeCount() include them.
setHandleTracker((handle) => { live.add(handle); const inner = handle.dispose; handle.dispose = () => { live.delete(handle); inner(); }; });

/** Disposes everything created through this module (components, behaviours, observers). Returns how many. */
export function dispose() {
  const handles = [...live];
  for (const h of handles) {
    try { h.dispose(); } catch (e) { console.error('[MudTail] dispose failed', e); }
  }
  return handles.length;
}

/** How many handles are still alive: a quick leak check from .NET. */
export function activeCount() {
  return live.size;
}

// ───────────── setup ─────────────

const THEME_KEYS = ['primary', 'secondary', 'borderRadius', 'isDark', 'numerals'];

/**
 * Applies the saved theme, then any overrides, and optionally the direction and behaviours.
 * options: { primary?, secondary?, borderRadius?, isDark?, numerals?: 'auto'|'latn'|'arab'|'arabext', locale?: 'ar', persistLocale?,
 *            dir?: 'ltr'|'rtl', lang?, autoInit?: boolean }
 * numerals picks the digits (Western 1 2 3 or Arabic-Indic ١ ٢ ٣); it is saved with the theme.
 * locale sets <html lang> and the direction (RTL for Arabic, Hebrew, …) and switches MudTail's own texts.
 * Only these keys are used, so calling it as a module entry point (moduleHost passes { moduleId }) is harmless.
 * Returns the theme.
 */
export function init(options = null) {
  initTheme();
  const overrides = Object.fromEntries(THEME_KEYS.filter((k) => options?.[k] !== undefined && options?.[k] !== null).map((k) => [k, options[k]]));
  if (Object.keys(overrides).length) updateTheme(overrides);
  if (options?.locale) setLocale(options.locale, { dir: options.dir ?? null, persist: !!options.persistLocale });
  else if (options?.dir) setDirection(options.dir, options.lang);
  if (options?.autoInit) autoInit(document.body);
  return getTheme();
}

export function setTheme(updates) {
  return updateTheme(updates || {});
}

export function toggleDark() {
  return updateTheme({ isDark: !getTheme().isDark });
}

/** Raises OnMudEvent("theme", theme) on every theme change. Returns a handle with dispose(). */
export function observeTheme(dotNetRef) {
  const emit = emitter(dotNetRef);
  const handler = (e) => emit('theme', e.detail);
  window.addEventListener('mud-theme-changed', handler);
  return track({ dispose: () => window.removeEventListener('mud-theme-changed', handler) });
}

/** Raises OnMudEvent("locale", { locale, dir }) whenever setLocale() changes it. Returns a handle with dispose(). */
export function observeLocale(dotNetRef) {
  const emit = emitter(dotNetRef);
  const handler = (e) => emit("locale", e.detail);
  window.addEventListener("mud-locale-changed", handler);
  return track({ dispose: () => window.removeEventListener("mud-locale-changed", handler) });
}

/** Raises OnMudEvent("direction", "ltr" | "rtl") whenever setDirection() changes it. Returns a handle with dispose(). */
export function observeDirection(dotNetRef) {
  const emit = emitter(dotNetRef);
  const handler = (e) => emit('direction', e.detail);
  window.addEventListener('mud-direction-changed', handler);
  return track({ dispose: () => window.removeEventListener('mud-direction-changed', handler) });
}

export function toast(message, type = 'info') {
  showToast(message, type);
}

// ───────────── behaviours on Blazor-rendered markup ─────────────

/** Adds the Material ripple to an element Blazor rendered (e.g. a <button class="mud-button-root">). */
export function ripple(element) {
  const onClick = (e) => createRipple(e, element);
  element?.addEventListener('click', onClick);
  return track({ dispose: () => element?.removeEventListener('click', onClick) });
}

/** Attaches a tooltip to an element Blazor rendered. */
export function tooltip(element, text, position = 'top') {
  const detach = attachTooltip(element, text, position);
  return track({ dispose: detach });
}

/**
 * Starts the data-mud behaviours under `root` (default <body>) and keeps them in sync with what Blazor renders.
 * Returns { refresh, dispose }. Shadows the library's autoInit so module-level dispose() stops it too.
 */
export function autoInit(root = document.body) {
  return track(libAutoInit(root));
}

/**
 * One behaviour on one element, tied to the caller's lifecycle (OnAfterRenderAsync → DisposeAsync):
 * 'ticker' | 'score-strip' | 'lightbox' | 'mega-nav' | 'reading-progress' | 'theme-toggle' (or a registered one).
 * Returns its handle, or null when the element's markup does not fit the behaviour.
 */
export function behavior(name, element) {
  return track(attachBehavior(name, element));
}

/** Opens the lightbox on [{ src, caption, alt }] at `index`. Returns { next, prev, show, close, dispose }. */
export function lightbox(items, index = 0) {
  return track(openLightbox(items, index));
}

const factories = {
  // Progress: handle.setValue(n), setBuffer(n) (linear), setIndeterminate(bool), update(options). See MudProgress.js.
  progress(host, options) {
    const bar = new MudProgressLinear(options);
    mountInto(host, bar.getElement());
    return bar;
  },

  progressCircular(host, options) {
    const ring = new MudProgressCircular(options);
    mountInto(host, ring.getElement());
    return ring;
  },

  // Tree view. Events: move ({ id, from: { parentId, index }, to: { parentId, index }, source }), select (node),
  // toggle ({ id, expanded }), change (the whole tree, in order: save it, or apply the single move).
  async tree(host, options, emit) {
    const { MudTreeView } = await load('tree');
    const tree = new MudTreeView({
      ...options,
      onMove: ({ node, from, to, source }) => emit('move', { id: node.id, from, to, source }),
      onSelect: (node) => emit('select', node),
      onToggle: (node, expanded) => emit('toggle', { id: node.id, expanded }),
      onChange: (nodes) => emit('change', nodes),
    });
    mountInto(host, tree.getElement());
    return {
      setData: (nodes) => tree.setData(nodes ?? []),
      getData: () => tree.getData(),
      select: (id) => tree.select(id),
      toggle: (id, expanded = null) => tree.toggle(id, expanded ?? undefined),
      expandAll: () => tree.expandAll(),
      collapseAll: () => tree.collapseAll(),
      moveUp: (id) => tree.moveUp(id),
      moveDown: (id) => tree.moveDown(id),
      move: (id, parentId, index) => tree.move(id, parentId ?? null, index),
      addNode: (parentId, node, index = null) => tree.addNode(parentId ?? null, node, index ?? Infinity),
      removeNode: (id) => tree.removeNode(id),
      updateNode: (id, patch) => tree.updateNode(id, patch),
      dispose: () => tree.dispose(),
    };
  },

  // Image editor. Events: opened, change (the edit state), exported (file list), uploaded, error.
  // Bytes come back to .NET as a stream: InvokeAsync<IJSStreamReference>("exportImage", "webp", 0.86) or
  // ("getFile", "thumbnail"), then OpenReadStreamAsync(maxBytes) and store it (e.g. POST it with your HttpClient).
  async imageEditor(host, options, emit) {
    const { MudImageEditor, renderRecipe } = await load('image-editor');
    const meta = (f) => ({ name: f.name, fileName: f.fileName, width: f.width, height: f.height, type: f.type, size: f.size });
    const editor = new MudImageEditor({
      ...options,
      onOpen: (info) => emit('opened', info),
      onChange: (state) => emit('change', state),
      onExport: (files, recipe) => { regenerated = null; emit('exported', { files: files.map(meta), recipe }); },
      onUpload: (result) => emit('uploaded', result),
      onError: (error) => emit('error', error),
      // Saving through .NET: the Save button raises "save" with the file list; read each with getFile(name).
      // The payload carries the recipe: store it with the original to regenerate these files later.
      onSave: options.saveWith === 'dotnet' ? (files, _editor, recipe) => { emit('save', { files: files.map(meta), recipe }); return { ok: true }; } : options.onSave,
    });
    mountInto(host, editor.getElement());
    const bytes = async (blob) => (blob ? new Uint8Array(await blob.arrayBuffer()) : null);
    let regenerated = null; // files from the last renderRecipe()
    return {
      open: (src) => editor.open(src),
      getState: () => editor.getState(),
      setState: (state) => editor.setState(state),
      undo: () => editor.undo(),
      redo: () => editor.redo(),
      reset: () => editor.reset(),
      setTool: (id) => editor.setTool(id),
      rotate: (direction) => editor.rotate(direction),
      flip: (axis) => editor.flip(axis),
      straighten: (angle) => editor.straighten(angle),
      adjust: (values) => editor.adjust(values ?? {}),
      applyPreset: (name) => editor.applyPreset(name),
      crop: (rectOrRatio) => editor.crop(rectOrRatio),
      resize: (size) => editor.resize(size),
      zoomFit: () => editor.zoomFit(),
      exportImage: async (format = null, quality = null) => bytes(await editor.exportImage({ ...(format ? { format } : {}), ...(quality ? { quality } : {}) })),
      exportSizes: async () => (await editor.exportSizes()).map(meta),
      getFile: (name) => bytes((regenerated ?? editor.exports).find((f) => f.name === name)?.blob),
      // Recipes: getRecipe() for storage, applyRecipe(json) to edit again, renderRecipe(originalUrl, json) to
      // regenerate the files without the editor's UI (then getFile(name) for each, as a stream).
      getRecipe: () => editor.getRecipe(),
      applyRecipe: (recipe, verify = 'warn') => editor.applyRecipe(recipe, { verify }),
      renderRecipe: async (original, recipe, verify = 'warn') => {
        const result = await renderRecipe(original, recipe, { verify });
        regenerated = result.files;
        return { files: result.files.map(meta), warnings: result.warnings };
      },
      upload: () => editor.upload(),
      download: (name = null) => editor.download(name),
      dispose: () => editor.dispose(),
    };
  },

  /**
   * File explorer (Windows 11 style). The provider is .NET: every question the Explorer has raises
   * OnMudEvent("request", { requestId, kind, ... }), answered with handle.resolve(requestId, value) or
   * handle.reject(requestId, message). Kinds:
   *   places → [{ id, name, hasChildren, icon? }]            list { folderId, search } → { folder, path, items }
   *   children { folderId } → [{ id, name, hasChildren }]     home → { recent }   gallery → { items }
   *   resolvePath { text } → id | null                       rename { item, name } → true | "error"
   *   preview { item } → url | null                          command { command, items, folder, target?, mode?, name?,
   *                                                            files?: { token, files: [{ index, name, size, type, path }] },
   *                                                            permanent? } → { refresh?, select? } | null
   * Files dropped or picked for an upload stay in the browser: read them with
   * InvokeAsync<IJSStreamReference>("readFile", token, index, offset, length) and call releaseFiles(token) when done.
   * options: the MudExplorer options that are data (start, pinned, view, sort, groupBy, detailsPane, navWidth) plus
   *   home, gallery, resolvePath, preview: true when .NET answers those kinds; renameInPlace (default true: "rename"
   *   requests while the name is being edited, else a "rename" command); menuItems / newItems: [{ id, label, icon,
   *   kinds?: ['image', 'folder', …], single?: true }] for the context menu / the New menu (they come back as commands).
   * Rights are data: an item's or the folder's deny list (see MudExplorer); it also hides menuItems / newItems by id.
   * Other events: navigate ({ location, folder, path }), selection ([ids]), pinned ([{ id, name }]), view (name).
   */
  async explorer(host, options, emit) {
    const { MudExplorer, fileKind } = await load('explorer');
    const pending = new Map();
    let seq = 0;
    const ask = (kind, args = {}) => new Promise((resolve, reject) => {
      const requestId = ++seq;
      pending.set(requestId, { resolve, reject });
      emit('request', { requestId, kind, ...args });
    });
    const files = new Map();
    let fileSeq = 0;
    const keepFiles = (list) => {
      const token = `files-${++fileSeq}`;
      files.set(token, list);
      return { token, files: list.map((f, index) => ({ index, name: f.name, size: f.size, type: f.type, lastModified: f.lastModified, path: f.webkitRelativePath || '' })) };
    };
    const slim = ({ iconSvg, ...item }) => item;
    const ctxOut = (ctx) => ({
      items: (ctx.items ?? []).map(slim), folder: ctx.folder ?? null, path: ctx.path ?? [], location: ctx.location ?? null,
      ...(ctx.target ? { target: slim(ctx.target) } : {}), ...(ctx.mode ? { mode: ctx.mode } : {}), ...(ctx.from ? { from: ctx.from } : {}),
      ...(ctx.name != null ? { name: ctx.name } : {}), ...(ctx.permanent ? { permanent: true } : {}),
      ...(ctx.files ? { files: keepFiles(ctx.files) } : {}),
    });
    const entries = (list, ctx) => (list ?? []).filter((m) => {
      if (m.single && ctx.items.length !== 1) return false;
      if (m.kinds?.length && !ctx.items.every((i) => m.kinds.includes(fileKind(i)))) return false;
      // Rights are data for your entries too: a selected item's, or the open folder's, deny list hides them.
      if (ctx.items.some((i) => i.deny?.includes(m.id)) || ctx.folder?.deny?.includes(m.id)) return false;
      return true;
    }).map(({ kinds, single, ...m }) => m);
    const explorer = new MudExplorer({
      ...options,
      provider: {
        places: () => ask('places'),
        list: (folderId, { search }) => ask('list', { folderId, search }),
        children: (folderId) => ask('children', { folderId }),
        ...(options.home ? { home: () => ask('home') } : {}),
        ...(options.gallery ? { gallery: () => ask('gallery') } : {}),
        ...(options.resolvePath ? { resolvePath: (text) => ask('resolvePath', { text }) } : {}),
        ...(options.renameInPlace !== false ? { rename: (item, name) => ask('rename', { item: slim(item), name }) } : {}),
        ...(options.preview ? { preview: (item) => ask('preview', { item: slim(item) }) } : {}),
      },
      menuItems: (ctx) => entries(options.menuItems, ctx),
      newItems: (ctx) => entries(options.newItems, ctx),
      onCommand: (command, ctx) => ask('command', { command, ...ctxOut(ctx) }),
      onNavigate: (e) => emit('navigate', e),
      onSelectionChange: (items) => emit('selection', items.map((i) => i.id)),
      onPinnedChange: (pinned) => emit('pinned', pinned),
      onViewChange: (view) => emit('view', view),
    });
    mountInto(host, explorer.getElement());
    return {
      resolve: (requestId, value = null) => { const p = pending.get(requestId); if (p) { pending.delete(requestId); p.resolve(value); } },
      reject: (requestId, message = '') => { const p = pending.get(requestId); if (p) { pending.delete(requestId); p.reject(new Error(message || 'The request failed.')); } },
      navigate: (id, search = '') => explorer.navigate(id, { search }),
      refresh: (select = null) => explorer.refresh({ select }),
      refreshTree: (folderId) => explorer.refreshTree(folderId),
      select: (ids) => explorer.select(ids ?? []),
      rename: (id = null) => explorer.rename(id ?? undefined),
      setView: (view) => explorer.setView(view),
      setSort: (key, direction = null) => explorer.setSort(direction ? { key, direction } : { key }),
      setGroupBy: (by) => explorer.setGroupBy(by),
      toggleDetails: (show = null) => explorer.toggleDetails(show ?? undefined),
      setProgress: (progress) => explorer.setProgress(progress),
      setPinned: (pinned) => explorer.setPinned(pinned),
      newTab: (id = null) => explorer.newTab(id ?? MudExplorer.HOME),
      getState: () => explorer.getState(),
      focus: () => explorer.focus(),
      // A slice of a kept file, for .NET to stream (IJSStreamReference); length null reads to the end.
      readFile: async (token, index, offset = 0, length = null) => {
        const file = files.get(token)?.[index];
        if (!file) throw new Error(`MudTail: no file ${index} under "${token}"`);
        const end = length == null ? file.size : Math.min(file.size, offset + length);
        return new Uint8Array(await file.slice(offset, end).arrayBuffer());
      },
      releaseFiles: (token) => files.delete(token),
      dispose: () => { pending.forEach((p) => p.reject(new Error('disposed'))); pending.clear(); files.clear(); explorer.dispose(); },
    };
  },

  // Stat hero card built from data (Razor can also write the markup and use behavior("stat-bars", element)).
  statCard(host, options) {
    let card = createStatCard(options);
    mountInto(host, card.element);
    return {
      element: card.element,
      update: (next) => { card.dispose(); card = createStatCard({ ...options, ...(next ?? {}) }); mountInto(host, card.element); },
      dispose: () => card.dispose(),
    };
  },

  spinner(host, options) {
    const el = createSpinner(options);
    mountInto(host, el);
    return { element: el, dispose: () => el.remove() };
  },

  // Loading overlay over `host` (any element; the overlay is appended as its last child, absolutely positioned).
  // Handle: show(), hide(), setText(text), isVisible(), dispose().
  loading(host, options) {
    if (!(host instanceof Element)) throw new Error('MudTail: "loading" needs the element to cover as its host.');
    return showLoading(host, options);
  },

  button(host, options, emit) {
    const el = createMudButton({ ...options, onClick: () => emit('click') });
    mountInto(host, el);
    return { element: el, dispose: () => el.remove() };
  },

  autocomplete(host, options, emit, dotNetRef) {
    const abort = new AbortController();
    // options.remote: the items come from .NET, [JSInvokable] Search(q, skip, take) -> { items: [{ key, text }], hasMore }.
    const search = options.remote && dotNetRef
      ? (q, { skip, take }) => dotNetRef.invokeMethodAsync('Search', q, skip, take)
      : undefined;
    const el = createAutocomplete({
      ...options,
      search,
      signal: abort.signal,
      onSelect: (value) => emit('select', value),
      onChange: (values) => emit('change', values),
    });
    mountInto(host, el);
    const api = el.mudAutocomplete;
    return {
      element: el,
      setValue: (value) => { if (api) api.setValue(value); else el.querySelector('input').value = value ?? ''; },
      setValues: (values) => api?.setValues(values),
      setDisabled: (value) => api?.setDisabled(value),
      reset: () => api?.reset(),
      focus: () => api?.focus(),
      dispose: () => { abort.abort(); el.remove(); },
    };
  },

  tabs(host, options, emit) {
    const tabs = new MudTabs({ ...options, onChange: (id) => emit('change', id) });
    mountInto(host, tabs.getElement());
    return tabs;
  },

  accordion(host, options) {
    const accordion = new MudAccordion(options);
    mountInto(host, accordion.getElement());
    return accordion;
  },

  // Drawers and dialogs live on <body>, so `host` is ignored.
  drawer(_host, options, emit) {
    return new MudDrawer({ ...options, onOpen: () => emit('open'), onClose: () => emit('close') });
  },

  dialog(_host, options, emit) {
    // Actions coming from C# carry an id instead of a callback: { id, label, color, variant, keepOpen }.
    const actions = (options?.actions || []).map((a) => ({
      ...a,
      onClick: (_e, dialog) => {
        emit('action', a.id ?? a.label);
        if (!a.keepOpen) dialog.close();
      },
    }));
    return new MudDialog({ ...options, actions, onClose: () => emit('close') });
  },

  /**
   * Data table. options: { columns, data?, rowKey?, selection?, pageSize?, pageSizes?, caption?, dense?, striped?,
   *   initialQuery?, rowClick?: boolean, server?: { url, headers?, credentials?, debounce? }, serverSide?: boolean }
   * Columns come from C#, so format is a preset name ('number', 'currency:EUR', 'date', …), not a function.
   *
   * serverSide: true → for every page the table raises OnMudEvent("query", { requestId, query, filter }), where
   * filter is the PaginationFilter shape { keyword, searchFields, pageNumber, pageSize, orderBy, filters }.
   * Call the API with it (your authorised HttpClient / ApiClient) and answer with
   * setPage(requestId, items, totalCount), or failPage(requestId, message). Answers to stale requests are ignored.
   * server.url instead fetches the endpoint directly (GET with the PaginationFilter query string).
   * Other events: "rowclick" (row) when rowClick is true, "selection" (keys).
   */
  datatable(host, options, emit) {
    const pending = new Map();
    let seq = 0;
    let server = null;
    if (options.server?.url) {
      server = { url: options.server.url, headers: options.server.headers, credentials: options.server.credentials, debounce: options.server.debounce };
    } else if (options.serverSide) {
      server = {
        debounce: options.debounce,
        fetch: (query, signal) => new Promise((resolve, reject) => {
          const requestId = ++seq;
          pending.set(requestId, { resolve, reject });
          signal.addEventListener('abort', () => {
            pending.delete(requestId);
            reject(new DOMException('Superseded by a newer query', 'AbortError'));
          });
          emit('query', { requestId, query, filter: toPaginationFilter(query) });
        }),
      };
    }

    // Editing. crud: 'dotnet' (default) asks .NET to save: OnMudEvent("create" | "update" | "delete", { requestId, … }),
    // answered with completeEdit(requestId, savedRow) or failEdit(requestId, message, errors). 'rest' uses server.url
    // (POST / PUT / DELETE); 'client' only changes the rows in the browser.
    const edits = new Map();
    const ask = (name, payload) => new Promise((resolve, reject) => {
      const requestId = ++seq;
      edits.set(requestId, { resolve, reject });
      emit(name, { requestId, ...payload });
    });
    const crud = options.crud ?? 'dotnet';
    const hooks = crud === 'dotnet' ? {
      onCreate: (model) => ask('create', { model }),
      onUpdate: (model, row) => ask('update', { model, key: String(row?.[options.rowKey ?? 'id'] ?? '') }),
      onDelete: (row) => ask('delete', { key: String(row?.[options.rowKey ?? 'id'] ?? ''), row }),
    } : {};

    const table = new MudDataTable({
      ...options,
      ...hooks,
      server,
      data: options.data ?? [],
      onRowClick: options.rowClick ? (row) => emit('rowclick', row) : null,
      onSelectionChange: (keys) => emit('selection', keys),
      onAction: (action, row, t) => emit('action', { action, key: t.getKeyOf(row), row }),
      onRowsChange: (change) => emit('changed', change),
    });
    mountInto(host, table.getElement());

    const settle = (requestId, fn) => {
      const p = pending.get(requestId);
      if (!p) return false; // stale or unknown: a newer query replaced it
      pending.delete(requestId);
      fn(p);
      return true;
    };

    return {
      setData: (rows) => table.setData(rows ?? []),
      setPage: (requestId, items, totalCount) => settle(requestId, (p) => p.resolve({ items: items ?? [], totalCount: totalCount ?? 0 })),
      failPage: (requestId, message) => settle(requestId, (p) => p.reject(new Error(message || 'The data could not be loaded.'))),
      reload: () => table.reload(),
      setQuery: (partial) => table.setQuery(partial ?? {}),
      getQuery: () => table.getQuery(),
      getSelection: () => table.getSelection(),
      clearSelection: () => table.clearSelection(),
      openCreate: (defaults) => { table.openCreate(defaults ?? {}); },
      openEdit: (key) => { table.openEdit(String(key)); },
      deleteRow: (key, confirm = true) => { table.deleteRow(String(key), { confirm }); },
      addRow: (row) => table.addRow(row),
      updateRow: (row) => table.updateRow(row),
      removeRow: (key) => table.removeRow(key),
      completeEdit: (requestId, row = null) => {
        const p = edits.get(requestId);
        if (!p) return false;
        edits.delete(requestId);
        p.resolve(row ?? undefined);
        return true;
      },
      failEdit: (requestId, message, errors = null) => {
        const p = edits.get(requestId);
        if (!p) return false;
        edits.delete(requestId);
        const error = new Error(message || '');
        if (errors) error.errors = errors; // { Field: "message" } or ValidationProblemDetails' { Field: ["message"] }
        p.reject(error);
        return true;
      },
      dispose: () => { pending.clear(); edits.forEach((p) => p.reject(new Error('disposed'))); edits.clear(); table.dispose(); },
    };
  },

  /**
   * IDE docking layout. Panel content is a Blazor component mounted with Blazor's JS root components, so Blazor
   * owns that DOM and the dock never moves Blazor-rendered markup. In Program.cs register each panel component:
   *   builder.RootComponents.RegisterForJavaScript<ExplorerPanel>("app-explorer");
   * and give it the parameters it receives:  [Parameter] public string PanelId / PanelKey / Title { get; set; }
   *
   * options: {
   *   storageKey?: string,                       save / restore in localStorage
   *   layout?: object | string,                  default arrangement, in the shape serialize() produces
   *   blockKey?: string,                         panel kind used by "add a block"
   *   definitions: [{ key, title, icon?, allowMultiple?, document?, component }]
   * }
   * Events: OnMudEvent("layout", json) after every change.
   */
  async dock(host, options, emit) {
    const { MudDock, DockModel } = await load('dock');
    const blazor = globalThis.Blazor;
    const definitions = (options.definitions ?? []).map(({ component, ...def }) => ({
      ...def,
      render(panel) {
        const element = document.createElement('div');
        element.className = 'mud-dock-blazor';
        if (!component || typeof blazor?.rootComponents?.add !== 'function') {
          element.textContent = component
            ? `Blazor root components are not available, so "${component}" cannot be shown.`
            : `No component is set for "${def.key}".`;
          return element;
        }
        let handle = null;
        let disposed = false;
        blazor.rootComponents.add(element, component, { panelId: panel.id, panelKey: panel.key, title: panel.title })
          .then((h) => { if (disposed) h.dispose(); else handle = h; })
          .catch((e) => {
            element.textContent = `"${component}" failed to load: ${e?.message ?? e}`;
            console.error('[MudTail] dock panel component failed', component, e);
          });
        return { element, dispose: () => { disposed = true; handle?.dispose(); } };
      },
    }));

    const dock = new MudDock({
      definitions,
      defaultLayout: options.layout ? DockModel.layoutFrom(options.layout) : null,
      blockKey: options.blockKey ?? 'note',
      storageKey: options.storageKey ?? null,
    });
    mountInto(host, dock.getElement());
    const m = dock.model;
    const off = m.on('change', () => emit('layout', m.serialize()));
    const firstGroup = () => m.groups[0]?.id ?? '';

    return {
      addPanel: (key, groupId = null, zone = 'Center') => m.addPanel(key, groupId || firstGroup(), zone),
      show: (key) => m.show(key),
      openDocument: (key, title, state = {}) => m.openDocument(key, title, state ?? {})?.id ?? null,
      close: (panelId) => m.close(panelId),
      activate: (panelId) => { const g = m.groupOf(panelId); if (g) m.activate(g.id, panelId); },
      getPanelState: (panelId) => ({ ...(m.findPanel(panelId)?.state ?? {}) }),
      setPanelState: (panelId, name, value) => {
        const p = m.findPanel(panelId);
        if (!p) return;
        p.state[name] = String(value);
        emit('layout', m.serialize());
      },
      resetLayout: () => m.resetLayout(),
      clearSaved: () => dock.clearSaved(),
      serialize: () => m.serialize(),
      load: (json) => m.load(json),
      dispose: () => { off(); dock.dispose(); },
    };
  },

  /**
   * The Content Designer's canvas (MudCanvas). The blocks are Blazor's, rendered one of two ways:
   *   - component: a JS root component mounted in the frame (RegisterForJavaScript<PostCanvas>("app-post-canvas")),
   *     receiving [Parameter] CanvasId; or
   *   - adopt: true, when the host itself is Blazor markup with the canvas parts, which MudCanvas only decorates:
   *       <div class="mud-canvas" @ref="host"><div class="mud-canvas__viewport"><div class="mud-canvas__frame">
   *         <div class="mud-canvas__content">…blocks…</div></div><div class="mud-canvas__overlay"></div></div></div>
   *     (no style or dir on the frame: MudCanvas sets them.)
   * Blocks carry data-block-id, data-block-type, data-container, data-empty, data-has-text, data-can-h (VsDock's
   * PostBlockNode attributes); palette items data-block-new="<key>" with draggable="true".
   *
   * options: { device?: 'mobile'|'tablet'|'desktop'|'amp', devices?, zoom?, dir?: 'ltr'|'rtl'|null, lang?,
   *            showHidden?, autoDraggable?, component?, adopt?, parameters? }
   * Events, one per finished gesture: OnMudEvent("select" | "drop" | "resize" | "edit" | "move" | "delete" |
   * "command" | "zoom", payload) — the payloads are listed in src/designer/MudCanvas.js.
   */
  async canvas(host, options, emit) {
    const { MudCanvas } = await load('designer');
    const { component, adopt, parameters, ...opts } = options;
    const canvas = new MudCanvas({ ...opts, element: adopt && host instanceof Element ? host : null, onGesture: emit });
    if (!adopt) mountInto(host, canvas.getElement());

    let root = null;
    let rootDisposed = false;
    if (component) {
      const blazor = globalThis.Blazor;
      if (typeof blazor?.rootComponents?.add !== 'function') {
        canvas.content.textContent = `Blazor root components are not available, so "${component}" cannot be shown.`;
      } else {
        const canvasId = `mud-canvas-${Math.random().toString(36).slice(2, 10)}`;
        blazor.rootComponents.add(canvas.content, component, { canvasId, ...(parameters ?? {}) })
          .then((h) => { if (rootDisposed) h.dispose(); else root = h; })
          .catch((e) => {
            canvas.content.textContent = `"${component}" failed to load: ${e?.message ?? e}`;
            console.error('[MudTail] canvas component failed', component, e);
          });
      }
    }

    return {
      setDevice: (device) => canvas.setDevice(device),
      getDevice: () => canvas.getDevice(),
      setZoom: (zoom) => canvas.setZoom(zoom),
      getZoom: () => canvas.getZoom(),
      zoomIn: () => canvas.zoomIn(),
      zoomOut: () => canvas.zoomOut(),
      resetZoom: () => canvas.resetZoom(),
      setDirection: (dir, lang = null) => canvas.setDirection(dir || null, lang || null),
      setShowHidden: (show) => canvas.setShowHidden(!!show),
      select: (id) => canvas.select(id || null),
      getSelected: () => canvas.getSelected(),
      reveal: (id) => canvas.reveal(id),
      refresh: () => canvas.refresh(),
      /** Re-applies parameters to the Blazor root component (component mode). */
      setParameters: (p) => root?.setParameters(p ?? {}),
      dispose: () => { rootDisposed = true; root?.dispose(); canvas.dispose(); },
    };
  },

  /**
   * A code editor: Monaco, self-hosted, loaded on first use (a textarea until then, and for good when it fails).
   * options: { value?, language?, readOnly?, schema? (JSON Schema object or string), languages? (true | ids | false),
   *            minimap?, wordWrap?, fontSize?, ariaLabel?, debounce? }
   * Events: OnMudEvent("change", text) after typing stops; OnMudEvent("language", id) from the picker;
   *         OnMudEvent("ready", true | false) once Monaco runs (false: the textarea stayed).
   */
  async monaco(host, options, emit) {
    const { MudMonaco } = await load('monaco');
    const editor = new MudMonaco({
      ...options,
      onChange: (text) => emit('change', text),
      onLanguage: (lang) => emit('language', lang),
    });
    mountInto(host, editor.getElement());
    editor.ready.then((ok) => emit('ready', ok));
    return {
      getValue: () => editor.getValue(),
      setValue: (text) => editor.setValue(text),
      getLanguage: () => editor.getLanguage(),
      setLanguage: (lang) => editor.setLanguage(lang),
      setReadOnly: (readOnly) => editor.setReadOnly(!!readOnly),
      setSchema: (schema) => editor.setSchema(schema ?? null),
      getProblems: () => editor.getProblems(),
      format: () => editor.format(),
      focus: () => editor.focus(),
      isMonaco: () => !!editor.editor,
      dispose: () => editor.dispose(),
    };
  },
};

/** Puts a button (ElementReference) in or out of its busy state: spinner, disabled, aria-busy. */
export function buttonLoading(button, loading = true) {
  setButtonLoading(button, !!loading);
}

/** The page-top loader: pageLoading(true) starts it (calls nest), pageLoading(false) finishes it; a number sets 0–100. */
export function pageLoading(state = true) {
  if (typeof state === 'number') pageLoader.set(state);
  else if (state) pageLoader.start();
  else pageLoader.done();
}

/**
 * Creates a component and returns its handle.
 * @param {'button'|'autocomplete'|'tabs'|'accordion'|'drawer'|'dialog'|'dock'|'datatable'|'progress'|'progressCircular'|'spinner'|'loading'|'tree'|'statCard'|'imageEditor'|'explorer'|'canvas'|'monaco'} name
 * @param {Element|null} host   Blazor ElementReference to render into (ignored for drawer/dialog)
 * @param {object} options      The component's options (see the component's constructor)
 * @param {object|null} dotNetRef DotNetObjectReference that receives OnMudEvent(name, payload)
 */
export function create(name, host, options = {}, dotNetRef = null) {
  const factory = factories[name];
  if (factory) {
    // The large components come back as a promise (their code loads first); JS interop awaits it like a value.
    const handle = factory(host, options || {}, emitter(dotNetRef), dotNetRef);
    return typeof handle?.then === 'function' ? handle.then(track) : track(handle);
  }
  if (hasComponent(name)) return track(buildComponent(name, host, options || {}, emitter(dotNetRef))); // from a plugin
  throw new Error(`MudTail: unknown component "${name}". Known: ${componentNames().join(', ')}`);
}

/** Names create() accepts: the built-in components, then those plugins added. */
export function componentNames() {
  return [...Object.keys(factories), ...pluginComponentNames()];
}

reserveComponentNames(Object.keys(factories));

// ───────────── large components, loaded on first use ─────────────
//
// Each area is its own chunk (JS) and, where it has one, its own stylesheet pack (css/packs/<area>.css next to the
// core stylesheet). A host that serves the packs says where with setPackBase(url); without it the area's styles are
// expected to be on the page already (e.g. the full mudtail.css).

const AREAS = {
  dock: () => import('./areas/dock.js'),
  tree: () => import('./areas/tree.js'),
  'image-editor': () => import('./areas/image-editor.js'),
  explorer: () => import('./areas/explorer.js'),
  designer: () => import('./areas/designer.js'),
  monaco: () => import('./areas/monaco.js').then((mod) => { mod.setMonacoWorkerBase(monacoBase); return mod; }),
};

// The stylesheet packs each area needs (the explorer uses the tree view; the canvas shows pages styled by the
// editorial tokens). 'editorial' has no code: loadPack('editorial') on pages that render tokens.
const AREA_PACKS = {
  dock: ['dock'],
  tree: ['tree'],
  'image-editor': ['image-editor'],
  explorer: ['tree', 'explorer'],
  designer: ['designer', 'editorial'],
  monaco: ['monaco'],
};

let monacoBase = null;

/**
 * Where Monaco's workers are served ('…/js/monaco/': editor.worker.js, json.worker.js, css.worker.js,
 * html.worker.js). Call it before the first editor; null leaves the language services without workers (the editor
 * still edits and colours, without validation).
 */
export function setMonacoBase(url) {
  monacoBase = url ? String(url).replace(/\/?$/, '/') : null;
}

let packBase = null;
const packLinks = new Map(); // pack -> Promise<void>

/** Where the stylesheet packs are served ('…/css/packs/'); null: they are already on the page. */
export function setPackBase(url) {
  packBase = url ? String(url).replace(/\/?$/, '/') : null;
}

/** Adds a stylesheet pack to the page once (resolves when it has loaded, or failed: the component still works). */
export function loadPack(name) {
  if (!packBase) return Promise.resolve();
  if (!packLinks.has(name)) {
    packLinks.set(name, new Promise((resolve) => {
      const link = document.createElement('link');
      link.rel = 'stylesheet';
      link.href = `${packBase}${name}.min.css`;
      link.dataset.mudPack = name;
      link.addEventListener('load', () => resolve(), { once: true });
      link.addEventListener('error', () => { console.warn(`MudTail: stylesheet pack "${name}" could not be loaded`); resolve(); }, { once: true });
      document.head.appendChild(link);
    }));
  }
  return packLinks.get(name);
}

/** Removes the stylesheet packs this module added (module teardown). */
export function removePacks() {
  document.querySelectorAll('link[data-mud-pack]').forEach((l) => l.remove());
  packLinks.clear();
}

/**
 * Loads a large component's code (and its styles): 'dock' | 'tree' | 'image-editor' | 'explorer' | 'designer' | 'monaco'. Resolves to its
 * exports (MudDock, MudTreeView, MudImageEditor, MudExplorer, …), for module scripts that use the classes directly.
 */
export async function load(area) {
  const loader = AREAS[area];
  if (!loader) throw new Error(`MudTail: unknown area "${area}". Known: ${Object.keys(AREAS).join(', ')}`);
  const [mod] = await Promise.all([loader(), ...(AREA_PACKS[area] ?? []).map(loadPack)]);
  return mod;
}

/** The areas load() accepts. */
export function areaNames() {
  return Object.keys(AREAS);
}

// Runtime-loaded plugins get the core API as ctx.mudtail, plus load() for the large components.
setPluginHost({ ...coreApi, load, loadPack });

// ───────────── plugins ─────────────
//
//   var info = await mud.InvokeAsync<PluginInfo>("loadPlugin", "./_content/My.Plugins/countdown.plugin.js", new { locale = "ar" });
//   var countdown = await mud.InvokeAsync<IJSObjectReference>("create", "countdown", host, options, dotNetRef);
//   var api = await mud.InvokeAsync<IJSObjectReference>("plugin", "countdown");   // the plugin's own API
//   await api.InvokeVoidAsync("pauseAll");

/**
 * Loads a plugin module from a URL (resolved against the page: "./_content/<Assembly>/x.plugin.js") and installs it
 * with `options`. Resolves to { name, version, description, components, behaviors } once its setup has finished.
 * Loading the same plugin again is harmless.
 */
export async function loadPlugin(url, options = null) {
  const record = await libLoadPlugin(url, options ?? {});
  return describePlugin(record.name);
}

/** Loads several plugins in order: [{ url, options }] (or plain URLs). Resolves to their descriptions. */
export async function loadPlugins(list) {
  const out = [];
  for (const entry of list ?? []) out.push(typeof entry === 'string' ? await loadPlugin(entry) : await loadPlugin(entry.url, entry.options));
  return out;
}

/** The plugin's API object (what its setup() returned), for C# to call: InvokeAsync<IJSObjectReference>("plugin", name). */
export function plugin(name) {
  if (!hasPlugin(name)) throw new Error(`MudTail: plugin "${name}" is not installed`);
  return getPlugin(name) ?? {};
}

/** Calls one method of a plugin's API: InvokeAsync<T>("invokePlugin", "countdown", "remaining", "match-1"). */
export function invokePlugin(name, method, ...args) {
  const target = plugin(name);
  if (typeof target[method] !== 'function') throw new Error(`MudTail: plugin "${name}" has no method "${method}"`);
  return target[method](...args);
}

/** The installed plugins: [{ name, version, description, components, behaviors }]. */
export function plugins() {
  return listPlugins();
}

/** Removes a plugin and everything it registered (its live components too). force also removes plugins that need it. */
export function removePlugin(name, force = false) {
  return libRemovePlugin(name, { force: !!force });
}

/** Raises OnMudEvent("plugin", { action: 'installed' | 'removed', name, version }). Returns a handle with dispose(). */
export function observePlugins(dotNetRef) {
  const emit = emitter(dotNetRef);
  const installed = (e) => emit('plugin', { action: 'installed', ...e.detail });
  const removed = (e) => emit('plugin', { action: 'removed', ...e.detail });
  window.addEventListener('mud-plugin-installed', installed);
  window.addEventListener('mud-plugin-removed', removed);
  return track({ dispose: () => { window.removeEventListener('mud-plugin-installed', installed); window.removeEventListener('mud-plugin-removed', removed); } });
}

function describePlugin(name) {
  return listPlugins().find((p) => p.name === name) ?? null;
}

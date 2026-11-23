// The Explorer's pure parts (no DOM): natural sorting, the selection model, the navigation history and the item
// formatting (sizes, types, icons). Tested in model.test.js; MudFileView and MudExplorer build on them.
//
// An item is { id, name, kind: 'folder' | 'file', size?, modified?, type?, contentType?, thumbnail?, …your data }.

import { formatNumber, formatDate, t } from '../core/i18n.js';

// ───────────── sorting ─────────────

const collators = new Map();
/** A locale-aware, numeric collator ("file 2" before "file 10", Arabic and Latin each in their own order). */
export function naturalCollator(locale) {
  const key = locale ?? '';
  if (!collators.has(key)) collators.set(key, new Intl.Collator(locale || undefined, { numeric: true, sensitivity: 'base', ignorePunctuation: false }));
  return collators.get(key);
}

/** Natural order of two names, like Windows' Explorer (numbers by value, case ignored). */
export function compareNames(a, b, locale) {
  return naturalCollator(locale).compare(String(a ?? ''), String(b ?? ''));
}

const time = (v) => (v == null ? -Infinity : new Date(v).getTime());

/**
 * Sorts items as Explorer does: folders first (in every column), then by `key` ('name' | 'modified' | 'type' | 'size'
 * or any property), ties broken by name. Returns a new array.
 */
export function sortItems(items, { key = 'name', direction = 'asc', locale, foldersFirst = true } = {}) {
  const sign = direction === 'desc' ? -1 : 1;
  const byName = (a, b) => compareNames(a.name, b.name, locale);
  const value = {
    name: byName,
    modified: (a, b) => time(a.modified) - time(b.modified),
    size: (a, b) => (a.size ?? -1) - (b.size ?? -1),
    type: (a, b) => compareNames(typeLabel(a), typeLabel(b), locale),
  }[key] ?? ((a, b) => {
    const x = a[key];
    const y = b[key];
    if (typeof x === 'number' || typeof y === 'number') return (x ?? -Infinity) - (y ?? -Infinity);
    return compareNames(x, y, locale);
  });
  return [...(items ?? [])].sort((a, b) => {
    if (foldersFirst && isFolder(a) !== isFolder(b)) return isFolder(a) ? -1 : 1;
    return sign * (value(a, b) || byName(a, b));
  });
}

/** Groups sorted items: 'type' ('Folders', 'PNG File'…), 'modified' (Today, Yesterday, Earlier this week, …) or 'kind'. */
export function groupItems(items, by, now = new Date()) {
  if (!by) return [{ key: null, label: null, items }];
  const groups = new Map();
  for (const item of items) {
    const { key, label, order } = groupOf(item, by, now);
    if (!groups.has(key)) groups.set(key, { key, label, order, items: [] });
    groups.get(key).items.push(item);
  }
  return [...groups.values()].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
}

function groupOf(item, by, now) {
  if (by === 'type') return isFolder(item) ? { key: 'folder', label: t('explorer.folders', {}, 'Folders'), order: -1 } : { key: typeLabel(item), label: typeLabel(item), order: 0 };
  if (by === 'kind') return isFolder(item) ? { key: 'folder', label: t('explorer.folders', {}, 'Folders'), order: 0 } : { key: 'file', label: t('explorer.files', {}, 'Files'), order: 1 };
  if (by === 'modified') {
    const bucket = dateBucket(item.modified, now);
    return { key: bucket, label: t(`explorer.when.${bucket}`, {}, WHEN[bucket]), order: Object.keys(WHEN).indexOf(bucket) };
  }
  const v = item[by] ?? '';
  return { key: String(v), label: String(v), order: 0 };
}

const WHEN = { today: 'Today', yesterday: 'Yesterday', thisWeek: 'Earlier this week', lastWeek: 'Last week', thisMonth: 'Earlier this month', thisYear: 'Earlier this year', older: 'A long time ago', unknown: 'Unknown' };

/** Explorer's date groups, relative to `now` (the week starts on Sunday, as getDay() counts). */
export function dateBucket(value, now = new Date()) {
  if (value == null) return 'unknown';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return 'unknown';
  const day = (x) => new Date(x.getFullYear(), x.getMonth(), x.getDate()).getTime();
  const days = Math.round((day(now) - day(d)) / 86_400_000);
  if (days <= 0) return 'today';
  if (days === 1) return 'yesterday';
  if (days < now.getDay() + 1) return 'thisWeek';
  if (days < now.getDay() + 8) return 'lastWeek';
  if (d.getFullYear() === now.getFullYear() && d.getMonth() === now.getMonth()) return 'thisMonth';
  if (d.getFullYear() === now.getFullYear()) return 'thisYear';
  return 'older';
}

// ───────────── formatting ─────────────

export const isFolder = (item) => item?.kind === 'folder';

/** The extension, lower case, without the dot ('' for none and for folders). ".gitignore" has none. */
export function extension(item) {
  if (isFolder(item)) return '';
  const name = String(item?.name ?? '');
  const dot = name.lastIndexOf('.');
  return dot > 0 && dot < name.length - 1 ? name.slice(dot + 1).toLowerCase() : '';
}

/**
 * Sizes as Explorer shows them. The Details view's Size column is always in KB, rounded up ("1 KB" for 1 byte,
 * "0 KB" for an empty file): `{ column: true }`. Elsewhere (status bar, details pane) the unit grows: "620 KB",
 * "4.2 MB", "1.5 GB".
 */
export function formatSize(bytes, { column = false } = {}) {
  if (bytes == null) return '';
  const kb = Math.ceil(bytes / 1024);
  if (column || bytes < 1024 ** 2) return t('explorer.size.kb', { n: formatNumber(kb) }, '{n} KB');
  const units = [['mb', 1024 ** 2], ['gb', 1024 ** 3], ['tb', 1024 ** 4]];
  let [unit, size] = units[0];
  for (const [u, s] of units) if (bytes >= s) [unit, size] = [u, s];
  const value = bytes / size;
  const n = formatNumber(value, { maximumFractionDigits: value < 100 ? 1 : 0 });
  return t(`explorer.size.${unit}`, { n }, `{n} ${unit.toUpperCase()}`);
}

/** Explorer's "Date modified" column: short date and time in the active locale. */
export function formatModified(value) {
  if (value == null) return '';
  return formatDate(value, { dateStyle: 'short', timeStyle: 'short' });
}

const KINDS = {
  image: ['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp', 'svg', 'avif', 'heic', 'tif', 'tiff', 'ico'],
  video: ['mp4', 'webm', 'mov', 'mkv', 'avi', 'm4v', 'wmv'],
  audio: ['mp3', 'wav', 'ogg', 'flac', 'm4a', 'aac', 'wma'],
  archive: ['zip', 'rar', '7z', 'tar', 'gz', 'bz2', 'xz'],
  pdf: ['pdf'],
  doc: ['doc', 'docx', 'odt', 'rtf'],
  sheet: ['xls', 'xlsx', 'ods', 'csv'],
  slides: ['ppt', 'pptx', 'odp'],
  code: ['js', 'ts', 'json', 'html', 'css', 'scss', 'cs', 'razor', 'xml', 'yml', 'yaml', 'md', 'sql', 'py', 'sh', 'ps1'],
  text: ['txt', 'log', 'ini'],
};
const KIND_OF = new Map(Object.entries(KINDS).flatMap(([kind, exts]) => exts.map((e) => [e, kind])));

/** 'folder' | 'image' | 'video' | 'audio' | 'archive' | 'pdf' | 'doc' | 'sheet' | 'slides' | 'code' | 'text' | 'file'. */
export function fileKind(item) {
  if (isFolder(item)) return 'folder';
  const byExt = KIND_OF.get(extension(item));
  if (byExt) return byExt;
  const ct = String(item?.contentType ?? '');
  if (ct.startsWith('image/')) return 'image';
  if (ct.startsWith('video/')) return 'video';
  if (ct.startsWith('audio/')) return 'audio';
  if (ct.startsWith('text/')) return 'text';
  return 'file';
}

/** The "Type" column: "File folder", "PNG File", "File" (an item's own `type` wins). */
export function typeLabel(item) {
  if (item?.type) return item.type;
  if (isFolder(item)) return t('explorer.type.folder', {}, 'File folder');
  const ext = extension(item);
  return ext ? t('explorer.type.ext', { ext: ext.toUpperCase() }, '{ext} File') : t('explorer.type.file', {}, 'File');
}

// ───────────── selection ─────────────

/**
 * Explorer's selection over an ordered list of ids: click, Ctrl+click (toggle), Shift+click (range from the anchor),
 * Ctrl+Shift+click (add the range), the keyboard (focus moves with or without selecting) and select all / none / invert.
 */
export class SelectionModel {
  constructor(ids = []) {
    this.ids = [...ids];
    this.selected = new Set();
    this.anchor = null;
    this.focus = null;
  }

  /** New order (sorting, a refresh): keeps the selection of the ids still there. */
  setIds(ids) {
    this.ids = [...ids];
    const present = new Set(this.ids);
    for (const id of [...this.selected]) if (!present.has(id)) this.selected.delete(id);
    if (!present.has(this.anchor)) this.anchor = null;
    if (!present.has(this.focus)) this.focus = this.ids[0] ?? null;
  }

  has(id) { return this.selected.has(id); }
  get size() { return this.selected.size; }
  /** The selected ids, in list order. */
  list() { return this.ids.filter((id) => this.selected.has(id)); }

  /** A pointer or keyboard pick: { ctrl, shift } as on the event. */
  pick(id, { ctrl = false, shift = false } = {}) {
    if (!this.ids.includes(id)) return;
    if (shift) {
      const range = this._range(this.anchor ?? this.focus ?? id, id);
      if (!ctrl) this.selected.clear();
      range.forEach((x) => this.selected.add(x));
    } else if (ctrl) {
      if (this.selected.has(id)) this.selected.delete(id);
      else this.selected.add(id);
      this.anchor = id;
    } else {
      this.selected = new Set([id]);
      this.anchor = id;
    }
    this.focus = id;
  }

  /** Right-click: an unselected item becomes the only selection; a selected one keeps the selection. */
  contextPick(id) {
    if (!this.selected.has(id)) this.pick(id);
    else this.focus = id;
  }

  /** Moves the focus by `delta` (rows × columns for the grid views) and selects like a click with the same keys. */
  moveFocus(delta, { ctrl = false, shift = false } = {}) {
    if (!this.ids.length) return null;
    const from = this.ids.indexOf(this.focus);
    // Nothing selected yet: the first press selects the focused item where it is, as in Explorer.
    if (from >= 0 && this.selected.size === 0 && !ctrl) return this.focusAt(from, { shift });
    const index = from < 0 ? 0 : Math.max(0, Math.min(this.ids.length - 1, from + delta));
    return this.focusAt(index, { ctrl, shift });
  }

  /** Focus the item at `index` (Home / End): Ctrl only moves the focus, Shift extends. */
  focusAt(index, { ctrl = false, shift = false } = {}) {
    const id = this.ids[index];
    if (id == null) return null;
    if (ctrl && !shift) this.focus = id;
    else this.pick(id, { shift });
    return id;
  }

  /** Ctrl+Space: toggles the focused item. */
  toggleFocused() { if (this.focus != null) this.pick(this.focus, { ctrl: true }); }

  set(ids) {
    this.selected = new Set(ids.filter((id) => this.ids.includes(id)));
    this.anchor = ids[0] ?? null;
    if (ids.length) this.focus = ids[ids.length - 1];
  }
  all() { this.selected = new Set(this.ids); }
  none() { this.selected.clear(); }
  invert() { this.selected = new Set(this.ids.filter((id) => !this.selected.has(id))); }

  _range(a, b) {
    const i = this.ids.indexOf(a);
    const j = this.ids.indexOf(b);
    if (i < 0 || j < 0) return [b];
    return this.ids.slice(Math.min(i, j), Math.max(i, j) + 1);
  }
}

/** Type-ahead: the next item whose name starts with what was typed (restarts after a pause), from after the focus. */
export function typeAhead(items, typed, focusId, locale) {
  const prefix = String(typed ?? '').toLocaleLowerCase(locale || undefined);
  if (!prefix || !items.length) return null;
  const start = Math.max(0, items.findIndex((i) => i.id === focusId));
  // One repeated letter cycles through the items that start with it, as in Explorer.
  const cycle = prefix.length > 1 && [...prefix].every((c) => c === prefix[0]);
  const wanted = cycle ? prefix[0] : prefix;
  const from = cycle || prefix.length === 1 ? start + 1 : start;
  for (let k = 0; k < items.length; k++) {
    const item = items[(from + k) % items.length];
    if (String(item.name).toLocaleLowerCase(locale || undefined).startsWith(wanted)) return item.id;
  }
  return null;
}

// ───────────── navigation history ─────────────

/** Back / forward, as in a browser: navigating somewhere new drops the forward list. Entries are location objects. */
export class NavHistory {
  constructor(max = 100) {
    this.max = max;
    this.back = [];
    this.forward = [];
    this.current = null;
  }

  /** Goes to `location` ({ id, search?, …}); the same place (id and search) as the current one only replaces it. */
  push(location) {
    if (this.current && this.current.id === location.id && (this.current.search ?? '') === (location.search ?? '')) { this.current = location; return; }
    if (this.current) this.back.push(this.current);
    if (this.back.length > this.max) this.back.shift();
    this.forward = [];
    this.current = location;
  }

  canGoBack() { return this.back.length > 0; }
  canGoForward() { return this.forward.length > 0; }

  goBack() {
    if (!this.back.length) return null;
    this.forward.push(this.current);
    this.current = this.back.pop();
    return this.current;
  }

  goForward() {
    if (!this.forward.length) return null;
    this.back.push(this.current);
    this.current = this.forward.pop();
    return this.current;
  }

  /** The recent locations, newest first (the back button's drop-down). */
  recent(limit = 10) { return [...this.back].reverse().slice(0, limit); }
}

/** Breadcrumb overflow: how many leading crumbs to fold into "«" so the rest fit `available` pixels. */
export function crumbsToFold(widths, available, overflowWidth = 32) {
  const total = widths.reduce((a, b) => a + b, 0);
  if (total <= available) return 0;
  let used = total + overflowWidth;
  let fold = 0;
  // Always keep the last crumb (the current folder).
  while (fold < widths.length - 1 && used > available) used -= widths[fold++];
  return fold;
}

/** A unique name in a folder: "New folder", "New folder (2)", … (case-insensitive, natural). */
export function uniqueName(base, taken, locale) {
  const keys = new Set([...taken].map((n) => String(n).toLocaleLowerCase(locale || undefined)));
  const dot = base.lastIndexOf('.');
  const [stem, ext] = dot > 0 ? [base.slice(0, dot), base.slice(dot)] : [base, ''];
  let name = base;
  for (let i = 2; keys.has(name.toLocaleLowerCase(locale || undefined)); i++) name = `${stem} (${i})${ext}`;
  return name;
}

const INVALID = /[\\/:*?"<>|\u0000-\u001f]/;
const RESERVED = /^(con|prn|aux|nul|com[0-9]|lpt[0-9])$/i;
/** Windows' naming rules (the server checks again): an error message, or null when the name is fine. */
export function nameError(name) {
  const value = String(name ?? '').trim().replace(/[ .]+$/, '');
  if (!value) return t('explorer.name.required', {}, 'Type a name.');
  if (INVALID.test(value)) return t('explorer.name.invalid', {}, 'A file name can\'t contain any of the following characters: \\ / : * ? " < > |');
  if (RESERVED.test(value.split('.')[0].trim())) return t('explorer.name.reserved', { name: value }, 'The name "{name}" is reserved.');
  if (value.length > 255) return t('explorer.name.long', {}, 'The name is too long.');
  return null;
}

/** On rename, select the stem only ("report" of "report.pdf"), as Explorer does; folders select everything. */
export function renameSelection(item) {
  const name = String(item?.name ?? '');
  if (isFolder(item)) return [0, name.length];
  const dot = name.lastIndexOf('.');
  return [0, dot > 0 ? dot : name.length];
}

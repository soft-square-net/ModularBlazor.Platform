// Components ▸ File explorer: Windows 11's File Explorer over an in-memory file system (a pretend server).
import { MudExplorer, MudFileView, MudContextMenu, showToast, t, getLocale } from 'mudtail';
import { componentPage } from './page.js';

// ───────── a small in-memory file system, standing in for the Storage API ─────────

const DAY = 86_400_000;
const now = Date.now();
let seq = 0;
const photo = (hue, label) => `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="320" height="220"><defs><linearGradient id="g" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue} 70% 62%)"/><stop offset="1" stop-color="hsl(${(hue + 50) % 360} 70% 38%)"/></linearGradient></defs><rect width="320" height="220" fill="url(#g)"/><circle cx="250" cy="60" r="26" fill="#fff" opacity=".8"/><path d="M0 220 110 110l60 60 40-30 110 80z" fill="#000" opacity=".25"/><text x="16" y="200" font-family="Segoe UI, sans-serif" font-size="20" fill="#fff">${label}</text></svg>`)}`;

function createDrive() {
  const nodes = new Map();
  const add = (parent, name, kind, extra = {}) => {
    const id = `n${++seq}`;
    nodes.set(id, { id, parent, name, kind, modified: now - (extra.age ?? 1) * DAY, created: now - 40 * DAY, owner: extra.owner ?? 'ana', ...extra });
    return id;
  };
  const root = add(null, t('demo.explorer.files', {}, 'Files'), 'folder', { icon: 'drive' });
  const users = add(null, t('demo.explorer.users', {}, 'Users'), 'folder', { icon: 'users' });
  const home = add(users, 'ana', 'folder');
  add(users, 'omar', 'folder', { owner: 'omar', deny: ['rename', 'delete', 'moveTo'] });
  const reports = add(root, t('demo.explorer.reports', {}, 'Reports'), 'folder', { age: 2 });
  const y2026 = add(reports, '2026', 'folder', { age: 0 });
  add(reports, '2025', 'folder', { age: 120 });
  const media = add(root, t('demo.explorer.media', {}, 'Media'), 'folder', { age: 5 });
  const photos = add(media, t('demo.explorer.photos', {}, 'Photos'), 'folder', { age: 1 });
  add(media, t('demo.explorer.videos', {}, 'Videos'), 'folder', { age: 9 });
  add(root, t('demo.explorer.archive', {}, 'Archive (read only)'), 'folder', { age: 400, deny: ['rename', 'delete', 'newFolder', 'upload', 'paste', 'moveTo', 'copyTo'] });
  add(y2026, 'تقرير الربع الأول.pdf', 'file', { size: 482_113, age: 0, contentType: 'application/pdf' });
  add(y2026, 'Q2 report.pdf', 'file', { size: 1_204_993, age: 1 });
  add(y2026, 'budget 10.xlsx', 'file', { size: 88_120, age: 3 });
  add(y2026, 'budget 2.xlsx', 'file', { size: 79_002, age: 3 });
  add(y2026, 'notes.txt', 'file', { size: 1_204, age: 6 });
  add(y2026, 'slides.pptx', 'file', { size: 5_300_221, age: 12 });
  add(reports, 'template.docx', 'file', { size: 44_000, age: 30 });
  add(reports, 'data.csv', 'file', { size: 980_000, age: 2 });
  add(reports, 'export.zip', 'file', { size: 24_400_000, age: 45 });
  add(media, 'intro.mp4', 'file', { size: 88_400_000, age: 7 });
  add(media, 'jingle.mp3', 'file', { size: 4_200_000, age: 7 });
  [['harbour.jpg', 200], ['market.jpg', 20], ['old-town.png', 280], ['stadium.webp', 120], ['desert.jpg', 35], ['night.jpg', 240]]
    .forEach(([name, hue], i) => add(photos, name, 'file', { size: 1_400_000 + i * 310_000, age: i, thumbnail: photo(hue, name) }));
  add(home, 'cv.docx', 'file', { size: 32_000, age: 1 });
  add(home, 'app.js', 'file', { size: 12_400, age: 0 });

  const kids = (id) => [...nodes.values()].filter((n) => n.parent === id && !n.deleted);
  const pathOf = (id) => { const out = []; for (let n = nodes.get(id); n; n = nodes.get(n.parent)) out.unshift({ id: n.id, name: n.name }); return out; };
  const view = (n) => ({ ...n });
  const descendants = (id) => kids(id).flatMap((k) => [k, ...descendants(k.id)]);
  const delay = (v, ms = 120) => new Promise((r) => setTimeout(() => r(v), ms));
  const within = (a, b) => pathOf(b).some((p) => p.id === a); // a contains b
  const free = (parent, name) => {
    const taken = new Set(kids(parent).map((k) => k.name.toLocaleLowerCase()));
    if (!taken.has(name.toLocaleLowerCase())) return name;
    const dot = name.lastIndexOf('.');
    const [stem, ext] = dot > 0 ? [name.slice(0, dot), name.slice(dot)] : [name, ''];
    for (let i = 2; ; i++) { const n = `${stem} (${i})${ext}`; if (!taken.has(n.toLocaleLowerCase())) return n; }
  };
  const copyTree = (id, parent) => {
    const { id: _id, parent: _parent, name, kind, ...rest } = nodes.get(id);
    const copyName = parent === _parent ? name.replace(/(\.[^.]*)?$/, ` - ${t('demo.explorer.copy', {}, 'Copy')}$1`) : name;
    const copy = add(parent, free(parent, copyName), kind, { ...rest, modified: Date.now() });
    kids(id).forEach((k) => copyTree(k.id, copy));
    return copy;
  };

  const provider = {
    places: () => delay([root, users].map((id) => ({ id, name: nodes.get(id).name, icon: nodes.get(id).icon, hasChildren: kids(id).some((k) => k.kind === 'folder') }))),
    children: (id) => delay(kids(id).filter((k) => k.kind === 'folder').map((k) => ({ id: k.id, name: k.name, hasChildren: kids(k.id).some((x) => x.kind === 'folder') }))),
    list: (id, { search }) => {
      const folder = nodes.get(id);
      if (!folder) return Promise.reject(new Error(t('demo.explorer.notFound', {}, 'This folder no longer exists.')));
      const items = search
        ? descendants(id).filter((n) => n.name.toLocaleLowerCase(getLocale()).includes(search.toLocaleLowerCase(getLocale())))
        : kids(id);
      return delay({ folder: view(folder), path: pathOf(id), items: items.map(view) }, 220);
    },
    home: () => delay({ recent: [...nodes.values()].filter((n) => n.kind === 'file' && !n.deleted).sort((a, b) => b.modified - a.modified).slice(0, 6).map(view) }),
    gallery: () => delay({ items: [...nodes.values()].filter((n) => n.thumbnail && !n.deleted).map(view) }),
    resolvePath: async (text) => {
      const parts = text.split(/[\\/]/).map((s) => s.trim()).filter(Boolean);
      let level = [...nodes.values()].filter((n) => n.parent == null);
      let hit = null;
      for (const part of parts) {
        hit = level.find((n) => n.kind === 'folder' && n.name.toLocaleLowerCase() === part.toLocaleLowerCase());
        if (!hit) return null;
        level = kids(hit.id);
      }
      return hit?.id ?? null;
    },
    rename: async (item, name) => {
      await delay(null, 150);
      const n = nodes.get(item.id);
      if (kids(n.parent).some((k) => k.id !== n.id && k.name.toLocaleLowerCase() === name.toLocaleLowerCase()))
        return t('demo.explorer.taken', { name }, 'There is already a file with the same name "{name}" in this location.');
      n.name = name;
      n.modified = Date.now();
      return true;
    },
  };

  /** The host's side of every command. */
  const run = async (command, ctx) => {
    const folder = ctx.folder?.id;
    switch (command) {
      case 'newFolder': { const id = add(folder, free(folder, ctx.name), 'folder', { age: 0 }); return { refresh: true, select: [id] }; }
      case 'delete':
        ctx.items.forEach((i) => { nodes.get(i.id).deleted = true; });
        showToast(t(ctx.permanent ? 'demo.explorer.deletedForever' : 'demo.explorer.deleted', { count: ctx.items.length }, ctx.permanent ? 'Deleted {count} item(s) permanently' : 'Moved {count} item(s) to the recycle bin'), 'success');
        return { refresh: true };
      case 'paste': case 'moveTo': case 'copyTo': {
        const target = command === 'paste' ? folder : ctx.target.id;
        const move = command === 'moveTo' || (command === 'paste' && ctx.mode === 'cut');
        const done = [];
        for (const i of ctx.items) {
          const n = nodes.get(i.id);
          if (!n || within(n.id, target)) { showToast(t('demo.explorer.intoItself', {}, 'The destination folder is a subfolder of the source folder.'), 'error'); continue; }
          if (move) { if (n.parent !== target) { n.parent = target; n.name = free(target, n.name); } done.push(n.id); }
          else done.push(copyTree(n.id, target));
        }
        return { refresh: true, select: target === folder ? done : [] };
      }
      case 'upload': {
        const list = ctx.files ?? [];
        const ids = list.map((f) => add(folder, free(folder, f.name), 'file', { size: f.size, age: 0, contentType: f.type }));
        showToast(t('demo.explorer.uploaded', { count: list.length }, 'Uploaded {count} file(s) (in memory)'), 'success');
        return { refresh: true, select: ids };
      }
      case 'open': showToast(t('demo.explorer.opened', { name: ctx.items[0].name }, 'Open {name}: the host decides (preview, download, editor)'), 'info'); return null;
      case 'edit': showToast(t('demo.explorer.edit', { name: ctx.items[0].name }, 'Edit {name}: the Storage client opens it in the image editor'), 'info'); return null;
      default:
        showToast(`${command}: ${ctx.items.map((i) => i.name).join(', ') || ctx.folder?.name || ''}`, 'info');
        return null;
    }
  };

  return { provider, run, root, home, reports };
}

const sampleCode = `
  import { MudExplorer } from 'mudtail';

  const explorer = new MudExplorer({
    start: rootId,
    pinned: [{ id: reportsId, name: 'Reports' }],
    provider: {
      places: () => api.places(),                       // the tree's roots
      list: (id, { search }) => api.list(id, search),   // { folder, path, items }
      children: (id) => api.subfolders(id),             // the tree and the address bar's chevrons
      home: () => api.recent(), gallery: () => api.images(),
      resolvePath: (text) => api.find(text),            // typing a path in the address bar
      rename: (item, name) => api.rename(item.id, name),// true, or the message to show
    },
    // Every change is a command the host carries out; { refresh, select } updates the view.
    onCommand: async (command, ctx) => {
      if (command === 'newFolder') return { refresh: true, select: [await api.createFolder(ctx.folder.id, ctx.name)] };
      if (command === 'upload') { await uploads.start(ctx.files, ctx.folder.id); return { refresh: true }; }
      // paste ({ items, mode: 'cut' | 'copy' }), moveTo / copyTo (drag and drop), delete, share, security, …
    },
  });
  document.querySelector('#files').appendChild(explorer.getElement());`;

export const EXPLORER_PAGE = {
  id: 'explorer', label: 'File explorer', icon: 'folderOpen',
  render: () => componentPage({
    title: 'File explorer', kinds: ['JS', 'Blazor'],
    lead: 'Windows 11\'s File Explorer for the web: tabs, back / forward / up, an address bar of breadcrumbs (with a folder list behind every chevron, or type a path), search, the command bar, the navigation pane (Home, Gallery, Quick access, the folder tree), six views, the details pane and the status bar. Selection, the marquee, keyboard shortcuts, in-place rename, drag and drop (and files from your desktop) and the Windows 11 context menu work as on Windows, mirrored in Arabic. It knows nothing about where files live: a provider lists folders and the host carries out the commands.',
    examples: [
      {
        title: 'The explorer',
        description: 'A pretend server in memory. Try <kbd>Ctrl</kbd>/<kbd>Shift</kbd>+click and a marquee, <kbd>F2</kbd>, <kbd>Ctrl+C</kbd> / <kbd>Ctrl+V</kbd>, dragging items onto a folder (or the tree), right-click (and <strong>Show more options</strong>), <kbd>Ctrl+T</kbd> for a tab, <kbd>Alt+←</kbd>, <kbd>Alt+Shift+P</kbd> for the details pane, and a file from your desktop. <em>Archive</em> is read-only and <em>omar</em> can\'t be renamed or deleted: rights travel as <code>deny</code> lists on items and folders.',
        code: sampleCode,
        mount(el) {
          const drive = createDrive();
          const host = document.createElement('div');
          host.style.blockSize = '640px';
          el.appendChild(host);
          const explorer = new MudExplorer({
            start: drive.reports,
            pinned: [{ id: drive.reports, name: t('demo.explorer.reports', {}, 'Reports') }, { id: drive.home, name: 'ana' }],
            provider: drive.provider,
            onCommand: drive.run,
          });
          host.appendChild(explorer.getElement());
          return () => explorer.dispose();
        },
      },
      {
        title: 'The file view on its own',
        description: 'The item area is a component of its own (<code>MudFileView</code>): views, sorting by column with folders first, natural order (<em>budget 2</em> before <em>budget 10</em>, Arabic and Latin), groups, selection and rename. Here grouped by type, in tiles.',
        code: `
          import { MudFileView } from 'mudtail';
          const view = new MudFileView({ items, view: 'tiles', groupBy: 'type', onOpen: (item) => … });`,
        mount(el) {
          const items = [
            { id: 1, name: 'budget 10.xlsx', kind: 'file', size: 88_120, modified: now },
            { id: 2, name: 'budget 2.xlsx', kind: 'file', size: 79_002, modified: now - DAY },
            { id: 3, name: 'Drafts', kind: 'folder', modified: now - 3 * DAY },
            { id: 4, name: 'تقرير.pdf', kind: 'file', size: 482_113, modified: now - 9 * DAY },
            { id: 5, name: 'harbour.jpg', kind: 'file', size: 1_400_000, modified: now, thumbnail: photo(200, 'harbour') },
          ];
          const host = document.createElement('div');
          host.className = 'mud-explorer';
          host.style.blockSize = '300px';
          host.style.display = 'flex';
          const view = new MudFileView({
            items, view: 'tiles', groupBy: 'type',
            onOpen: (item) => showToast(item.name, 'info'),
            onContextMenu: ({ x, y, items: sel }) => MudContextMenu.open({
              x, y, quick: [{ id: 'copy', icon: 'copy', label: 'Copy' }, { id: 'delete', icon: 'delete', label: 'Delete' }],
              items: [{ id: 'open', label: 'Open', bold: true }, { separator: true }, { id: 'properties', label: 'Properties', icon: 'info' }],
              onSelect: (id) => showToast(`${id}: ${sel.map((i) => i.name).join(', ')}`, 'info'),
            }),
            onRename: async () => true,
          });
          host.appendChild(view.getElement());
          el.appendChild(host);
          return () => view.dispose();
        },
      },
    ],
    api: [
      {
        title: 'MudExplorer options',
        rows: [
          ['provider', '<code>places()</code>, <code>list(id, { search })</code> → <code>{ folder, path, items }</code>, <code>children(id)</code>; optional <code>home()</code>, <code>gallery()</code>, <code>resolvePath(text)</code>, <code>rename(item, name)</code>, <code>preview(item)</code>'],
          ['onCommand(command, ctx)', 'newFolder, upload, paste, moveTo, copyTo, rename, delete (<code>permanent</code> with Shift+Delete), open, edit, download, share, security, properties and your own menu ids. Return <code>{ refresh, select }</code>'],
          ['items', '<code>{ id, name, kind: "folder" | "file", size, modified, created, owner, contentType, thumbnail, deny: [commands], properties: [{ label, value }] }</code>'],
          ['can(command, ctx)', 'Narrows the defaults; or use <code>deny</code> lists on items and folders'],
          ['start, pinned, view, sort, groupBy, detailsPane', 'Initial state; <code>MudExplorer.HOME</code> / <code>GALLERY</code> are the virtual places'],
          ['menuItems(ctx), newItems(ctx)', 'Extra entries for the context menu and the New menu'],
        ],
      },
      {
        title: 'Methods',
        rows: [
          ['navigate(id, { search, newTab })', 'Go somewhere'],
          ['refresh({ select }), refreshTree(id)', 'After changes made elsewhere'],
          ['setView, setSort, setGroupBy, toggleDetails', 'Presentation'],
          ['newTab, closeTab, activateTab', 'Tabs'],
          ['select(ids), rename(id), getSelected(), getState()', 'Selection and state'],
          ['setProgress({ label, value }), setPinned(list)', 'Status bar progress (uploads), Quick access'],
        ],
      },
      {
        title: 'Keyboard',
        rows: [
          ['Alt+← / Alt+→ / Alt+↑, Backspace', 'Back, forward, up (mirrored in RTL)'],
          ['Ctrl+T, Ctrl+W, Ctrl+Tab', 'Tabs'],
          ['Ctrl+E / Ctrl+F, Alt+D / Ctrl+L, F5', 'Search, address bar, refresh'],
          ['Ctrl+Shift+N, F2, Delete, Shift+Delete', 'New folder, rename, delete'],
          ['Ctrl+X / C / V, Ctrl+A', 'Clipboard, select all'],
          ['Ctrl+Shift+2…8, Alt+Shift+P, Alt+Enter', 'Views, details pane, properties'],
          ['Shift+F10, Menu key', 'Context menu (Shift+F10 opens "more options")'],
        ],
      },
    ],
  }),
};

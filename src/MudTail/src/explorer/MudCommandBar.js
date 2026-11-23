// MudCommandBar: Windows 11's command bar. Buttons with an icon (and a label for the wide ones: New, Sort, View),
// drop-down buttons, toggles (Details), separators, and an "…" button that takes the commands that do not fit, so
// the bar never wraps. Arrow keys move along it (one tab stop, as a WAI-ARIA toolbar).
//
//   const bar = new MudCommandBar({
//     label: 'Commands',
//     items: [
//       { id: 'new', icon: 'plus', label: 'New', showLabel: true, menu: [{ id: 'newFolder', label: 'Folder', icon: 'folderPlus' }] },
//       { separator: true },
//       { id: 'cut', icon: 'cut', label: 'Cut' }, { id: 'copy', icon: 'copy', label: 'Copy', disabled: true },
//       { id: 'details', icon: 'detailsPane', label: 'Details', showLabel: true, toggle: true, pressed: false, end: true },
//     ],
//     moreMenu: () => [...],          // optional: "…" always shows, with these entries after the ones that did not fit
//     onCommand: (id, item) => …,      // a menu entry's id for drop-downs; for a toggle, item.pressed is the new state
//   });
//   bar.update('copy', { disabled: false });

import { getIconSvg } from '../core/icons.js';
import { t } from '../core/i18n.js';
import { MudContextMenu } from './MudContextMenu.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

export class MudCommandBar {
  constructor(o = {}) {
    this.o = o;
    this.items = (o.items ?? []).map((i) => ({ ...i }));
    this.el = document.createElement('div');
    this.el.className = 'mx-commandbar';
    this.el.setAttribute('role', 'toolbar');
    this.el.setAttribute('aria-label', o.label ?? t('explorer.commands', {}, 'Commands'));
    this.el.addEventListener('keydown', (e) => this._onKey(e));
    this.hidden = new Set();
    this._ro = typeof ResizeObserver === 'function' ? new ResizeObserver(() => this._fit()) : null;
    this._ro?.observe(this.el);
    this.render();
  }

  getElement() { return this.el; }

  setItems(items) { this.items = items.map((i) => ({ ...i })); this.render(); }

  /** Changes one command ({ disabled, pressed, label, icon, menu, hidden }) without rebuilding the bar. */
  update(id, patch) {
    const item = this.items.find((i) => i.id === id);
    if (!item) return;
    Object.assign(item, patch);
    const b = this.el.querySelector(`[data-id="${CSS.escape(id)}"]`);
    if (!b || 'label' in patch || 'icon' in patch || 'hidden' in patch) { this.render(); return; }
    b.disabled = !!item.disabled;
    if (item.toggle) { b.setAttribute('aria-pressed', String(!!item.pressed)); b.classList.toggle('is-pressed', !!item.pressed); }
  }

  /** Updates many commands at once: { id: patch }. */
  updateAll(patches) {
    let rebuild = false;
    for (const [id, patch] of Object.entries(patches ?? {})) {
      const item = this.items.find((i) => i.id === id);
      if (!item) continue;
      if ('label' in patch || 'icon' in patch || 'hidden' in patch) rebuild = true;
      Object.assign(item, patch);
    }
    if (rebuild) this.render();
    else for (const id of Object.keys(patches ?? {})) this.update(id, {});
  }

  render() {
    this.el.innerHTML = '';
    const start = document.createElement('div');
    start.className = 'mx-commandbar__group';
    const end = document.createElement('div');
    end.className = 'mx-commandbar__group mx-commandbar__group--end';
    for (const item of this.items) {
      if (item.hidden) continue;
      (item.end ? end : start).appendChild(this._button(item));
    }
    this.more = this._button({ id: '__overflow', icon: 'ellipsis', label: t('explorer.seeMore', {}, 'See more'), menu: async () => {
      const hidden = this._overflowItems();
      const extra = (await this.o.moreMenu?.()) ?? [];
      return hidden.length && extra.length ? [...hidden, { separator: true }, ...extra] : [...hidden, ...extra];
    } });
    this.more.classList.add('mx-commandbar__more');
    start.appendChild(this.more);
    this.el.append(start, end);
    this.measured = null;
    this._roving(this._buttons()[0]);
    requestAnimationFrame(() => this._fit());
  }

  dispose() {
    this._ro?.disconnect();
    this.el.remove();
  }

  _button(item) {
    if (item.separator) {
      const s = document.createElement('span');
      s.className = 'mx-commandbar__sep';
      s.setAttribute('role', 'separator');
      s.dataset.sep = '1';
      return s;
    }
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `mx-cmd${item.showLabel ? ' mx-cmd--labelled' : ''}${item.primary ? ' mx-cmd--primary' : ''}`;
    b.dataset.id = item.id;
    b.disabled = !!item.disabled;
    b.title = item.title ?? (item.shortcut ? `${item.label} (${item.shortcut})` : item.label);
    if (!item.showLabel) b.setAttribute('aria-label', item.label);
    if (item.toggle) { b.setAttribute('aria-pressed', String(!!item.pressed)); b.classList.toggle('is-pressed', !!item.pressed); }
    if (item.menu) b.setAttribute('aria-haspopup', 'menu');
    b.innerHTML = (item.icon ? getIconSvg(item.icon, 18) : '')
      + (item.showLabel ? `<span class="mx-cmd__label">${esc(item.label)}</span>` : '')
      + (item.menu && item.showLabel ? `<span class="mx-cmd__caret">${getIconSvg('chevronDown', 14)}</span>` : '');
    b.addEventListener('click', () => this._activate(item, b));
    return b;
  }

  async _activate(item, button) {
    if (item.disabled) return;
    if (item.menu) {
      const entries = typeof item.menu === 'function' ? await item.menu() : item.menu;
      MudContextMenu.open({
        anchor: button, items: entries, label: item.label, focusFirst: true,
        onSelect: (id, entry) => (entry.run ? entry.run() : this.o.onCommand?.(id, entry)),
      });
      return;
    }
    if (item.toggle) this.update(item.id, { pressed: !item.pressed });
    this.o.onCommand?.(item.id, item);
  }

  /** The commands hidden for lack of room, as menu entries (drop-downs become submenus). */
  _overflowItems() {
    return this.items.filter((i) => !i.separator && this.hidden.has(i.id)).map((i) => ({
      id: i.id, label: i.label, icon: i.icon, disabled: i.disabled, shortcut: i.shortcut,
      ...(i.toggle ? { checked: !!i.pressed } : {}),
      ...(i.menu ? { children: i.menu } : { run: () => this._activate(i, this.more) }),
    }));
  }

  /** Hides commands from the end of the first group until the bar fits; they go to "…". */
  _fit() {
    if (!this.el.isConnected) return;
    const start = this.el.querySelector('.mx-commandbar__group');
    const end = this.el.querySelector('.mx-commandbar__group--end');
    const nodes = [...start.children].filter((n) => n !== this.more);
    nodes.forEach((n) => { n.hidden = false; });
    this.hidden = new Set();
    this.more.hidden = !this.o.moreMenu;
    const room = this.el.clientWidth - (end?.offsetWidth ?? 0) - 8;
    if (start.scrollWidth <= room) return;
    this.more.hidden = false;
    for (let i = nodes.length - 1; i >= 0 && start.scrollWidth > room; i--) {
      nodes[i].hidden = true;
      if (nodes[i].dataset.id) this.hidden.add(nodes[i].dataset.id);
    }
    // A separator left at the edge of the visible part goes too.
    const visible = nodes.filter((n) => !n.hidden);
    if (visible.at(-1)?.dataset.sep) visible.at(-1).hidden = true;
  }

  // ───────── keyboard (one tab stop) ─────────

  _buttons() { return [...this.el.querySelectorAll('.mx-cmd')].filter((b) => !b.hidden && !b.closest('[hidden]')); }

  _roving(active) {
    for (const b of this._buttons()) b.tabIndex = b === active ? 0 : -1;
  }

  _onKey(e) {
    const buttons = this._buttons().filter((b) => !b.disabled);
    const at = buttons.indexOf(document.activeElement);
    if (at < 0) return;
    const rtl = getComputedStyle(this.el).direction === 'rtl';
    const step = { ArrowRight: rtl ? -1 : 1, ArrowLeft: rtl ? 1 : -1 }[e.key];
    let next = null;
    if (step) next = buttons[(at + step + buttons.length) % buttons.length];
    else if (e.key === 'Home') next = buttons[0];
    else if (e.key === 'End') next = buttons.at(-1);
    else if (e.key === 'ArrowDown' && document.activeElement.getAttribute('aria-haspopup')) { document.activeElement.click(); e.preventDefault(); return; }
    if (!next) return;
    e.preventDefault();
    this._roving(next);
    next.focus();
  }
}

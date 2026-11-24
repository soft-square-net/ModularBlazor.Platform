// MudAddressBar: Windows 11's address bar. The location's icon, then its path as breadcrumbs with a chevron between
// them; a chevron opens the folders inside the crumb before it. Crumbs that do not fit fold into "«" (at the start),
// the current folder always shows. A click on the empty part (or Alt+D / Ctrl+L, from the host) turns it into a text
// box with the whole path; Enter asks the host to go there, Esc goes back to the crumbs.
//
//   const bar = new MudAddressBar({
//     path: [{ id: 'root', name: 'Files' }, { id: 'f1', name: 'Reports' }],
//     icon: '<svg…>',                                    // the location's icon (markup)
//     onNavigate: (id) => …,
//     children: async (id) => [{ id, name }],            // the folders in a crumb, for its chevron (optional)
//     onSubmit: (text) => …,                             // the typed path (optional; without it the bar is not editable)
//     separator: '\\',                                   // between names in the text box
//   });

import { getIconSvg } from '../core/icons.js';
import { t } from '../core/i18n.js';
import { MudContextMenu } from './MudContextMenu.js';
import { crumbsToFold } from './model.js';
import { kindIconSvg } from './icons.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

export class MudAddressBar {
  constructor(o = {}) {
    this.o = { separator: '\\', ...o };
    this.path = o.path ?? [];
    this.el = document.createElement('div');
    this.el.className = 'mx-address';
    this.el.setAttribute('role', 'navigation');
    this.el.setAttribute('aria-label', t('explorer.address', {}, 'Address'));
    this.el.addEventListener('click', (e) => { if (e.target === this.el || e.target.classList.contains('mx-address__fill')) this.edit(); });
    this._ro = typeof ResizeObserver === 'function' ? new ResizeObserver(() => this._fold()) : null;
    this._ro?.observe(this.el);
    this.render();
  }

  getElement() { return this.el; }

  /** New location: [{ id, name }] from the top; `icon` replaces the location icon (markup). */
  setPath(path, icon = undefined) {
    this.path = path ?? [];
    if (icon !== undefined) this.o.icon = icon;
    this.editing = false;
    this.render();
  }

  /** The path as text ("Files\Reports\2026"). */
  text() { return this.path.map((p) => p.name).join(this.o.separator); }

  render() {
    this.el.classList.toggle('is-editing', !!this.editing);
    if (this.editing) return this._renderEdit();
    const icon = this.o.icon ?? kindIconSvg('folder', 16);
    this.el.innerHTML = `<span class="mx-address__icon">${icon}</span><ol class="mx-address__crumbs"></ol><span class="mx-address__fill"></span>`;
    const list = this.el.querySelector('ol');
    const overflow = this._crumbButton({ id: '__fold', name: '«' }, true);
    overflow.classList.add('mx-address__fold');
    overflow.setAttribute('aria-label', t('explorer.hiddenLocations', {}, 'Hidden locations'));
    overflow.hidden = true;
    const li = document.createElement('li');
    li.appendChild(overflow);
    list.appendChild(li);
    this.path.forEach((crumb, i) => {
      const item = document.createElement('li');
      item.className = 'mx-address__crumb';
      item.dataset.index = String(i);
      if (i > 0 || this.o.children) item.appendChild(this._chevron(this.path[i - 1] ?? null, i));
      const b = this._crumbButton(crumb, false);
      if (i === this.path.length - 1) b.setAttribute('aria-current', 'location');
      item.appendChild(b);
      list.appendChild(item);
    });
    // The last chevron: the folders in the current one.
    if (this.o.children && this.path.length) {
      const last = document.createElement('li');
      last.className = 'mx-address__crumb mx-address__crumb--tail';
      last.appendChild(this._chevron(this.path.at(-1), this.path.length));
      list.appendChild(last);
    }
    this._fold();
  }

  /** Switches to the text box, the path selected. */
  edit() {
    if (!this.o.onSubmit || this.editing) return;
    this.editing = true;
    this.render();
  }

  dispose() {
    this._ro?.disconnect();
    this.el.remove();
  }

  _renderEdit() {
    this.el.innerHTML = `<span class="mx-address__icon">${this.o.icon ?? kindIconSvg('folder', 16)}</span>`;
    const input = document.createElement('input');
    input.className = 'mx-address__input';
    input.value = this.text();
    input.setAttribute('aria-label', t('explorer.address', {}, 'Address'));
    input.spellcheck = false;
    input.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') { e.preventDefault(); const value = input.value.trim(); this.editing = false; this.render(); if (value && value !== this.text()) this.o.onSubmit(value); }
      else if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); this.editing = false; this.render(); }
    });
    input.addEventListener('blur', () => { if (this.editing) { this.editing = false; this.render(); } });
    this.el.appendChild(input);
    input.focus();
    input.select();
  }

  _crumbButton(crumb, fold) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'mx-address__btn';
    b.textContent = crumb.name;
    b.title = crumb.name;
    b.addEventListener('click', (e) => {
      e.stopPropagation();
      if (fold) this._openFolded(b);
      else this.o.onNavigate?.(crumb.id);
    });
    return b;
  }

  /** The chevron before crumb `index`: lists the folders of `parent` (null before the first crumb: no menu). */
  _chevron(parent, index) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'mx-address__chevron';
    b.tabIndex = -1;
    b.setAttribute('aria-label', parent ? t('explorer.foldersIn', { name: parent.name }, 'Folders in {name}') : '');
    b.innerHTML = getIconSvg('chevronRight', 14);
    if (!parent || !this.o.children) { b.disabled = true; return b; }
    b.addEventListener('click', async (e) => {
      e.stopPropagation();
      b.classList.add('is-open');
      const kids = await this.o.children(parent.id);
      const next = this.path[index];
      MudContextMenu.open({
        anchor: b, focusFirst: true,
        items: (kids ?? []).length
          ? kids.map((k) => ({ id: k.id, label: k.name, iconSvg: kindIconSvg('folder', 16), ...(next && k.id === next.id ? { bold: true } : {}) }))
          : [{ id: '__none', label: t('explorer.noFolders', {}, 'No folders'), disabled: true }],
        onSelect: (id) => this.o.onNavigate?.(id),
        onClose: () => b.classList.remove('is-open'),
      });
    });
    return b;
  }

  _openFolded(button) {
    const folded = this.path.slice(0, this.folded ?? 0).reverse();
    MudContextMenu.open({
      anchor: button, focusFirst: true,
      items: folded.map((c) => ({ id: c.id, label: c.name, iconSvg: kindIconSvg('folder', 16) })),
      onSelect: (id) => this.o.onNavigate?.(id),
    });
  }

  /** Folds the leading crumbs that do not fit into "«". */
  _fold() {
    const list = this.el.querySelector('.mx-address__crumbs');
    if (!list || !this.el.isConnected) return;
    const crumbs = [...list.querySelectorAll('.mx-address__crumb:not(.mx-address__crumb--tail)')];
    crumbs.forEach((c) => { c.hidden = false; });
    const fold = this.el.querySelector('.mx-address__fold');
    fold.hidden = true;
    const tail = list.querySelector('.mx-address__crumb--tail')?.offsetWidth ?? 0;
    const icon = this.el.querySelector('.mx-address__icon')?.offsetWidth ?? 0;
    const available = this.el.clientWidth - icon - tail - 24;
    this.folded = crumbsToFold(crumbs.map((c) => c.offsetWidth), available, 32);
    for (let i = 0; i < this.folded; i++) crumbs[i].hidden = true;
    fold.hidden = this.folded === 0;
  }
}

// MudContextMenu: a Windows 11 style menu. On top, an optional row of icon-only commands (cut, copy, rename, share,
// delete); below, the commands with icons, keyboard shortcuts, check marks, separators and submenus; at the bottom an
// optional "Show more options" that swaps in the classic, longer list. Also used as the drop-down of command bar
// buttons (New ▾, Sort ▾, View ▾) and of the address bar's chevrons.
//
//   MudContextMenu.open({
//     x: e.clientX, y: e.clientY,                       // or anchor: buttonElement (drops below it)
//     quick: [{ id: 'cut', icon: 'cut', label: 'Cut' }, { id: 'copy', icon: 'copy', label: 'Copy' }],
//     items: [{ id: 'open', label: 'Open', shortcut: 'Enter' }, { separator: true },
//             { id: 'sort', label: 'Sort by', icon: 'sort', children: [{ id: 'sort:name', label: 'Name', checked: true, radio: true }] }],
//     more: [/* the classic menu, behind "Show more options" (Shift+F10 opens it directly) */],
//     onSelect: (id, item) => …,
//   });
//
// Keyboard: ↑ ↓ Home End, Enter / Space, → opens a submenu (← in RTL), ← / Esc closes it, Esc closes the menu, the
// underlined access key is not supported (Windows 11 drops it too). One menu is open at a time.

import { getIconSvg } from '../core/icons.js';
import { t } from '../core/i18n.js';
import { isRtl } from '../core/direction.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
let current = null;
let uid = 0;

export class MudContextMenu {
  /** Opens a menu (closing any other) and returns it. */
  static open(options) {
    current?.close();
    current = new MudContextMenu(options);
    return current;
  }

  /** Closes the open menu, if any. */
  static closeAll() { current?.close(); }

  static get isOpen() { return !!current; }

  constructor(o) {
    this.o = { items: [], quick: [], more: null, ...o };
    this.rtl = o.rtl ?? isRtl();
    this.levels = []; // [{ el, items, index }] — level 0 is the menu, then open submenus
    this.closed = false;
    this.restoreFocus = document.activeElement;
    this._outside = (e) => { if (!this.levels.some((l) => l.el.contains(e.target)) && !this.o.anchor?.contains?.(e.target)) this.close(); };
    this._key = (e) => this._onKey(e);
    this._dismiss = () => this.close();
    this._render(this.o.showMore ? this.o.more : this.o.items, !this.o.showMore);
    queueMicrotask(() => {
      if (this.closed) return;
      document.addEventListener('pointerdown', this._outside, true);
      window.addEventListener('blur', this._dismiss);
      window.addEventListener('resize', this._dismiss);
      document.addEventListener('scroll', this._onScroll = (e) => { if (!this.levels.some((l) => l.el.contains(e.target))) this.close(); }, true);
    });
  }

  close({ restoreFocus = true } = {}) {
    if (this.closed) return;
    this.closed = true;
    this.levels.forEach((l) => l.el.remove());
    this.levels = [];
    document.removeEventListener('pointerdown', this._outside, true);
    document.removeEventListener('scroll', this._onScroll, true);
    window.removeEventListener('blur', this._dismiss);
    window.removeEventListener('resize', this._dismiss);
    if (current === this) current = null;
    if (restoreFocus && this.restoreFocus?.isConnected) this.restoreFocus.focus?.({ preventScroll: true });
    this.o.onClose?.();
  }

  // ───────── rendering ─────────

  _render(items, withQuick) {
    this.levels.forEach((l) => l.el.remove());
    this.levels = [];
    const el = this._menu(items, withQuick ? this.o.quick : [], withQuick && this.o.more?.length);
    document.body.appendChild(el);
    this.levels.push({ el, items: this._focusables(el), index: -1 });
    this._place(el, this.o.anchor ? this._anchorPoint() : { x: this.o.x ?? 0, y: this.o.y ?? 0 });
    // Keyboard opening (Shift+F10, the menu key, a button) focuses the first command; a pointer only the menu.
    if (this.o.focusFirst !== false) this._focus(0, 0);
    else el.focus({ preventScroll: true });
  }

  _menu(items, quick, withMore) {
    const el = document.createElement('div');
    el.className = 'mx-menu';
    el.setAttribute('role', 'menu');
    el.tabIndex = -1;
    el.dir = this.rtl ? 'rtl' : 'ltr';
    el.addEventListener('keydown', this._key);
    if (this.o.label) el.setAttribute('aria-label', this.o.label);

    if (quick?.length) {
      const row = document.createElement('div');
      row.className = 'mx-menu__quick';
      row.setAttribute('role', 'group');
      for (const q of quick) {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'mx-menu__quick-btn';
        b.setAttribute('role', 'menuitem');
        b.title = q.label;
        b.setAttribute('aria-label', q.label);
        b.disabled = !!q.disabled;
        b.innerHTML = getIconSvg(q.icon, 18);
        b.addEventListener('click', () => this._choose(q));
        row.appendChild(b);
      }
      el.appendChild(row);
    }

    for (const item of items ?? []) {
      if (item.separator) {
        const hr = document.createElement('div');
        hr.className = 'mx-menu__sep';
        hr.setAttribute('role', 'separator');
        el.appendChild(hr);
        continue;
      }
      if (item.header) {
        const h = document.createElement('div');
        h.className = 'mx-menu__header';
        h.textContent = item.header;
        el.appendChild(h);
        continue;
      }
      el.appendChild(this._item(item));
    }

    if (withMore) {
      const hr = document.createElement('div');
      hr.className = 'mx-menu__sep';
      el.appendChild(hr);
      el.appendChild(this._item({
        id: '__more', label: this.o.moreLabel ?? t('explorer.menu.more', {}, 'Show more options'), icon: 'openExternal', shortcut: 'Shift+F10',
        action: () => this._render(this.o.more, false),
      }));
    }
    return el;
  }

  _item(item) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `mx-menu__item${item.bold ? ' is-bold' : ''}${item.danger ? ' is-danger' : ''}`;
    b.id = `mx-menu-${++uid}`;
    const hasKids = !!(item.children?.length || typeof item.children === 'function');
    const check = item.checked != null;
    b.setAttribute('role', check ? (item.radio ? 'menuitemradio' : 'menuitemcheckbox') : 'menuitem');
    if (check) b.setAttribute('aria-checked', String(!!item.checked));
    if (hasKids) { b.setAttribute('aria-haspopup', 'menu'); b.setAttribute('aria-expanded', 'false'); }
    b.disabled = !!item.disabled;
    const lead = check
      ? `<span class="mx-menu__check">${item.checked ? (item.radio ? '<span class="mx-menu__dot"></span>' : getIconSvg('check', 16)) : ''}</span>`
      : `<span class="mx-menu__icon">${item.iconSvg ?? (item.icon ? getIconSvg(item.icon, 16) : '')}</span>`;
    b.innerHTML = `${lead}<span class="mx-menu__label">${esc(item.label)}</span>`
      + (item.shortcut ? `<span class="mx-menu__shortcut">${esc(item.shortcut)}</span>` : '')
      + (hasKids ? `<span class="mx-menu__arrow">${getIconSvg('chevronRight', 14)}</span>` : '');
    b._item = item;
    if (hasKids) {
      let hover;
      b.addEventListener('pointerenter', () => { hover = setTimeout(() => this._openSub(b), 250); });
      b.addEventListener('pointerleave', () => clearTimeout(hover));
      b.addEventListener('click', () => this._openSub(b, true));
    } else {
      b.addEventListener('click', () => this._choose(item));
      b.addEventListener('pointerenter', () => this._closeSubsAbove(this._levelOf(b)));
    }
    return b;
  }

  async _openSub(button, focus = false) {
    const level = this._levelOf(button);
    this._closeSubsAbove(level);
    const kids = typeof button._item.children === 'function' ? await button._item.children() : button._item.children;
    if (this.closed || !kids?.length) return;
    const el = this._menu(kids, [], false);
    el.classList.add('mx-menu--sub');
    document.body.appendChild(el);
    button.setAttribute('aria-expanded', 'true');
    this.levels.push({ el, items: this._focusables(el), index: -1, opener: button });
    const r = button.getBoundingClientRect();
    this._place(el, { x: this.rtl ? r.left + 4 : r.right - 4, y: r.top - 4 }, { sub: true, opener: r });
    if (focus) this._focus(this.levels.length - 1, 0);
  }

  _closeSubsAbove(level) {
    while (this.levels.length - 1 > level) {
      const l = this.levels.pop();
      l.opener?.setAttribute('aria-expanded', 'false');
      l.el.remove();
    }
  }

  _choose(item) {
    if (item.disabled) return;
    if (item.action) { item.action(); return; }
    this.close();
    this.o.onSelect?.(item.id, item);
  }

  // ───────── positioning ─────────

  _anchorPoint() {
    const r = this.o.anchor.getBoundingClientRect();
    return { x: this.rtl ? r.right : r.left, y: r.bottom + 4, anchor: r };
  }

  /** Places the menu at the point (its inline start there), flipping and clamping to stay in the viewport. */
  _place(el, { x, y, anchor }, { sub = false, opener = null } = {}) {
    el.style.left = '0px';
    el.style.top = '0px';
    const w = el.offsetWidth;
    const h = el.offsetHeight;
    const vw = window.innerWidth;
    const vh = window.innerHeight;
    let left = this.rtl ? x - w : x;
    if (left + w > vw - 4) left = sub && opener ? opener.left - w + 4 : vw - w - 4;
    if (left < 4) left = sub && opener ? opener.right - 4 : 4;
    let top = y;
    if (top + h > vh - 4) top = anchor ? anchor.top - h - 4 : Math.max(4, vh - h - 4);
    el.style.left = `${Math.max(4, Math.min(left, vw - w - 4))}px`;
    el.style.top = `${Math.max(4, top)}px`;
    el.classList.add('is-open');
  }

  // ───────── keyboard ─────────

  _focusables(el) { return [...el.querySelectorAll('.mx-menu__quick-btn, .mx-menu__item')]; }
  _levelOf(node) { return this.levels.findIndex((l) => l.el.contains(node)); }

  _focus(level, index) {
    const l = this.levels[level];
    if (!l) return;
    const enabled = l.items.filter((b) => !b.disabled);
    if (!enabled.length) { l.el.focus({ preventScroll: true }); return; }
    const target = enabled[(index + enabled.length) % enabled.length];
    l.index = enabled.indexOf(target);
    target.focus({ preventScroll: true });
  }

  _onKey(e) {
    const level = this._levelOf(e.target);
    const l = this.levels[level];
    if (!l) return;
    const enabled = l.items.filter((b) => !b.disabled);
    const at = enabled.indexOf(document.activeElement);
    const inward = this.rtl ? 'ArrowLeft' : 'ArrowRight';
    const outward = this.rtl ? 'ArrowRight' : 'ArrowLeft';
    const quickRow = document.activeElement?.classList.contains('mx-menu__quick-btn');
    switch (e.key) {
      case 'ArrowDown': this._focus(level, at + 1); break;
      case 'ArrowUp': this._focus(level, at < 0 ? -1 : at - 1); break;
      case 'Home': this._focus(level, 0); break;
      case 'End': this._focus(level, -1); break;
      case 'Escape': if (level > 0) { const opener = l.opener; this._closeSubsAbove(level - 1); opener?.focus(); } else this.close(); break;
      case 'Tab': this.close(); return;
      case 'F10': if (e.shiftKey && this.o.more?.length) this._render(this.o.more, false); else return; break;
      default:
        if (e.key === inward) {
          if (quickRow) this._focus(level, at + 1);
          else if (document.activeElement?._item?.children) this._openSub(document.activeElement, true);
          else return;
        } else if (e.key === outward) {
          if (quickRow) this._focus(level, at - 1);
          else if (level > 0) { const opener = l.opener; this._closeSubsAbove(level - 1); opener?.focus(); }
          else return;
        } else return;
    }
    e.preventDefault();
    e.stopPropagation();
  }
}

// MudCanvas: the WYSIWYG design surface of the Content Designer, ported from VsDock's post designer
// (wwwroot/js/post-designer.js). It shows a page in a device frame and turns pointer and keyboard gestures on its
// blocks into one call per finished gesture: hit-testing, markers and live previews happen here while the pointer
// moves, and the host (plain JS or Blazor) only hears the result.
//
//   const canvas = new MudCanvas({ device: 'tablet', onGesture: (name, payload) => … });
//   host.appendChild(canvas.getElement());
//   canvas.content.innerHTML = '<article data-block-id="root" data-container="true">…</article>';
//
// Blocks are ordinary elements carrying the attributes VsDock's PostBlockNode renders:
//   data-block-id        the block's id (required)
//   data-block-type      shown on the selection chip (data-block-label wins when present)
//   data-container       "true": other blocks can be dropped inside it
//   data-empty           "true": a container without children (always a drop target, shows a hint)
//   data-has-text        "true": double-click edits its text in place (plain text)
//   data-can-h           "true": the bottom handle sets a height
//   draggable="true"     on every block but the root, so it can be dragged (autoDraggable sets it for you)
// Palette items anywhere on the page are `[data-block-new="<key>"][draggable="true"]`.
//
// Gestures (onGesture(name, payload); OnMudEvent from Blazor):
//   select  { id }                                  a click (id null: nothing selected)
//   drop    { kind: 'move' | 'new', id, key, parentId, index, position, targetId }
//   resize  { id, span, height }                    span: columns (0 = unchanged), height: CSS px (0 = unchanged)
//   edit    { id, text }                            in-place text editing committed (Enter, or focus left)
//   move    { id, by }                              Alt + ↑ / ↓ (-1 / 1)
//   delete  { id }                                  Delete / Backspace on the selection
//   command { name: 'undo' | 'redo' | 'duplicate', id }
//   zoom    { zoom }                                Ctrl + wheel (once the wheel rests), Ctrl + = / − / 0
import { isRtl } from '../core/direction.js';
import { t } from '../core/i18n.js';
import {
  DEVICES, ZOOM_MIN, ZOOM_MAX, clampZoom, stepZoom, wheelZoom, dropPosition, markerRect, dropIndex,
  gridTracks, spanFromDrag, heightFromDrag, keyCommand,
} from './geometry.js';

const esc = (s) => (globalThis.CSS?.escape ? CSS.escape(s) : String(s).replace(/["\\]/g, '\\$&'));
const BLOCK = '[data-block-id]';
const WHEEL_REST_MS = 180;

let dragging = null; // the drag in progress, shared by every canvas on the page: { kind, id?, key?, el?, from }

export class MudCanvas {
  /**
   * @param {object} [options]
   * @param {string} [options.device]           'mobile' | 'tablet' | 'desktop' | 'amp' (or a key of options.devices)
   * @param {Record<string, number>} [options.devices]  frame widths in CSS px
   * @param {number} [options.zoom]             percent, 25–300
   * @param {'ltr'|'rtl'|null} [options.dir]   the page's direction in the frame (null: the app's)
   * @param {string|null} [options.lang]        the page's language in the frame
   * @param {boolean} [options.showHidden]      blocks hidden in this view stay visible, faded (default true)
   * @param {boolean} [options.autoDraggable]   set draggable="true" on blocks (default true)
   * @param {Element|null} [options.element]    adopt this existing .mud-canvas markup instead of creating it
   * @param {(name: string, payload: any) => void} [options.onGesture]
   */
  constructor(options = {}) {
    this.options = options;
    this.devices = { ...DEVICES, ...(options.devices ?? {}) };
    this.onGesture = typeof options.onGesture === 'function' ? options.onGesture : () => {};
    this._selected = null;
    this._zoom = 100;
    this._raf = 0;
    this._wheelTimer = 0;
    this._editing = null;
    this._off = [];

    this._build(options.element ?? null);
    this.setDevice(options.device ?? 'desktop');
    this.setDirection(options.dir ?? null, options.lang ?? null);
    this.setShowHidden(options.showHidden !== false);
    this.setZoom(options.zoom ?? 100);
    this._listen();
  }

  // ───────────── DOM ─────────────

  _build(existing) {
    const part = (cls, parent, tag = 'div') => {
      let el = parent.querySelector(`:scope > .${cls}`);
      if (!el) {
        el = document.createElement(tag);
        el.className = cls;
        parent.appendChild(el);
      }
      return el;
    };
    this.el = existing ?? document.createElement('div');
    this.el.classList.add('mud-canvas');
    if (!this.el.hasAttribute('tabindex')) this.el.tabIndex = 0;
    this.el.setAttribute('role', 'region');
    this.el.setAttribute('aria-label', t('canvas.label', {}, 'Design canvas'));
    this.el.style.setProperty('--mud-canvas-empty', JSON.stringify(t('canvas.empty', {}, 'Drop blocks here')));
    this.viewport = part('mud-canvas__viewport', this.el);
    // The viewport is always LTR: scroll offsets and the overlay's coordinates stay simple. The frame carries the
    // page's own direction.
    this.viewport.dir = 'ltr';
    this.frame = part('mud-canvas__frame', this.viewport);
    this.frame.setAttribute('data-mb-page', '');
    /** The element the blocks are rendered into. */
    this.content = part('mud-canvas__content', this.frame);
    this.overlay = part('mud-canvas__overlay', this.viewport);
    this.overlay.setAttribute('aria-hidden', 'true');

    this.selBox = document.createElement('div');
    this.selBox.className = 'mud-canvas__selection';
    this.selBox.innerHTML =
      '<span class="mud-canvas__chip"></span>' +
      `<i class="mud-canvas__handle mud-canvas__handle--span" title="${t('canvas.resizeSpan', {}, 'Drag to change the column span')}"></i>` +
      `<i class="mud-canvas__handle mud-canvas__handle--height" title="${t('canvas.resizeHeight', {}, 'Drag to change the height')}"></i>`;
    this.dropBox = document.createElement('div');
    this.dropBox.className = 'mud-canvas__drop';
    this.overlay.append(this.selBox, this.dropBox);
  }

  getElement() { return this.el; }

  _blockEl(id) {
    return id ? this.content.querySelector(`[data-block-id="${esc(id)}"]`) : null;
  }

  _parentBlock(el) {
    const p = el.parentElement?.closest(BLOCK);
    return p && this.content.contains(p) ? p : null;
  }

  _rootBlock() {
    return this.content.querySelector(BLOCK);
  }

  /** Places an overlay box over a screen rectangle (the overlay scrolls with the viewport). */
  _place(box, r) {
    const v = this.viewport.getBoundingClientRect();
    box.style.left = `${r.left - v.left + this.viewport.scrollLeft}px`;
    box.style.top = `${r.top - v.top + this.viewport.scrollTop}px`;
    box.style.width = `${r.width}px`;
    box.style.height = `${r.height}px`;
  }

  // ───────────── public API ─────────────

  /** Shows the page at a device width: 'mobile' (375), 'tablet' (768), 'desktop' (1280), 'amp' (375). */
  setDevice(name) {
    const key = Object.hasOwn(this.devices, name) ? name : 'desktop';
    this.device = key;
    this.frame.dataset.device = key;
    this.frame.style.setProperty('--mud-canvas-width', `${this.devices[key]}px`);
    this._redrawSoon();
    return key;
  }

  getDevice() { return this.device; }

  /**
   * Sets the zoom (percent, 25–300). With an anchor { clientX, clientY } the point under it stays where it is
   * (Ctrl + wheel); without one the view keeps its centre.
   */
  setZoom(percent, anchor = null) {
    const next = clampZoom(percent, ZOOM_MIN, ZOOM_MAX);
    const z0 = this._zoom / 100;
    const z1 = next / 100;
    const vr = this.viewport.getBoundingClientRect();
    const px = anchor ? anchor.clientX - vr.left : vr.width / 2;
    const py = anchor ? anchor.clientY - vr.top : vr.height / 2;
    const fr = this.frame.getBoundingClientRect();
    const cx = (vr.left + px - fr.left) / z0; // the content point (CSS px) under the anchor
    const cy = (vr.top + py - fr.top) / z0;

    this._zoom = next;
    this.frame.style.zoom = String(z1);
    this.frame.style.setProperty('--mud-canvas-zoom', String(z1));

    if (z0 !== z1 && this.viewport.isConnected) {
      const fr1 = this.frame.getBoundingClientRect();
      this.viewport.scrollLeft += fr1.left + cx * z1 - (vr.left + px);
      this.viewport.scrollTop += fr1.top + cy * z1 - (vr.top + py);
    }
    this._redrawSoon();
    return next;
  }

  getZoom() { return this._zoom; }
  zoomIn() { return this.setZoom(stepZoom(this._zoom, 1)); }
  zoomOut() { return this.setZoom(stepZoom(this._zoom, -1)); }
  resetZoom() { return this.setZoom(100); }

  /** The page's direction inside the frame ('ltr' | 'rtl'), independent of the app; null follows the app. */
  setDirection(dir, lang = null) {
    if (dir === 'ltr' || dir === 'rtl') this.frame.dir = dir;
    else this.frame.removeAttribute('dir');
    if (lang) this.frame.lang = lang;
    else if (lang === null) this.frame.removeAttribute('lang');
    this._redrawSoon();
  }

  /** Hidden blocks (data-mb-hidden for this view) stay visible, faded, so they can still be selected. */
  setShowHidden(show) {
    if (show) this.frame.setAttribute('data-mb-reveal-hidden', '');
    else this.frame.removeAttribute('data-mb-reveal-hidden');
    this._redrawSoon();
  }

  /** Selects a block (null clears). Does not raise `select`: the host asked for it. */
  select(id) {
    this._selected = id ? String(id) : null;
    this._draw();
    this._watchSelected();
  }

  getSelected() { return this._selected; }

  /** Redraws the selection box (after the host changed the content without the canvas noticing). */
  refresh() { this._draw(); }

  /** Scrolls a block into view and selects it. */
  reveal(id) {
    const el = this._blockEl(id);
    if (!el) return false;
    el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    this.select(id);
    return true;
  }

  dispose() {
    if (this._disposed) return;
    this._disposed = true;
    this._off.forEach((off) => off());
    this._off = [];
    cancelAnimationFrame(this._raf);
    clearTimeout(this._wheelTimer);
    this._ro?.disconnect();
    this._mo?.disconnect();
    this._selRo?.disconnect();
    this._finishEdit?.(false);
    this.selBox.remove();
    this.dropBox.remove();
    if (!this.options.element) this.el.remove();
  }

  _emit(name, payload) {
    try { this.onGesture(name, payload); } catch (e) { console.error('[MudCanvas] gesture handler failed', e); }
  }

  // ───────────── selection box ─────────────

  _draw() {
    if (this._disposed) return;
    const el = this._blockEl(this._selected);
    const r = el?.getBoundingClientRect();
    if (!el || !r || (r.width === 0 && r.height === 0)) {
      this.selBox.style.display = 'none';
      return;
    }
    this._place(this.selBox, r);
    this.selBox.style.display = 'block';
    // The handles follow the block's direction: the span handle sits on its inline end.
    this.selBox.dir = isRtl(el) ? 'rtl' : 'ltr';
    this.selBox.firstChild.textContent = el.dataset.blockLabel || el.dataset.blockType || '';

    const isRoot = !this._parentBlock(el);
    const inGrid = !isRoot && el.parentElement && getComputedStyle(el.parentElement).display.includes('grid');
    this.selBox.querySelector('.mud-canvas__handle--span').style.display = inGrid ? '' : 'none';
    this.selBox.querySelector('.mud-canvas__handle--height').style.display = !isRoot && el.dataset.canH === 'true' ? '' : 'none';
  }

  // Blazor (or any host) patches the DOM after a gesture, so the box is redrawn whenever the content or the
  // selected element changes, not only when the host calls select().
  _redrawSoon() {
    if (this._raf || this._disposed) return;
    this._raf = requestAnimationFrame(() => {
      this._raf = 0;
      this._draw();
      this._watchSelected();
    });
  }

  _watchSelected() {
    const el = this._blockEl(this._selected);
    if (el === this._selWatched) return;
    this._selRo?.disconnect();
    this._selWatched = el;
    if (el && globalThis.ResizeObserver) {
      this._selRo ??= new ResizeObserver(() => this._redrawSoon());
      this._selRo.observe(el);
    }
  }

  // ───────────── drop marker ─────────────

  _showDrop(target) {
    this.dropBox.classList.toggle('mud-canvas__drop--bar', target.position !== 'inside');
    this._place(this.dropBox, target.rect);
    this.dropBox.style.display = 'block';
  }

  _hideDrop() {
    this.dropBox.style.display = 'none';
    this._dropTarget = null;
  }

  /** Where the drag would land at this pointer event, or null. */
  _resolve(e) {
    let el = e.target?.closest?.(BLOCK);
    if (!el || !this.content.contains(el)) el = this._rootBlock();
    if (!el || !dragging) return null;
    if (dragging.kind === 'move' && (el === dragging.el || dragging.el?.contains(el))) return null; // not into itself

    const r = el.getBoundingClientRect();
    const parentEl = this._parentBlock(el);
    const pos = dropPosition(r, { x: e.clientX, y: e.clientY }, {
      container: el.dataset.container === 'true',
      empty: el.dataset.empty === 'true',
      root: !parentEl,
    }, isRtl(el));
    if (!pos) return null;

    if (pos.position === 'inside') {
      return { parentId: el.dataset.blockId, index: -1, position: 'inside', targetId: el.dataset.blockId, rect: markerRect(r, null) };
    }
    if (!parentEl) return null;
    const siblings = [...parentEl.children].filter((c) => c.hasAttribute('data-block-id'));
    return {
      parentId: parentEl.dataset.blockId,
      index: dropIndex(pos.position, siblings.indexOf(el)),
      position: pos.position,
      targetId: el.dataset.blockId,
      rect: markerRect(r, pos.edge),
    };
  }

  // ───────────── events ─────────────

  _on(target, type, fn, opts) {
    target.addEventListener(type, fn, opts);
    this._off.push(() => target.removeEventListener(type, fn, opts));
  }

  _listen() {
    if (globalThis.ResizeObserver) {
      this._ro = new ResizeObserver(() => this._redrawSoon());
      this._ro.observe(this.content);
      this._ro.observe(this.viewport);
    }
    if (globalThis.MutationObserver) {
      this._mo = new MutationObserver(() => this._redrawSoon());
      this._mo.observe(this.content, { subtree: true, childList: true, characterData: true, attributes: true });
    }
    this._on(window, 'resize', () => this._redrawSoon());

    // Blocks can be dragged without the host rendering draggable="true" on each of them.
    if (this.options.autoDraggable !== false) {
      this._on(this.content, 'pointerover', (e) => {
        const el = e.target?.closest?.(BLOCK);
        if (el && !el.hasAttribute('draggable') && !el.isContentEditable && this._parentBlock(el)) el.setAttribute('draggable', 'true');
      });
    }

    this._on(this.content, 'click', (e) => this._onClick(e));
    this._on(this.viewport, 'click', (e) => {
      if (!this.content.contains(e.target) && !e.target.closest?.('.mud-canvas__handle')) this._userSelect(null);
    });

    // Drag and drop: palette items anywhere on the page, blocks inside this canvas.
    this._on(document, 'dragstart', (e) => this._onDragStart(e));
    this._on(document, 'dragover', (e) => this._onDragOver(e));
    this._on(document, 'drop', (e) => this._onDrop(e));
    this._on(document, 'dragend', () => this._endDrag());

    this._on(this.selBox, 'pointerdown', (e) => this._onHandleDown(e));
    this._on(this.content, 'dblclick', (e) => this._onDblClick(e));
    this._on(this.el, 'keydown', (e) => this._onKey(e));
    this._on(this.viewport, 'wheel', (e) => this._onWheel(e), { passive: false });
    this._on(this.viewport, 'pointerdown', (e) => this._onPanStart(e));
    this._on(this.viewport, 'auxclick', (e) => { if (e.button === 1) e.preventDefault(); });
  }

  _userSelect(id) {
    if ((id ?? null) === this._selected) return;
    this.select(id);
    this._emit('select', { id: this._selected });
  }

  _onClick(e) {
    if (this._editing) return;
    const el = e.target?.closest?.(BLOCK);
    // Links and buttons must not navigate or submit while designing.
    if (e.target?.closest?.('a, button, input[type="submit"]')) e.preventDefault();
    this.el.focus({ preventScroll: true });
    this._userSelect(el && this.content.contains(el) ? el.dataset.blockId : null);
  }

  _onDragStart(e) {
    const tgt = e.target;
    if (!tgt?.closest) return;
    const palette = tgt.closest('[data-block-new]');
    if (palette) {
      dragging = { kind: 'new', key: palette.dataset.blockNew };
    } else {
      const el = tgt.closest(BLOCK);
      if (!el || !this.content.contains(el) || !this._parentBlock(el)) return;
      dragging = { kind: 'move', id: el.dataset.blockId, el };
    }
    if (e.dataTransfer) {
      e.dataTransfer.effectAllowed = 'copyMove';
      e.dataTransfer.setData('text/plain', dragging.kind === 'new' ? dragging.key : dragging.id); // Firefox needs data
    }
    document.body.classList.add('mud-canvas-dragging');
  }

  _onDragOver(e) {
    if (!dragging) return;
    if (!this.viewport.contains(e.target)) {
      if (this._dropTarget) this._hideDrop();
      return;
    }
    const target = this._resolve(e);
    if (!target) { this._hideDrop(); return; }
    e.preventDefault(); // a valid drop target
    if (e.dataTransfer) e.dataTransfer.dropEffect = dragging.kind === 'new' ? 'copy' : 'move';
    this._dropTarget = target;
    this._showDrop(target);
  }

  _onDrop(e) {
    if (!dragging || !this._dropTarget || !this.viewport.contains(e.target)) return;
    e.preventDefault();
    const d = dragging;
    const tgt = this._dropTarget;
    this._endDrag();
    this._emit('drop', {
      kind: d.kind,
      id: d.kind === 'move' ? d.id : null,
      key: d.kind === 'new' ? d.key : null,
      parentId: tgt.parentId,
      index: tgt.index,
      position: tgt.position,
      targetId: tgt.targetId,
    });
  }

  _endDrag() {
    this._hideDrop();
    dragging = null;
    document.body.classList.remove('mud-canvas-dragging');
  }

  // ───────────── resizing (column span / height) ─────────────

  _onHandleDown(e) {
    const handle = e.target?.closest?.('.mud-canvas__handle');
    if (!handle || e.button !== 0 || !this._selected) return;
    const el = this._blockEl(this._selected);
    if (!el) return;
    e.preventDefault();
    e.stopPropagation();

    const spanHandle = handle.classList.contains('mud-canvas__handle--span');
    const r0 = el.getBoundingClientRect();
    const x0 = e.clientX;
    const y0 = e.clientY;
    // The frame is zoomed: rectangles and pointer deltas are screen pixels, computed sizes are CSS pixels.
    const z = (el.offsetWidth > 0 ? r0.width / el.offsetWidth : 1) || 1;
    const rtl = isRtl(el);
    let grid = null;
    if (spanHandle) {
      const cs = getComputedStyle(el.parentElement);
      grid = gridTracks(cs.gridTemplateColumns, cs.columnGap, r0.width / z);
    }
    let span = 0;
    let height = 0;
    const id = this._selected;

    try { handle.setPointerCapture?.(e.pointerId); } catch { /* not an active pointer (synthetic events): moves still arrive */ }
    document.body.classList.add(spanHandle ? 'mud-canvas-resizing-x' : 'mud-canvas-resizing-y');

    const move = (ev) => {
      if (spanHandle) {
        span = spanFromDrag(r0.width, ev.clientX - x0, z, grid, rtl);
        el.style.gridColumn = `span ${span} / span ${span}`; // live preview
      } else {
        height = heightFromDrag(r0.height, ev.clientY - y0, z);
        el.style.height = `${height}px`;
      }
      this._draw();
    };
    const up = () => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', up);
      handle.removeEventListener('pointercancel', up);
      document.body.classList.remove('mud-canvas-resizing-x', 'mud-canvas-resizing-y');
      // The host re-renders the block with its real tokens; the preview must not linger.
      el.style.gridColumn = '';
      el.style.height = '';
      this._draw();
      if (span || height) this._emit('resize', { id, span, height });
    };
    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', up);
    handle.addEventListener('pointercancel', up);
  }

  // ───────────── in-place text editing ─────────────

  _onDblClick(e) {
    const el = e.target?.closest?.(BLOCK);
    if (!el || !this.content.contains(el) || el.dataset.hasText !== 'true' || this._editing) return;
    e.preventDefault();
    this._userSelect(el.dataset.blockId);

    const original = el.textContent;
    // A host such as Blazor keeps a reference to THIS text node and patches it later. It goes back into the element
    // afterwards: a new node (el.textContent = …) would leave the host updating a detached one.
    const textNode = el.firstChild && el.firstChild.nodeType === 3 && el.childNodes.length === 1 ? el.firstChild : null;
    const multiline = el.tagName === 'P' || el.tagName === 'BLOCKQUOTE' || el.dataset.multiline === 'true';
    const wasDraggable = el.getAttribute('draggable');

    el.removeAttribute('draggable'); // a draggable element cannot place a text cursor
    el.contentEditable = 'plaintext-only';
    if (el.contentEditable !== 'plaintext-only') el.contentEditable = 'true';
    this._editing = el;
    el.focus();
    const range = document.createRange();
    range.selectNodeContents(el);
    const sel = window.getSelection();
    sel?.removeAllRanges();
    sel?.addRange(range);

    const finish = (commit) => {
      el.removeEventListener('blur', onBlur);
      el.removeEventListener('keydown', onKey);
      this._finishEdit = null;
      this._editing = null;
      const text = el.innerText.replace(/\n$/, '');
      el.removeAttribute('contenteditable');
      if (wasDraggable !== null) el.setAttribute('draggable', wasDraggable);
      if (textNode) {
        el.replaceChildren(textNode);
        textNode.nodeValue = original;
      } else {
        el.textContent = original;
      }
      this.el.focus({ preventScroll: true });
      if (commit && text !== original) this._emit('edit', { id: el.dataset.blockId, text });
    };
    const onBlur = () => finish(true);
    const onKey = (ev) => {
      ev.stopPropagation(); // the canvas' own keys (Delete, Alt + ↑) do not apply while typing
      if (ev.key === 'Escape') { ev.preventDefault(); finish(false); }
      else if (ev.key === 'Enter' && !ev.shiftKey && !multiline) { ev.preventDefault(); finish(true); }
    };
    el.addEventListener('blur', onBlur);
    el.addEventListener('keydown', onKey);
    this._finishEdit = finish;
  }

  // ───────────── keyboard ─────────────

  _onKey(e) {
    const tgt = e.target;
    if (tgt?.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(tgt?.tagName ?? '')) return;
    const cmd = keyCommand(e);
    if (!cmd) return;
    const id = this._selected;
    const isRoot = id && !this._parentBlock(this._blockEl(id) ?? this.content);
    switch (cmd.name) {
      case 'zoom':
        e.preventDefault();
        this._emit('zoom', { zoom: cmd.by === 0 ? this.resetZoom() : cmd.by > 0 ? this.zoomIn() : this.zoomOut() });
        return;
      case 'escape':
        if (!id) return;
        e.preventDefault();
        this._userSelect(null);
        return;
      case 'delete':
        if (!id || isRoot) return;
        e.preventDefault();
        this._emit('delete', { id });
        return;
      case 'move':
        if (!id || isRoot) return;
        e.preventDefault();
        this._emit('move', { id, by: cmd.by });
        return;
      default: // undo, redo, duplicate
        e.preventDefault();
        this._emit('command', { name: cmd.name, id });
    }
  }

  // ───────────── zoom & pan ─────────────

  _onWheel(e) {
    if (!e.ctrlKey && !e.metaKey) return;
    e.preventDefault(); // do not zoom the whole browser page
    const next = wheelZoom(this._zoom, e.deltaY, e.deltaMode);
    if (next === this._zoom) return;
    this.setZoom(next, e);
    // One call for the whole gesture: once the wheel (or the pinch) rests.
    clearTimeout(this._wheelTimer);
    this._wheelTimer = setTimeout(() => this._emit('zoom', { zoom: this._zoom }), WHEEL_REST_MS);
  }

  _onPanStart(e) {
    if (e.button !== 1) return;
    e.preventDefault(); // no auto-scroll circle
    const x0 = e.clientX;
    const y0 = e.clientY;
    const sl = this.viewport.scrollLeft;
    const st = this.viewport.scrollTop;
    this.viewport.classList.add('mud-canvas__viewport--panning');
    const move = (ev) => {
      this.viewport.scrollLeft = sl - (ev.clientX - x0);
      this.viewport.scrollTop = st - (ev.clientY - y0);
    };
    const up = () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      window.removeEventListener('pointercancel', up);
      this.viewport.classList.remove('mud-canvas__viewport--panning');
    };
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
  }
}

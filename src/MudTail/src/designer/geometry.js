// MudCanvas geometry: the pure rules behind the canvas gestures (no DOM), so they can be unit-tested.
// The canvas measures the DOM (getBoundingClientRect, computed grid tracks) and hands plain numbers to these.

/** Device frames: the CSS width each view is designed at. AMP pages are designed at the phone width. */
export const DEVICES = Object.freeze({ mobile: 375, tablet: 768, desktop: 1280, amp: 375 });

export const ZOOM_MIN = 25;
export const ZOOM_MAX = 300;
/** The steps the zoom buttons and Ctrl + / Ctrl − go through. */
export const ZOOM_STEPS = Object.freeze([25, 33, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300]);

export function clampZoom(value, min = ZOOM_MIN, max = ZOOM_MAX) {
  const n = Math.round(Number(value));
  if (!Number.isFinite(n)) return 100;
  return Math.max(min, Math.min(max, n));
}

/** The next step up (dir > 0) or down (dir < 0) from `current`. */
export function stepZoom(current, dir, steps = ZOOM_STEPS) {
  const z = clampZoom(current, steps[0], steps[steps.length - 1]);
  if (dir > 0) return steps.find((s) => s > z) ?? steps[steps.length - 1];
  if (dir < 0) return [...steps].reverse().find((s) => s < z) ?? steps[0];
  return z;
}

/**
 * The zoom after one Ctrl + wheel event (a trackpad pinch arrives the same way). Exponential, so a notch feels the
 * same at 50 % and at 200 %; always at least 1 % per event, so slow trackpads still move.
 * deltaMode: 0 pixels, 1 lines (Firefox mouse wheels), 2 pages.
 */
export function wheelZoom(current, deltaY, deltaMode = 0, min = ZOOM_MIN, max = ZOOM_MAX) {
  const cur = clampZoom(current, min, max);
  if (!deltaY) return cur;
  const rate = deltaMode === 1 ? 0.05 : deltaMode === 2 ? 0.5 : 0.0025;
  let next = Math.round(cur * Math.exp(-deltaY * rate));
  if (next === cur) next = cur + (deltaY < 0 ? 1 : -1);
  return clampZoom(next, min, max);
}

/**
 * The scroll change that keeps the content point under the pointer still while the zoom changes from z0 to z1
 * (factors, 1 = 100 %). `offset` is the pointer's distance from the frame's top-left corner, in screen pixels,
 * before the change.
 */
export function zoomScrollDelta(offset, z0, z1) {
  return { x: (offset.x / z0) * z1 - offset.x, y: (offset.y / z0) * z1 - offset.y };
}

/**
 * Where a dragged block lands when it is released at `point` over a block with the rectangle `rect`.
 *   block: { container?, empty?, root? }  as the block's data-container / data-empty attributes say
 *   rtl:   the block's writing direction. In RTL the right edge is the start of the line, so dropping on it means
 *          "before" and dropping on the left edge means "after".
 * Returns { position: 'inside' | 'before' | 'after', edge: 'left' | 'right' | 'top' | 'bottom' | null }:
 *   - over the middle of a container, or any root or empty container: inside it, at the end;
 *   - otherwise before or after the block, on the nearest edge.
 * Returns null for a non-container root (there is nothing to put it before or after).
 */
export function dropPosition(rect, point, block = {}, rtl = false) {
  const { container = false, empty = false, root = false } = block;
  const x = point.x;
  const y = point.y;
  if (container) {
    const bx = Math.min(24, rect.width * 0.25);
    const by = Math.min(24, rect.height * 0.25);
    const nearEdge = x - rect.left < bx || rect.right - x < bx || y - rect.top < by || rect.bottom - y < by;
    if (root || empty || !nearEdge) return { position: 'inside', edge: null };
  }
  if (root) return null;

  const d = { left: x - rect.left, right: rect.right - x, top: y - rect.top, bottom: rect.bottom - y };
  const edge = /** @type {'left'|'right'|'top'|'bottom'} */ (Object.keys(d).reduce((a, k) => (d[k] < d[a] ? k : a), 'left'));
  let before;
  if (edge === 'top') before = true;
  else if (edge === 'bottom') before = false;
  else before = (edge === 'left') !== rtl; // left = start in LTR, end in RTL
  return { position: before ? 'before' : 'after', edge };
}

/** The drop marker: the whole block for 'inside', else a 4 px bar on the edge the pointer is nearest. */
export function markerRect(rect, edge) {
  const r = { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
  switch (edge) {
    case 'left': return { left: r.left - 2, top: r.top, width: 4, height: r.height };
    case 'right': return { left: r.left + r.width - 2, top: r.top, width: 4, height: r.height };
    case 'top': return { left: r.left, top: r.top - 2, width: r.width, height: 4 };
    case 'bottom': return { left: r.left, top: r.top + r.height - 2, width: r.width, height: 4 };
    default: return r;
  }
}

/**
 * The index a dropped block gets among its new siblings (counted as they are now, the dragged block included).
 * `at` is the target's index among them.
 */
export function dropIndex(position, at) {
  if (position === 'inside') return -1; // at the end
  return position === 'before' ? at : at + 1;
}

/**
 * Parses a computed grid-template-columns ("120px 120px 120px") into the column count and the width of one
 * column plus one gap: the step the span handle snaps to. Widths are CSS pixels.
 */
export function gridTracks(templateColumns, columnGap = 0, fallbackWidth = 0) {
  const tracks = String(templateColumns || '').split(' ').filter((t) => t && t !== 'none');
  const gap = Number.parseFloat(String(columnGap)) || 0;
  const first = Number.parseFloat(tracks[0] ?? '') || fallbackWidth;
  return { columns: Math.max(1, tracks.length), unit: first + gap, gap };
}

/**
 * The column span while the inline-end handle is dragged.
 *   startWidth: the block's width when the drag began (screen pixels);  delta: pointer x movement (screen pixels)
 *   zoom: the frame's zoom factor (screen px per CSS px);  grid: gridTracks(...)
 *   rtl: in RTL the handle sits on the left edge, so moving left makes the block wider.
 */
export function spanFromDrag(startWidth, delta, zoom, grid, rtl = false) {
  const z = zoom > 0 ? zoom : 1;
  const width = (startWidth + (rtl ? -delta : delta)) / z;
  const span = Math.round((width + grid.gap) / (grid.unit || 1));
  return Math.max(1, Math.min(grid.columns, span));
}

/** The height while the bottom handle is dragged: CSS pixels, at least 16. */
export function heightFromDrag(startHeight, delta, zoom) {
  const z = zoom > 0 ? zoom : 1;
  return Math.max(16, Math.round((startHeight + delta) / z));
}

/**
 * The canvas command for a key press, or null when the canvas leaves the key alone.
 *   Delete / Backspace: delete the selected block;  Escape: clear the selection
 *   Alt + ↑ / ↓: move the selected block one place earlier / later among its siblings
 *   Ctrl + Z / Ctrl + Y (Ctrl + Shift + Z): undo / redo;  Ctrl + D: duplicate
 *   Ctrl + = / + / − / 0: zoom in / out / back to 100 %
 */
export function keyCommand(e) {
  const ctrl = !!(e.ctrlKey || e.metaKey);
  const key = String(e.key || '');
  const lower = key.toLowerCase();
  if (e.altKey && !ctrl && (key === 'ArrowUp' || key === 'ArrowDown')) return { name: 'move', by: key === 'ArrowUp' ? -1 : 1 };
  if (e.altKey) return null;
  if (!ctrl && (key === 'Delete' || key === 'Backspace')) return { name: 'delete' };
  if (!ctrl && key === 'Escape') return { name: 'escape' };
  if (!ctrl) return null;
  if (lower === 'z') return { name: e.shiftKey ? 'redo' : 'undo' };
  if (lower === 'y') return { name: 'redo' };
  if (lower === 'd') return { name: 'duplicate' };
  if (key === '+' || key === '=') return { name: 'zoom', by: 1 };
  if (key === '-' || key === '_') return { name: 'zoom', by: -1 };
  if (key === '0') return { name: 'zoom', by: 0 };
  return null;
}

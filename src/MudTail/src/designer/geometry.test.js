import test from 'node:test';
import assert from 'node:assert/strict';
import {
  DEVICES, clampZoom, stepZoom, wheelZoom, zoomScrollDelta, dropPosition, markerRect, dropIndex,
  gridTracks, spanFromDrag, heightFromDrag, keyCommand,
} from './geometry.js';

const rect = (left, top, width, height) => ({ left, top, width, height, right: left + width, bottom: top + height });

test('device frames are the agreed widths', () => {
  assert.deepEqual({ ...DEVICES }, { mobile: 375, tablet: 768, desktop: 1280, amp: 375 });
});

test('zoom stays between 25 and 300 %', () => {
  assert.equal(clampZoom(10), 25);
  assert.equal(clampZoom(900), 300);
  assert.equal(clampZoom('abc'), 100);
  assert.equal(clampZoom(99.6), 100);
});

test('zoom steps go to the next step, and stop at the ends', () => {
  assert.equal(stepZoom(100, 1), 110);
  assert.equal(stepZoom(100, -1), 90);
  assert.equal(stepZoom(104, 1), 110);
  assert.equal(stepZoom(104, -1), 100);
  assert.equal(stepZoom(300, 1), 300);
  assert.equal(stepZoom(25, -1), 25);
});

test('Ctrl + wheel zooms in on a wheel up and out on a wheel down, at least 1 % per event', () => {
  assert.ok(wheelZoom(100, -100) > 100);
  assert.ok(wheelZoom(100, 100) < 100);
  assert.equal(wheelZoom(100, -0.01), 101);
  assert.equal(wheelZoom(100, 0.01), 99);
  assert.equal(wheelZoom(300, -500), 300);
  assert.equal(wheelZoom(25, 500), 25);
  assert.equal(wheelZoom(100, 0), 100);
  // Firefox's line mode moves faster per unit than pixels
  assert.ok(wheelZoom(100, -3, 1) > wheelZoom(100, -3, 0));
});

test('zooming keeps the point under the pointer still', () => {
  // 200 px into the frame at 100 %: at 200 % that point is 400 px in, so the view scrolls 200 px further
  assert.deepEqual(zoomScrollDelta({ x: 200, y: 50 }, 1, 2), { x: 200, y: 50 });
  assert.deepEqual(zoomScrollDelta({ x: 200, y: 50 }, 2, 1), { x: -100, y: -25 });
});

test('drop: the middle of a container drops inside it; its edges drop before or after it', () => {
  const r = rect(0, 0, 400, 200);
  assert.deepEqual(dropPosition(r, { x: 200, y: 100 }, { container: true }), { position: 'inside', edge: null });
  assert.deepEqual(dropPosition(r, { x: 200, y: 5 }, { container: true }), { position: 'before', edge: 'top' });
  assert.deepEqual(dropPosition(r, { x: 200, y: 195 }, { container: true }), { position: 'after', edge: 'bottom' });
});

test('drop: an empty container or the root always takes the block inside', () => {
  const r = rect(0, 0, 400, 200);
  assert.equal(dropPosition(r, { x: 2, y: 100 }, { container: true, empty: true }).position, 'inside');
  assert.equal(dropPosition(r, { x: 2, y: 2 }, { container: true, root: true }).position, 'inside');
  assert.equal(dropPosition(r, { x: 2, y: 2 }, { root: true }), null);
});

test('drop: a leaf block takes the nearest edge', () => {
  const r = rect(100, 100, 300, 40);
  assert.deepEqual(dropPosition(r, { x: 200, y: 105 }), { position: 'before', edge: 'top' });
  assert.deepEqual(dropPosition(r, { x: 200, y: 138 }), { position: 'after', edge: 'bottom' });
  assert.deepEqual(dropPosition(r, { x: 102, y: 120 }), { position: 'before', edge: 'left' });
  assert.deepEqual(dropPosition(r, { x: 398, y: 120 }), { position: 'after', edge: 'right' });
});

test('drop: before and after flip on the inline axis in RTL, not on the block axis', () => {
  const r = rect(100, 100, 300, 40);
  assert.deepEqual(dropPosition(r, { x: 102, y: 120 }, {}, true), { position: 'after', edge: 'left' });
  assert.deepEqual(dropPosition(r, { x: 398, y: 120 }, {}, true), { position: 'before', edge: 'right' });
  assert.deepEqual(dropPosition(r, { x: 200, y: 105 }, {}, true), { position: 'before', edge: 'top' });
  assert.deepEqual(dropPosition(r, { x: 200, y: 138 }, {}, true), { position: 'after', edge: 'bottom' });
});

test('drop markers are a bar on the edge, or the whole block', () => {
  const r = rect(10, 20, 100, 50);
  assert.deepEqual(markerRect(r, 'left'), { left: 8, top: 20, width: 4, height: 50 });
  assert.deepEqual(markerRect(r, 'right'), { left: 108, top: 20, width: 4, height: 50 });
  assert.deepEqual(markerRect(r, 'top'), { left: 10, top: 18, width: 100, height: 4 });
  assert.deepEqual(markerRect(r, 'bottom'), { left: 10, top: 68, width: 100, height: 4 });
  assert.deepEqual(markerRect(r, null), { left: 10, top: 20, width: 100, height: 50 });
});

test('drop index: before takes the place, after the next one, inside appends', () => {
  assert.equal(dropIndex('before', 2), 2);
  assert.equal(dropIndex('after', 2), 3);
  assert.equal(dropIndex('inside', 2), -1);
});

test('grid tracks: the column count and one column plus a gap', () => {
  assert.deepEqual(gridTracks('100px 100px 100px 100px', '20px'), { columns: 4, unit: 120, gap: 20 });
  assert.deepEqual(gridTracks('none', 'normal', 300), { columns: 1, unit: 300, gap: 0 });
});

test('span handle: snaps to whole columns and stays within the grid', () => {
  const grid = gridTracks('100px 100px 100px 100px', '20px'); // a 4-column grid, 120 px per column
  assert.equal(spanFromDrag(100, 0, 1, grid), 1);
  assert.equal(spanFromDrag(100, 120, 1, grid), 2);
  assert.equal(spanFromDrag(100, 1000, 1, grid), 4);
  assert.equal(spanFromDrag(220, -500, 1, grid), 1);
  // at 50 % zoom the same 120 CSS px is 60 screen px
  assert.equal(spanFromDrag(50, 60, 0.5, grid), 2);
});

test('span handle: in RTL dragging to the left widens the block', () => {
  const grid = gridTracks('100px 100px 100px 100px', '20px');
  assert.equal(spanFromDrag(100, -120, 1, grid, true), 2);
  assert.equal(spanFromDrag(100, 120, 1, grid, true), 1);
});

test('height handle: CSS pixels, at least 16', () => {
  assert.equal(heightFromDrag(100, 50, 1), 150);
  assert.equal(heightFromDrag(100, 50, 2), 75);
  assert.equal(heightFromDrag(20, -100, 1), 16);
});

test('keyboard: moves, delete, escape, history and zoom', () => {
  assert.deepEqual(keyCommand({ key: 'ArrowUp', altKey: true }), { name: 'move', by: -1 });
  assert.deepEqual(keyCommand({ key: 'ArrowDown', altKey: true }), { name: 'move', by: 1 });
  assert.equal(keyCommand({ key: 'ArrowDown' }), null);
  assert.deepEqual(keyCommand({ key: 'Delete' }), { name: 'delete' });
  assert.deepEqual(keyCommand({ key: 'Backspace' }), { name: 'delete' });
  assert.deepEqual(keyCommand({ key: 'Escape' }), { name: 'escape' });
  assert.deepEqual(keyCommand({ key: 'z', ctrlKey: true }), { name: 'undo' });
  assert.deepEqual(keyCommand({ key: 'Z', ctrlKey: true, shiftKey: true }), { name: 'redo' });
  assert.deepEqual(keyCommand({ key: 'y', metaKey: true }), { name: 'redo' });
  assert.deepEqual(keyCommand({ key: 'd', ctrlKey: true }), { name: 'duplicate' });
  assert.deepEqual(keyCommand({ key: '=', ctrlKey: true }), { name: 'zoom', by: 1 });
  assert.deepEqual(keyCommand({ key: '-', ctrlKey: true }), { name: 'zoom', by: -1 });
  assert.deepEqual(keyCommand({ key: '0', ctrlKey: true }), { name: 'zoom', by: 0 });
  assert.equal(keyCommand({ key: 'a' }), null);
  assert.equal(keyCommand({ key: 'Delete', altKey: true }), null);
});

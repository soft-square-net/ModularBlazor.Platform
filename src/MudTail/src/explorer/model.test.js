import test from 'node:test';
import assert from 'node:assert/strict';
import {
  compareNames, sortItems, groupItems, dateBucket, extension, formatSize, fileKind, typeLabel, SelectionModel, typeAhead,
  NavHistory, crumbsToFold, uniqueName, nameError, renameSelection,
} from './model.js';

const f = (name, extra = {}) => ({ id: name, name, kind: 'file', ...extra });
const d = (name, extra = {}) => ({ id: name, name, kind: 'folder', ...extra });

test('compareNames: numbers by value, case ignored', () => {
  const names = ['file10.txt', 'File2.txt', 'file1.txt', 'file 3.txt'];
  assert.deepEqual([...names].sort((a, b) => compareNames(a, b, 'en')), ['file 3.txt', 'file1.txt', 'File2.txt', 'file10.txt']);
  assert.ok(compareNames('تقرير 2', 'تقرير 10', 'ar') < 0, 'Arabic names with numbers');
});

test('sortItems: folders first in every column, ties by name, descending keeps folders first', () => {
  const items = [f('b.png', { size: 10 }), d('Zeta'), f('a.txt', { size: 30 }), d('alpha'), f('c.txt', { size: 10 })];
  assert.deepEqual(sortItems(items).map((i) => i.name), ['alpha', 'Zeta', 'a.txt', 'b.png', 'c.txt']);
  assert.deepEqual(sortItems(items, { key: 'size', direction: 'desc' }).map((i) => i.name), ['Zeta', 'alpha', 'a.txt', 'c.txt', 'b.png']);
  assert.deepEqual(sortItems(items, { key: 'type' }).map((i) => i.name), ['alpha', 'Zeta', 'b.png', 'a.txt', 'c.txt']);
  assert.deepEqual(sortItems(items, { foldersFirst: false }).map((i) => i.name), ['a.txt', 'alpha', 'b.png', 'c.txt', 'Zeta']);
});

test('dateBucket: Explorer\'s date groups', () => {
  const now = new Date(2026, 10, 25, 15, 0); // Wednesday 25 November 2026
  assert.equal(dateBucket(new Date(2026, 10, 25, 1, 0), now), 'today');
  assert.equal(dateBucket(new Date(2026, 10, 24, 23, 0), now), 'yesterday');
  assert.equal(dateBucket(new Date(2026, 10, 22), now), 'thisWeek');
  assert.equal(dateBucket(new Date(2026, 10, 17), now), 'lastWeek');
  assert.equal(dateBucket(new Date(2026, 10, 3), now), 'thisMonth');
  assert.equal(dateBucket(new Date(2026, 1, 3), now), 'thisYear');
  assert.equal(dateBucket(new Date(2024, 1, 3), now), 'older');
  assert.equal(dateBucket(null, now), 'unknown');
});

test('groupItems: groups in Explorer\'s order', () => {
  const now = new Date(2026, 10, 25, 15, 0);
  const groups = groupItems([f('old', { modified: '2020-01-01' }), f('new', { modified: now })], 'modified', now);
  assert.deepEqual(groups.map((g) => g.key), ['today', 'older']);
  assert.deepEqual(groupItems([f('a.png'), d('x')], 'type').map((g) => g.label), ['Folders', 'PNG File']);
  assert.equal(groupItems([f('a')], null)[0].items.length, 1);
});

test('extension, kind and type label', () => {
  assert.equal(extension(f('Report.PDF')), 'pdf');
  assert.equal(extension(f('.gitignore')), '');
  assert.equal(extension(f('noext')), '');
  assert.equal(extension(d('a.b')), '');
  assert.equal(fileKind(f('photo.JPG')), 'image');
  assert.equal(fileKind(f('blob', { contentType: 'video/mp4' })), 'video');
  assert.equal(fileKind(d('x')), 'folder');
  assert.equal(fileKind(f('x.unknown')), 'file');
  assert.equal(typeLabel(f('a.png')), 'PNG File');
  assert.equal(typeLabel(d('a')), 'File folder');
  assert.equal(typeLabel(f('a')), 'File');
  assert.equal(typeLabel(f('a.png', { type: 'Screenshot' })), 'Screenshot');
});

test('formatSize: KB in the column, growing units elsewhere', () => {
  assert.equal(formatSize(0, { column: true }), '0 KB');
  assert.equal(formatSize(1, { column: true }), '1 KB');
  assert.equal(formatSize(5 * 1024 ** 3, { column: true }), '5,242,880 KB');
  assert.equal(formatSize(620 * 1024), '620 KB');
  assert.equal(formatSize(4.25 * 1024 ** 2), '4.3 MB');
  assert.equal(formatSize(1.5 * 1024 ** 3), '1.5 GB');
  assert.equal(formatSize(null), '');
});

test('SelectionModel: click, ctrl, shift, ctrl+shift', () => {
  const s = new SelectionModel(['a', 'b', 'c', 'd', 'e']);
  s.pick('b');
  assert.deepEqual(s.list(), ['b']);
  s.pick('d', { shift: true });
  assert.deepEqual(s.list(), ['b', 'c', 'd']);
  s.pick('a', { shift: true });
  assert.deepEqual(s.list(), ['a', 'b'], 'shift ranges from the anchor, not the last click');
  s.pick('e', { ctrl: true });
  assert.deepEqual(s.list(), ['a', 'b', 'e']);
  s.pick('c', { ctrl: true, shift: true });
  assert.deepEqual(s.list(), ['a', 'b', 'c', 'd', 'e'], 'ctrl+shift adds the range from the new anchor');
  s.pick('b', { ctrl: true });
  assert.deepEqual(s.list(), ['a', 'c', 'd', 'e']);
});

test('SelectionModel: keyboard, context pick, setIds', () => {
  const s = new SelectionModel(['a', 'b', 'c', 'd']);
  s.moveFocus(1);
  assert.deepEqual(s.list(), ['a'], 'first arrow press lands on the first item');
  s.moveFocus(2, { shift: true });
  assert.deepEqual(s.list(), ['a', 'b', 'c']);
  s.moveFocus(1, { ctrl: true });
  assert.equal(s.focus, 'd');
  assert.deepEqual(s.list(), ['a', 'b', 'c'], 'ctrl+arrow moves the focus only');
  s.toggleFocused();
  assert.deepEqual(s.list(), ['a', 'b', 'c', 'd']);
  s.contextPick('b');
  assert.equal(s.size, 4, 'right-click on a selected item keeps the selection');
  s.setIds(['b', 'd', 'x']);
  assert.deepEqual(s.list(), ['b', 'd']);
  s.invert();
  assert.deepEqual(s.list(), ['x']);
  s.moveFocus(100);
  assert.deepEqual(s.list(), ['x']);
});

test('typeAhead: prefix, then cycling one letter', () => {
  const items = [f('apple'), f('banana'), f('blueberry'), f('cherry')];
  assert.equal(typeAhead(items, 'b', 'apple'), 'banana');
  assert.equal(typeAhead(items, 'b', 'banana'), 'blueberry');
  assert.equal(typeAhead(items, 'bb', 'blueberry'), 'banana', 'repeating a letter cycles');
  assert.equal(typeAhead(items, 'bl', 'banana'), 'blueberry');
  assert.equal(typeAhead(items, 'z', 'apple'), null);
});

test('NavHistory: back, forward, new location drops forward', () => {
  const h = new NavHistory();
  h.push({ id: 1 });
  h.push({ id: 2 });
  h.push({ id: 3 });
  assert.equal(h.goBack().id, 2);
  assert.equal(h.goBack().id, 1);
  assert.equal(h.canGoBack(), false);
  assert.equal(h.goForward().id, 2);
  h.push({ id: 9 });
  assert.equal(h.canGoForward(), false);
  assert.deepEqual(h.recent().map((x) => x.id), [2, 1]);
  h.push({ id: 9, view: 'tiles' });
  assert.equal(h.back.length, 2, 'the same location only replaces the current entry');
  h.push({ id: 9, search: 'x' });
  assert.equal(h.back.length, 3, 'a search in the folder is a new entry');
});

test('crumbsToFold: keeps the last crumb', () => {
  assert.equal(crumbsToFold([50, 50, 50], 200), 0);
  assert.equal(crumbsToFold([50, 50, 50], 120), 2);
  assert.equal(crumbsToFold([500], 100), 0);
});

test('uniqueName, nameError, renameSelection', () => {
  assert.equal(uniqueName('New folder', ['new folder', 'New folder (2)']), 'New folder (3)');
  assert.equal(uniqueName('a.txt', ['A.TXT']), 'a (2).txt');
  assert.equal(nameError('ok.txt'), null);
  assert.match(nameError('a:b'), /can't contain/);
  assert.match(nameError('  '), /Type a name/);
  assert.match(nameError('CON.txt'), /reserved/);
  assert.deepEqual(renameSelection(f('report.final.pdf')), [0, 12]);
  assert.deepEqual(renameSelection(f('.env')), [0, 4]);
  assert.deepEqual(renameSelection(d('a.b')), [0, 3]);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import { LANGUAGES, normalizeLanguage, workerFor, workerUrl, toHex, mix, themeFromPalette, jsonDiagnostics } from './languages.js';

test('the picker offers JSON first and plain text last', () => {
  assert.equal(LANGUAGES[0].id, 'json');
  assert.equal(LANGUAGES.at(-1).id, 'plaintext');
});

test('languages are normalized, with common aliases', () => {
  assert.equal(normalizeLanguage('JSON'), 'json');
  assert.equal(normalizeLanguage('js'), 'javascript');
  assert.equal(normalizeLanguage('c#'), 'csharp');
  assert.equal(normalizeLanguage('yml'), 'yaml');
  assert.equal(normalizeLanguage('cobol'), 'plaintext');
  assert.equal(normalizeLanguage(null), 'plaintext');
});

test('each Monaco worker label maps to its script', () => {
  assert.equal(workerFor('json'), 'json');
  assert.equal(workerFor('scss'), 'css');
  assert.equal(workerFor('razor'), 'html');
  assert.equal(workerFor('editorWorkerService'), 'editor');
  assert.equal(workerFor('typescript'), 'editor'); // no TypeScript service: tokens only
  assert.equal(workerUrl('https://api/files/ui/js/monaco', 'json'), 'https://api/files/ui/js/monaco/json.worker.js');
  assert.equal(workerUrl('/m/', 'less'), '/m/css.worker.js');
});

test('palette colours become hex, as Monaco themes need', () => {
  assert.equal(toHex('#abc'), '#aabbcc');
  assert.equal(toHex('#594AE2'), '#594ae2');
  assert.equal(toHex('#594ae2ff'), '#594ae2');
  assert.equal(toHex('rgb(89, 74, 226)'), '#594ae2');
  assert.equal(toHex('89,74,226'), '#594ae2');
  assert.equal(toHex('var(--x)', '#123456'), '#123456');
  assert.equal(mix('#000000', '#ffffff', 0.5), '#808080');
});

test('the theme follows light and dark and uses the palette', () => {
  const palette = { surface: '#ffffff', text: '#111111', muted: '#666666', primary: '#594ae2', lines: '#dddddd', error: '#f44336' };
  const light = themeFromPalette(palette, false);
  assert.equal(light.base, 'vs');
  assert.equal(light.colors['editor.background'], '#ffffff');
  assert.equal(light.colors['editorCursor.foreground'], '#594ae2');
  assert.equal(light.colors['editorError.foreground'], '#f44336');
  const dark = themeFromPalette({ ...palette, surface: '#1e1e2d', text: '#eeeeee' }, true);
  assert.equal(dark.base, 'vs-dark');
  assert.equal(dark.colors['editor.background'], '#1e1e2d');
});

test('each editor validates against its own JSON Schema', () => {
  const opts = jsonDiagnostics({
    'inmemory://mud/a.json': { type: 'object', required: ['headline'] },
    'inmemory://mud/b.json': { $id: 'https://x/post.json', type: 'array' },
  });
  assert.equal(opts.validate, true);
  assert.equal(opts.enableSchemaRequest, false);
  assert.deepEqual(opts.schemas.map((s) => s.fileMatch[0]), ['inmemory://mud/a.json', 'inmemory://mud/b.json']);
  assert.equal(opts.schemas[0].uri, 'inmemory://mud/a.json.schema.json');
  assert.deepEqual(jsonDiagnostics(null).schemas, []);
});

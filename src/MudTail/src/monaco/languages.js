// MudMonaco's pure parts (no DOM, no Monaco): the languages it offers, which worker serves which language, and the
// editor theme made from the MudTail palette. Unit-tested in languages.test.js.

/** The languages in the picker, in this order. Labels are translated with `monaco.lang.<id>` when a pack has one. */
export const LANGUAGES = Object.freeze([
  { id: 'json', label: 'JSON' },
  { id: 'html', label: 'HTML' },
  { id: 'css', label: 'CSS' },
  { id: 'scss', label: 'SCSS' },
  { id: 'javascript', label: 'JavaScript' },
  { id: 'typescript', label: 'TypeScript' },
  { id: 'markdown', label: 'Markdown' },
  { id: 'xml', label: 'XML' },
  { id: 'yaml', label: 'YAML' },
  { id: 'csharp', label: 'C#' },
  { id: 'razor', label: 'Razor' },
  { id: 'sql', label: 'SQL' },
  { id: 'plaintext', label: 'Plain text' },
]);

export function isLanguage(id) {
  return LANGUAGES.some((l) => l.id === id);
}

/** A language the picker knows (falls back to plain text). */
export function normalizeLanguage(id) {
  const v = String(id ?? '').toLowerCase();
  const alias = { js: 'javascript', ts: 'typescript', md: 'markdown', cs: 'csharp', 'c#': 'csharp', htm: 'html', yml: 'yaml', text: 'plaintext', txt: 'plaintext' };
  const resolved = alias[v] ?? v;
  return isLanguage(resolved) ? resolved : 'plaintext';
}

/**
 * The worker script a Monaco worker label needs: Monaco asks MonacoEnvironment for a worker by label ('json', 'css',
 * 'html', …, or 'editorWorkerService' for its own). Files are `<name>.worker.js` under the worker base.
 */
export function workerFor(label) {
  switch (label) {
    case 'json': return 'json';
    case 'css': case 'scss': case 'less': return 'css';
    case 'html': case 'handlebars': case 'razor': return 'html';
    default: return 'editor';
  }
}

/** The URL of a worker script: base ('…/js/monaco/') + '<name>.worker.js'. */
export function workerUrl(base, label) {
  const root = String(base ?? '').replace(/\/?$/, '/');
  return `${root}${workerFor(label)}.worker.js`;
}

/** A #rrggbb colour from '#rgb', '#rrggbb', 'rgb(r, g, b)' or 'r, g, b' (Monaco themes take hex only). */
export function toHex(value, fallback = '#000000') {
  const v = String(value ?? '').trim();
  let m = v.match(/^#([0-9a-f]{3})$/i);
  if (m) return `#${m[1].split('').map((c) => c + c).join('')}`.toLowerCase();
  m = v.match(/^#([0-9a-f]{6})([0-9a-f]{2})?$/i);
  if (m) return `#${m[1]}`.toLowerCase();
  m = v.match(/^(?:rgba?\()?\s*(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})/i);
  if (m) return `#${[m[1], m[2], m[3]].map((n) => Math.min(255, Number(n)).toString(16).padStart(2, '0')).join('')}`;
  return fallback;
}

/** Mixes two hex colours: weight 0 = a, 1 = b. */
export function mix(a, b, weight) {
  const pa = toHex(a).slice(1).match(/../g).map((h) => parseInt(h, 16));
  const pb = toHex(b).slice(1).match(/../g).map((h) => parseInt(h, 16));
  return `#${pa.map((c, i) => Math.round(c + (pb[i] - c) * weight).toString(16).padStart(2, '0')).join('')}`;
}

/**
 * A Monaco theme from the MudTail palette, so the editor sits in the page like any other MudTail surface.
 * palette: { surface, text, muted, primary, lines, error, warning } (CSS colours, as read from --mud-palette-*).
 */
export function themeFromPalette(palette, isDark) {
  const surface = toHex(palette.surface, isDark ? '#1e1e2d' : '#ffffff');
  const text = toHex(palette.text, isDark ? '#e6e6e6' : '#1f2328');
  const muted = toHex(palette.muted, isDark ? '#9a9aa5' : '#6b7280');
  const primary = toHex(palette.primary, '#594ae2');
  const lines = toHex(palette.lines, isDark ? '#3a3a48' : '#e0e0e0');
  return {
    base: isDark ? 'vs-dark' : 'vs',
    inherit: true,
    rules: [],
    colors: {
      'editor.background': surface,
      'editor.foreground': text,
      'editorGutter.background': surface,
      'minimap.background': surface,
      'editorLineNumber.foreground': muted,
      'editorLineNumber.activeForeground': text,
      'editor.lineHighlightBackground': mix(surface, text, isDark ? 0.06 : 0.04),
      'editor.lineHighlightBorder': '#00000000',
      'editor.selectionBackground': mix(surface, primary, isDark ? 0.45 : 0.25),
      'editor.inactiveSelectionBackground': mix(surface, primary, isDark ? 0.25 : 0.12),
      'editorCursor.foreground': primary,
      'editorIndentGuide.background1': lines,
      'editorWidget.background': mix(surface, text, isDark ? 0.04 : 0.02),
      'editorWidget.border': lines,
      'focusBorder': primary,
      ...(palette.error ? { 'editorError.foreground': toHex(palette.error) } : {}),
      ...(palette.warning ? { 'editorWarning.foreground': toHex(palette.warning) } : {}),
    },
  };
}

/**
 * Monaco's JSON diagnostics options with every editor's schema: { [modelUri]: schema }. Each schema applies to its own
 * model only (fileMatch is the model's URI), so two editors with different schemas do not mix them up.
 */
export function jsonDiagnostics(schemas) {
  return {
    validate: true,
    allowComments: false,
    trailingCommas: 'error',
    schemaValidation: 'warning',
    enableSchemaRequest: false,
    schemas: Object.entries(schemas ?? {}).map(([uri, schema]) => ({
      uri: `${uri}.schema.json`, // per model: two editors may load different versions of the same $id
      fileMatch: [uri],
      schema,
    })),
  };
}

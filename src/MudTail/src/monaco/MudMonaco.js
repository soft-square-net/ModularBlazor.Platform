// MudMonaco: the code editor of the Content Designer (JSON view, HTML / AMP / JSON-LD output) and of any module that
// needs one. Monaco is self-hosted: its ES modules are the 'monaco' chunk, imported the first time an editor is
// created, and its workers come from MudTail's own files (setMonacoWorkerBase), never from a CDN.
//
//   const editor = new MudMonaco({ value: '{}', language: 'json', schema, onChange: (text) => … });
//   host.appendChild(editor.getElement());
//   await editor.ready;           // true: Monaco is running; false: the plain textarea stayed (Monaco failed to load)
//
// Until Monaco has loaded, and for good when it can't, the editor is a plain <textarea>: typing works from the first
// moment and nothing is lost when Monaco takes over. Colours follow the MudTail theme, light and dark.
import { t } from '../core/i18n.js';
import { getTheme } from '../core/theme.js';
import { LANGUAGES, normalizeLanguage, workerUrl, themeFromPalette, jsonDiagnostics } from './languages.js';

const THEME = 'mudtail';
let workerBase = null;
let monacoPromise = null;
let modelSeq = 0;
const schemas = {}; // model uri -> JSON Schema, for every live editor

/**
 * Where Monaco's workers are served ('…/js/monaco/': editor.worker.js, json.worker.js, css.worker.js,
 * html.worker.js). Call it before the first editor. Workers on another origin are started through a same-origin
 * blob that imports them. null (the default) leaves workers to the bundler (Vite, webpack: `new URL(…,
 * import.meta.url)`), which is right for apps that bundle MudTail themselves.
 */
export function setMonacoWorkerBase(url) {
  workerBase = url ? String(url).replace(/\/?$/, '/') : null;
}

function installEnvironment() {
  if (!workerBase || globalThis.MonacoEnvironment?.getWorker || globalThis.MonacoEnvironment?.getWorkerUrl) return;
  globalThis.MonacoEnvironment = {
    ...(globalThis.MonacoEnvironment ?? {}),
    getWorker(_moduleId, label) {
      const url = new URL(workerUrl(workerBase, label), document.baseURI);
      if (url.origin === location.origin) return new Worker(url, { type: 'module', name: label });
      const blob = URL.createObjectURL(new Blob([`import ${JSON.stringify(url.href)};`], { type: 'text/javascript' }));
      const worker = new Worker(blob, { type: 'module', name: label });
      setTimeout(() => URL.revokeObjectURL(blob), 60_000);
      return worker;
    },
  };
}

function cssVar(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

function applyTheme(monaco) {
  const palette = {
    surface: cssVar('--mud-palette-surface'),
    text: cssVar('--mud-palette-text-primary'),
    muted: cssVar('--mud-palette-text-secondary'),
    primary: cssVar('--mud-palette-primary'),
    lines: cssVar('--mud-palette-lines-default'),
    error: cssVar('--mud-palette-error'),
    warning: cssVar('--mud-palette-warning'),
  };
  monaco.editor.defineTheme(THEME, themeFromPalette(palette, !!getTheme()?.isDark));
  monaco.editor.setTheme(THEME);
}

function applySchemas(monaco) {
  monaco.json?.jsonDefaults?.setDiagnosticsOptions(jsonDiagnostics(schemas));
}

/** Loads Monaco once (the 'monaco' chunk). Resolves to the monaco namespace; rejects when it can't be loaded. */
export function loadMonaco() {
  if (!monacoPromise) {
    installEnvironment();
    monacoPromise = import('./runtime.js').then((monaco) => {
      applyTheme(monaco);
      applySchemas(monaco);
      window.addEventListener('mud-theme-changed', () => applyTheme(monaco));
      return monaco;
    });
    monacoPromise.catch(() => { monacoPromise = null; }); // the next editor tries again
  }
  return monacoPromise;
}

export class MudMonaco {
  /**
   * @param {object} [options]
   * @param {string} [options.value]
   * @param {string} [options.language]          'json' | 'html' | 'css' | … (see LANGUAGES); default 'plaintext'
   * @param {boolean} [options.readOnly]
   * @param {object|null} [options.schema]        a JSON Schema for this editor's JSON (DynamicFields exports them)
   * @param {boolean|string[]} [options.languages] the language picker: true (all), a list of ids, or false (none)
   * @param {boolean} [options.minimap]
   * @param {'on'|'off'} [options.wordWrap]
   * @param {number} [options.fontSize]
   * @param {string} [options.ariaLabel]
   * @param {boolean} [options.monaco]           false: stay a textarea (tests, very small screens)
   * @param {number} [options.debounce]          ms before onChange after typing stops (default 200)
   * @param {(value: string) => void} [options.onChange]
   * @param {(language: string) => void} [options.onLanguage]  the user picked another language
   */
  constructor(options = {}) {
    this.options = options;
    this._value = String(options.value ?? '');
    this._language = normalizeLanguage(options.language ?? 'plaintext');
    this._readOnly = !!options.readOnly;
    this._uri = `inmemory://mudtail/editor-${++modelSeq}.${this._language === 'json' ? 'json' : 'txt'}`;
    this._timer = 0;
    this.editor = null;
    this.model = null;
    this._build();
    if (options.schema) this.setSchema(options.schema);
    /** Resolves true once Monaco runs, false when the textarea stays. */
    this.ready = options.monaco === false ? Promise.resolve(false) : this._start();
  }

  _build() {
    const el = document.createElement('div');
    el.className = 'mud-monaco';
    this.el = el;

    const pick = this.options.languages ?? true;
    if (pick !== false) {
      const bar = document.createElement('div');
      bar.className = 'mud-monaco__bar';
      const label = document.createElement('label');
      label.className = 'mud-monaco__label';
      label.textContent = t('monaco.language', {}, 'Language');
      const select = document.createElement('select');
      select.className = 'mud-monaco__select';
      const ids = Array.isArray(pick) ? pick.map(normalizeLanguage) : LANGUAGES.map((l) => l.id);
      for (const lang of LANGUAGES.filter((l) => ids.includes(l.id))) {
        const o = document.createElement('option');
        o.value = lang.id;
        o.textContent = t(`monaco.lang.${lang.id}`, {}, lang.label);
        select.appendChild(o);
      }
      select.value = this._language;
      select.addEventListener('change', () => {
        this.setLanguage(select.value);
        this.options.onLanguage?.(this._language);
      });
      label.appendChild(select);
      this._readOnlyBadge = document.createElement('span');
      this._readOnlyBadge.className = 'mud-monaco__badge';
      this._readOnlyBadge.textContent = t('monaco.readOnly', {}, 'Read-only');
      bar.append(label, this._readOnlyBadge);
      el.appendChild(bar);
      this.select = select;
    }

    const body = document.createElement('div');
    body.className = 'mud-monaco__body';
    body.dir = 'ltr'; // code reads left to right, in Arabic pages too
    const ta = document.createElement('textarea');
    ta.className = 'mud-monaco__fallback';
    ta.spellcheck = false;
    ta.value = this._value;
    ta.setAttribute('aria-label', this.options.ariaLabel ?? t('monaco.label', {}, 'Code editor'));
    ta.addEventListener('input', () => {
      this._value = ta.value;
      this._changed();
    });
    ta.addEventListener('keydown', (e) => {
      // Tab inserts two spaces instead of leaving the field (Escape, then Tab, still leaves it).
      if (e.key === 'Tab' && !e.shiftKey && !e.ctrlKey && !e.altKey && !ta.readOnly && !this._escaped) {
        e.preventDefault();
        ta.setRangeText('  ', ta.selectionStart, ta.selectionEnd, 'end');
        this._value = ta.value;
        this._changed();
      }
      this._escaped = e.key === 'Escape';
    });
    body.appendChild(ta);
    el.appendChild(body);
    this.body = body;
    this.textarea = ta;
    this._applyReadOnly();
  }

  getElement() { return this.el; }

  async _start() {
    let monaco;
    try {
      monaco = await loadMonaco();
    } catch (e) {
      console.warn('[MudMonaco] Monaco could not be loaded; the plain editor stays', e);
      this.el.classList.add('mud-monaco--fallback');
      return false;
    }
    if (this._disposed) return false;
    this.monaco = monaco;
    // Text typed in the textarea while Monaco loaded is kept.
    this.model = monaco.editor.createModel(this._value, this._language, monaco.Uri.parse(this._uri));
    const host = document.createElement('div');
    host.className = 'mud-monaco__editor';
    this.body.appendChild(host);
    this.editor = monaco.editor.create(host, {
      model: this.model,
      theme: THEME,
      readOnly: this._readOnly,
      automaticLayout: true, // follows dock splitters and hidden → visible tabs
      minimap: { enabled: this.options.minimap ?? false },
      wordWrap: this.options.wordWrap ?? 'off',
      fontSize: this.options.fontSize ?? 13,
      fontFamily: 'ui-monospace, "Cascadia Code", "Cascadia Mono", Consolas, "SF Mono", monospace',
      scrollBeyondLastLine: false,
      renderWhitespace: 'selection',
      tabSize: 2,
      padding: { top: 8 },
      ariaLabel: this.options.ariaLabel ?? t('monaco.label', {}, 'Code editor'),
    });
    this.model.onDidChangeContent(() => {
      this._value = this.model.getValue();
      if (!this._silent) this._changed();
    });
    const hadFocus = document.activeElement === this.textarea;
    this.textarea.remove();
    this.el.classList.add('mud-monaco--ready');
    if (hadFocus) this.editor.focus();
    return true;
  }

  _changed() {
    clearTimeout(this._timer);
    const flush = () => this.options.onChange?.(this._value);
    const ms = this.options.debounce ?? 200;
    if (ms <= 0) flush();
    else this._timer = setTimeout(flush, ms);
  }

  _applyReadOnly() {
    this.textarea.readOnly = this._readOnly;
    this.el.classList.toggle('mud-monaco--readonly', this._readOnly);
    if (this._readOnlyBadge) this._readOnlyBadge.hidden = !this._readOnly;
  }

  getValue() {
    return this.model ? this.model.getValue() : this._value;
  }

  /** Replaces the whole text (one undo step in Monaco). It does not raise onChange: the caller knows its own text. */
  setValue(text) {
    const value = String(text ?? '');
    if (value === this.getValue()) return;
    this._value = value;
    if (this.editor && this.model) {
      this._silent = true;
      this.editor.executeEdits('mudtail', [{ range: this.model.getFullModelRange(), text: value, forceMoveMarkers: true }]);
      this.editor.pushUndoStop();
      this._silent = false;
    } else {
      this.textarea.value = value;
    }
  }

  getLanguage() { return this._language; }

  setLanguage(id) {
    this._language = normalizeLanguage(id);
    if (this.select) this.select.value = this._language;
    if (this.model && this.monaco) this.monaco.editor.setModelLanguage(this.model, this._language);
    return this._language;
  }

  setReadOnly(readOnly) {
    this._readOnly = !!readOnly;
    this._applyReadOnly();
    this.editor?.updateOptions({ readOnly: this._readOnly });
  }

  /** Validates this editor's JSON against a JSON Schema (null removes it). Errors show as squiggles. */
  setSchema(schema) {
    if (schema && typeof schema === 'string') {
      try { schema = JSON.parse(schema); } catch { schema = null; }
    }
    if (schema && typeof schema === 'object') schemas[this._uri] = schema;
    else delete schemas[this._uri];
    if (this.monaco) applySchemas(this.monaco);
  }

  /** Monaco's markers for this editor (validation errors and warnings): [{ message, severity, line, column }]. */
  getProblems() {
    if (!this.monaco || !this.model) return [];
    return this.monaco.editor.getModelMarkers({ resource: this.model.uri }).map((m) => ({
      message: m.message,
      severity: m.severity >= 8 ? 'error' : m.severity >= 4 ? 'warning' : 'info',
      line: m.startLineNumber,
      column: m.startColumn,
    }));
  }

  /** Formats the document (JSON, HTML, CSS). */
  async format() {
    if (!this.editor) return false;
    await this.editor.getAction('editor.action.formatDocument')?.run();
    return true;
  }

  focus() {
    if (this.editor) this.editor.focus();
    else this.textarea.focus();
  }

  layout() { this.editor?.layout(); }

  /** Disposes the editor and returns its last text. */
  dispose() {
    if (this._disposed) return this._value;
    this._disposed = true;
    const value = this.getValue();
    clearTimeout(this._timer);
    delete schemas[this._uri];
    if (this.monaco) applySchemas(this.monaco);
    this.editor?.dispose();
    this.model?.dispose();
    this.el.remove();
    return value;
  }
}

// Components ▸ Design canvas, Code editor, Style tokens: the three packs behind the Content Designer.
//   - MudCanvas (pack 'designer'): a post in a device frame, edited by gestures; this page plays the host's part
//     (Blazor's in the studio): it keeps the model, applies each gesture and re-renders.
//   - MudMonaco (pack 'monaco'): self-hosted Monaco with a JSON Schema, read-only output and the textarea fallback.
//   - Style tokens (pack 'editorial'): data-mb-* attributes and container queries.
import { MudCanvas, MudMonaco, createMudButton, t, getLocale } from 'mudtail';
import { componentPage, esc } from './page.js';

// ───────────── Design canvas ─────────────

const VIEWS = [
  { id: 'mobile', label: 'Mobile', suffix: '' },
  { id: 'tablet', label: 'Tablet', suffix: '-md' },
  { id: 'desktop', label: 'Desktop', suffix: '-lg' },
  { id: 'amp', label: 'AMP', suffix: '' },
];

// The post in both languages; the canvas' direction switch swaps them, so drops and the span handle can be tried in RTL.
const TEXT = {
  en: {
    kicker: 'Sport · Football',
    headline: 'Harbour City win the derby in the last minute',
    standfirst: 'A header from the captain settles a match that turned twice in the second half.',
    p1: 'The home side started fast and led after twelve minutes, when a corner fell to the far post.',
    p2: 'Northbridge answered either side of half-time, and looked set to hold on until the 93rd minute.',
    quote: '“We never stopped believing,” the captain said. “This one is for the city.”',
    caption: 'Fans at the harbour end after the final whistle.',
    side: 'Table: Harbour City move second, two points behind the leaders.',
    p: 'New paragraph',
    h: 'New heading',
  },
  ar: {
    kicker: 'رياضة · كرة القدم',
    headline: 'هاربور سيتي يفوز بالديربي في الدقيقة الأخيرة',
    standfirst: 'رأسية من القائد تحسم مباراة انقلبت مرتين في الشوط الثاني.',
    p1: 'بدأ أصحاب الأرض بقوة وتقدموا بعد اثنتي عشرة دقيقة، حين سقطت ركلة ركنية عند القائم البعيد.',
    p2: 'ردّ نورثبريدج قبل الاستراحة وبعدها، وبدا أنه سيصمد حتى الدقيقة الثالثة والتسعين.',
    quote: '«لم نتوقف عن الإيمان»، قال القائد. «هذا الفوز للمدينة».',
    caption: 'المشجعون في مدرج الميناء بعد صافرة النهاية.',
    side: 'الترتيب: هاربور سيتي يصعد إلى المركز الثاني، بفارق نقطتين عن المتصدر.',
    p: 'فقرة جديدة',
    h: 'عنوان جديد',
  },
};

const PALETTE = [
  { key: 'paragraph', label: 'Paragraph' },
  { key: 'heading', label: 'Heading' },
  { key: 'image', label: 'Image' },
  { key: 'quote', label: 'Quote' },
  { key: 'columns', label: 'Columns' },
];

let seq = 0;
const nextId = (type) => `${type}-${++seq}`;

function picture(hue, label) {
  return `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><defs><linearGradient id="g" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue} 65% 55%)"/><stop offset="1" stop-color="hsl(${(hue + 60) % 360} 65% 32%)"/></linearGradient></defs><rect width="640" height="360" fill="url(#g)"/><path d="M0 360 220 170l90 90 70-50 260 150z" fill="#000" opacity=".25"/><text x="24" y="336" font-family="Segoe UI, sans-serif" font-size="28" fill="#fff">${label}</text></svg>`)}`;
}

/** A block of the model: tag, text key or literal, tokens (style), per-view placement (views), children. */
function block(type, extra = {}) {
  return { id: extra.id ?? nextId(type), type, style: {}, views: {}, children: [], ...extra };
}

function newBlock(key, lang) {
  const tx = TEXT[lang];
  switch (key) {
    case 'heading': return block('heading', { tag: 'h2', text: tx.h, style: { level: 'h3' } });
    case 'image': return block('image', { tag: 'figure', style: { aspect: '16-9' }, image: picture(200, '16:9') });
    case 'quote': return block('quote', { tag: 'blockquote', text: tx.quote, style: { tone: 'muted', spacing: 'md' } });
    case 'columns': return block('columns', { tag: 'section', container: true, style: { columns: '1', 'columns-md': '2' } });
    default: return block('paragraph', { tag: 'p', text: tx.p, style: { size: 'md' } });
  }
}

function samplePost() {
  return block('post', {
    id: 'post', tag: 'article', container: true, style: { layout: 'standard', spacing: 'lg' },
    children: [
      block('kicker', { tag: 'p', textKey: 'kicker', style: { size: 'sm', tone: 'default' } }),
      block('headline', { tag: 'h1', textKey: 'headline', style: { level: 'h2', 'level-lg': 'h1' } }),
      block('standfirst', { tag: 'p', textKey: 'standfirst', style: { size: 'lg' } }),
      block('grid', {
        tag: 'section', container: true, style: { columns: '1', 'columns-md': '2', 'columns-lg': '3' },
        children: [
          block('image', { tag: 'figure', style: { aspect: '4-3', 'aspect-lg': '16-9' }, image: picture(205, 'Harbour City 2 – 1'), views: { desktop: { span: '2' } } }),
          block('sidebar', { tag: 'aside', textKey: 'side', style: { tone: 'muted', spacing: 'md' }, views: { mobile: { hidden: 'true' }, tablet: { hidden: 'false' } } }),
          block('paragraph', { tag: 'p', textKey: 'p1' }),
          block('paragraph', { tag: 'p', textKey: 'p2' }),
          block('quote', { tag: 'blockquote', textKey: 'quote', style: { tone: 'accent', spacing: 'md' }, views: { tablet: { span: 'full' }, desktop: { span: '1', order: '-1' } } }),
        ],
      }),
      block('caption', { tag: 'p', textKey: 'caption', style: { size: 'sm', align: 'center' } }),
    ],
  });
}

const clone = (x) => JSON.parse(JSON.stringify(x));

function find(node, id, parent = null) {
  if (node.id === id) return { node, parent };
  for (const c of node.children) {
    const hit = find(c, id, node);
    if (hit) return hit;
  }
  return null;
}

function contains(node, id) {
  return node.id === id || node.children.some((c) => contains(c, id));
}

/** Renders the model the way a block renderer does: real elements, data-block-* for the canvas, data-mb-* tokens. */
function render(node, lang, root = true) {
  const el = document.createElement(node.tag);
  el.dataset.blockId = node.id;
  el.dataset.blockType = t(`demo.canvas.block.${node.type}`, {}, node.type);
  el.dataset.container = node.container ? 'true' : 'false';
  el.dataset.empty = node.container && !node.children.length ? 'true' : 'false';
  const text = node.textKey ? TEXT[lang][node.textKey] : node.text;
  el.dataset.hasText = text !== undefined ? 'true' : 'false';
  if (!root) el.setAttribute('draggable', 'true');
  for (const [k, v] of Object.entries(node.style)) el.setAttribute(`data-mb-${k}`, v);
  for (const view of VIEWS) {
    if (view.id === 'amp') continue;
    for (const [k, v] of Object.entries(node.views[view.id] ?? {})) el.setAttribute(`data-mb-${k}${view.suffix}`, v);
  }
  if (node.image) {
    const img = document.createElement('img');
    img.src = node.image;
    img.alt = '';
    img.draggable = false;
    el.appendChild(img);
  } else if (text !== undefined) {
    el.textContent = text;
  } else {
    for (const c of node.children) el.appendChild(render(c, lang, false));
  }
  return el;
}

function mountCanvas(preview) {
  let lang = getLocale() === 'ar' ? 'ar' : 'en';
  let model = samplePost();
  let selected = null;
  const undo = [];
  const redo = [];

  const wrap = document.createElement('div');
  wrap.className = 'cp-designer';
  wrap.innerHTML = `
    <div class="cp-designer__bar" role="toolbar" aria-label="${esc(t('Canvas'))}">
      <div class="cp-designer__seg" data-views></div>
      <span class="cp-designer__sep"></span>
      <div class="cp-designer__zoom"></div>
      <span class="cp-designer__sep"></span>
      <div class="cp-designer__toggles"></div>
    </div>
    <div class="cp-designer__body">
      <aside class="cp-designer__palette" aria-label="${esc(t('Toolbox'))}">
        <strong>${esc(t('Toolbox'))}</strong>
        ${PALETTE.map((p) => `<div class="cp-designer__tool" draggable="true" data-block-new="${p.key}">${esc(t(p.label))}</div>`).join('')}
        <p class="cp-designer__hint">${esc(t('Drag a block onto the page. Double-click text to edit it. Alt+↑ / Alt+↓ move the selection, Delete removes it, Ctrl+wheel zooms, the middle button pans.'))}</p>
      </aside>
      <div class="cp-designer__stage"></div>
    </div>
    <p class="cp-tree-log" role="status"></p>`;
  preview.appendChild(wrap);

  const log = wrap.querySelector('[role=status]');
  const say = (name, payload) => { log.textContent = `${name} ${JSON.stringify(payload)}`; };

  const canvas = new MudCanvas({
    device: 'desktop',
    zoom: 60,
    dir: lang === 'ar' ? 'rtl' : 'ltr',
    lang,
    onGesture(name, payload) {
      say(name, payload);
      const handler = gestures[name];
      if (handler) handler(payload);
    },
  });
  wrap.querySelector('.cp-designer__stage').appendChild(canvas.getElement());

  const draw = () => {
    canvas.content.replaceChildren(render(model, lang));
    canvas.select(selected);
  };
  const change = (fn) => {
    undo.push(clone(model));
    redo.length = 0;
    fn();
    draw();
  };
  const viewOf = (node) => (node.views[canvas.getDevice() === 'amp' ? 'mobile' : canvas.getDevice()] ??= {});

  const gestures = {
    select: ({ id }) => { selected = id; },
    drop: ({ kind, id, key, parentId, index }) => change(() => {
      const target = find(model, parentId)?.node;
      if (!target) return;
      let node;
      let at = index < 0 ? target.children.length : index;
      if (kind === 'new') {
        node = newBlock(key, lang);
      } else {
        const hit = find(model, id);
        if (!hit?.parent || contains(hit.node, parentId)) return;
        const from = hit.parent.children.indexOf(hit.node);
        hit.parent.children.splice(from, 1);
        if (hit.parent === target && from < at) at -= 1; // the index counted the block before it left
        node = hit.node;
      }
      target.children.splice(at, 0, node);
      selected = node.id;
    }),
    resize: ({ id, span }) => change(() => {
      const node = find(model, id)?.node;
      if (node && span) viewOf(node).span = String(span);
    }),
    edit: ({ id, text }) => change(() => {
      const node = find(model, id)?.node;
      if (!node) return;
      delete node.textKey;
      node.text = text;
    }),
    move: ({ id, by }) => change(() => {
      const hit = find(model, id);
      if (!hit?.parent) return;
      const list = hit.parent.children;
      const from = list.indexOf(hit.node);
      const to = Math.max(0, Math.min(list.length - 1, from + by));
      list.splice(from, 1);
      list.splice(to, 0, hit.node);
    }),
    delete: ({ id }) => change(() => {
      const hit = find(model, id);
      if (!hit?.parent) return;
      hit.parent.children.splice(hit.parent.children.indexOf(hit.node), 1);
      selected = null;
    }),
    command: ({ name, id }) => {
      if (name === 'undo' && undo.length) { redo.push(clone(model)); model = undo.pop(); draw(); }
      else if (name === 'redo' && redo.length) { undo.push(clone(model)); model = redo.pop(); draw(); }
      else if (name === 'duplicate' && id) change(() => {
        const hit = find(model, id);
        if (!hit?.parent) return;
        const copy = clone(hit.node);
        const renumber = (n) => { n.id = nextId(n.type); n.children.forEach(renumber); };
        renumber(copy);
        hit.parent.children.splice(hit.parent.children.indexOf(hit.node) + 1, 0, copy);
        selected = copy.id;
      });
    },
    zoom: ({ zoom }) => { zoomLabel.textContent = `${zoom}%`; },
  };

  // Toolbar: views, zoom, direction, hidden blocks.
  const views = wrap.querySelector('[data-views]');
  const viewButtons = VIEWS.map((v) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = t(v.label);
    b.title = `${t(v.label)} · ${canvas.devices[v.id]} px`;
    b.addEventListener('click', () => {
      canvas.setDevice(v.id);
      viewButtons.forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    });
    b.setAttribute('aria-pressed', String(v.id === 'desktop'));
    views.appendChild(b);
    return b;
  });
  const zoomLabel = document.createElement('span');
  zoomLabel.className = 'cp-designer__pct';
  const zoomBy = (fn) => () => { zoomLabel.textContent = `${fn()}%`; };
  wrap.querySelector('.cp-designer__zoom').append(
    createMudButton({ text: '−', variant: 'text', size: 'small', onClick: zoomBy(() => canvas.zoomOut()) }),
    zoomLabel,
    createMudButton({ text: '+', variant: 'text', size: 'small', onClick: zoomBy(() => canvas.zoomIn()) }),
    createMudButton({ text: '100%', variant: 'text', size: 'small', onClick: zoomBy(() => canvas.resetZoom()) }),
  );
  zoomLabel.textContent = `${canvas.getZoom()}%`;

  const toggle = (label, initial, onChange) => {
    const l = document.createElement('label');
    l.className = 'cp-designer__check';
    const c = document.createElement('input');
    c.type = 'checkbox';
    c.checked = initial;
    c.addEventListener('change', () => onChange(c.checked));
    l.append(c, document.createTextNode(` ${t(label)}`));
    return l;
  };
  wrap.querySelector('.cp-designer__toggles').append(
    toggle('Arabic page (RTL)', lang === 'ar', (on) => {
      lang = on ? 'ar' : 'en';
      canvas.setDirection(on ? 'rtl' : 'ltr', lang);
      draw();
    }),
    toggle('Show hidden blocks', true, (on) => canvas.setShowHidden(on)),
  );

  draw();
  return () => canvas.dispose();
}

export const CANVAS_PAGE = {
  id: 'design-canvas', label: 'Design canvas', icon: 'layers',
  render: () => componentPage({
    title: 'Design canvas', kinds: ['JS', 'Blazor'],
    lead: 'The Content Designer’s surface (<code>MudCanvas</code>, pack <code>designer</code>), ported from VsDock’s post designer. A page in a device frame (Mobile 375, Tablet 768, Desktop 1280, AMP), with a selection box, drop markers, a span handle, in-place text editing, zoom from 25 to 300 % and panning. It hit-tests while the pointer moves and calls the host <strong>once per finished gesture</strong>; the host owns the model and re-renders.',
    examples: [{
      title: 'A news post in four views',
      description: 'The same markup in every view: each block carries its tokens per breakpoint (<code>data-mb-span-lg="2"</code>, <code>data-mb-hidden="true"</code>), and the editorial pack’s container queries measure the frame, not the window. Resizing sets the span for the current view only. Switch the page to Arabic: before/after and the span handle mirror.',
      mount: mountCanvas,
      code: `
        const canvas = new MudCanvas({
          device: 'desktop', zoom: 60, dir: 'rtl', lang: 'ar',
          onGesture(name, payload) {          // select · drop · resize · edit · move · delete · command · zoom
            apply(name, payload);             // the host changes its model…
            canvas.content.replaceChildren(render(model));   // …and renders the blocks again
          },
        });
        host.appendChild(canvas.getElement());

        // A block, as a renderer writes it:
        // <figure data-block-id="b7" data-block-type="image" data-container="false" data-has-text="false"
        //         draggable="true" data-mb-aspect="4-3" data-mb-span-lg="2">…</figure>`,
    }, {
      title: 'From Blazor',
      description: 'The studio renders the blocks with a JS root component (or its own markup, <code>adopt: true</code>) and hears one <code>OnMudEvent</code> per gesture.',
      language: 'C#',
      code: `
        // Program.cs
        builder.RootComponents.RegisterForJavaScript<PostCanvas>("app-post-canvas");

        _canvas = await mud.InvokeAsync<IJSObjectReference>("create", "canvas", host,
            new { device = "tablet", zoom = 100, dir = "rtl", lang = "ar", component = "app-post-canvas" },
            DotNetObjectReference.Create(this));

        [JSInvokable] public Task OnMudEvent(string name, JsonElement payload) => name switch
        {
            "drop" => Design.Move(payload),      // { kind, id, key, parentId, index, position, targetId }
            "resize" => Design.SetSpan(payload), // { id, span, height }
            _ => Task.CompletedTask,
        };

        await _canvas.InvokeVoidAsync("setDevice", "mobile");
        await _canvas.InvokeVoidAsync("select", blockId);`,
    }],
    api: [{
      title: 'Options',
      rows: [
        ['device', "'mobile' | 'tablet' | 'desktop' | 'amp' (375, 768, 1280, 375 px); <code>devices</code> overrides the widths"],
        ['zoom', 'Percent, 25–300. Ctrl+wheel zooms around the pointer; Ctrl + = / − / 0 from the keyboard'],
        ['dir, lang', 'The page’s direction and language in the frame, apart from the app’s'],
        ['showHidden', 'Blocks hidden in this view stay visible, faded (default true)'],
        ['autoDraggable', 'Sets <code>draggable="true"</code> on blocks the host renders without it (default true)'],
        ['element', 'Adopt existing <code>.mud-canvas</code> markup instead of creating it'],
        ['onGesture(name, payload)', 'Called once per finished gesture'],
      ],
    }, {
      title: 'Gestures',
      rows: [
        ['select', '<code>{ id }</code>: a click; <code>null</code> clears (Escape too)'],
        ['drop', '<code>{ kind: "move" | "new", id, key, parentId, index, position: "before" | "after" | "inside", targetId }</code>; <code>index</code> counts the siblings as they are, -1 is the end'],
        ['resize', '<code>{ id, span, height }</code>: the inline-end handle snaps to grid columns'],
        ['edit', '<code>{ id, text }</code>: plain text, committed with Enter (Shift+Enter in paragraphs) or on blur; Escape cancels'],
        ['move', '<code>{ id, by: -1 | 1 }</code>: Alt+↑ / Alt+↓'],
        ['delete', '<code>{ id }</code>: Delete or Backspace'],
        ['command', '<code>{ name: "undo" | "redo" | "duplicate", id }</code>: Ctrl+Z, Ctrl+Y, Ctrl+D'],
        ['zoom', '<code>{ zoom }</code>: once the wheel rests'],
      ],
    }, {
      title: 'Methods',
      rows: [
        ['setDevice(name), getDevice()', 'The device frame'],
        ['setZoom(percent), zoomIn(), zoomOut(), resetZoom(), getZoom()', 'Zoom; keeps the view’s centre'],
        ['setDirection(dir, lang)', 'The page direction and language'],
        ['select(id), getSelected(), reveal(id)', 'The selection (does not raise <code>select</code>)'],
        ['content', 'The element the blocks are rendered into'],
        ['refresh(), dispose()', 'Redraw the selection box; tear down'],
      ],
    }],
  }),
};

// ───────────── Code editor ─────────────

const POST_SCHEMA = {
  $schema: 'https://json-schema.org/draft/2020-12/schema',
  title: 'post.news',
  type: 'object',
  required: ['headline', 'datePublished', 'contents'],
  additionalProperties: false,
  properties: {
    headline: { type: 'string', maxLength: 110, description: 'schema.org headline' },
    datePublished: { type: 'string', format: 'date-time' },
    section: { enum: ['news', 'sport', 'culture'] },
    contents: {
      type: 'array',
      items: {
        type: 'object',
        required: ['field', 'id'],
        properties: {
          field: { enum: ['block.paragraph', 'block.image', 'block.quote', 'block.video'] },
          id: { type: 'string' },
          style: {
            type: 'object',
            properties: {
              width: { enum: ['content', 'wide', 'full', 'half', 'third'] },
              aspect: { enum: ['auto', '1-1', '4-3', '3-2', '16-9', '21-9'] },
              tone: { enum: ['default', 'muted', 'accent', 'inverse'] },
            },
          },
        },
      },
    },
  },
};

const POST_JSON = `{
  "headline": "Harbour City win the derby in the last minute",
  "datePublished": "2026-11-24T21:45:00Z",
  "section": "sport",
  "contents": [
    { "field": "block.image", "id": "c-101", "style": { "aspect": "16-9", "width": "wide" } },
    { "field": "block.paragraph", "id": "c-102" },
    { "field": "block.quote", "id": "c-103", "style": { "tone": "loud" } }
  ]
}`;

const OUTPUT_HTML = `<article data-mb-page data-mb-layout="standard">
  <h1 data-mb-level="h2" data-mb-level-lg="h1">Harbour City win the derby in the last minute</h1>
  <section data-mb-columns="1" data-mb-columns-md="2" data-mb-columns-lg="3">
    <figure data-mb-aspect="16-9" data-mb-span-lg="2"><img src="/media/derby.jpg" alt=""></figure>
    <aside data-mb-hidden="true" data-mb-hidden-md="false" data-mb-tone="muted">Table: second place</aside>
  </section>
</article>`;

function problemsList(editor, list) {
  const show = () => {
    const problems = editor.getProblems();
    list.replaceChildren(...(problems.length ? problems : [{ severity: 'info', message: t('No problems'), line: 0 }]).map((p) => {
      const li = document.createElement('li');
      li.dataset.severity = p.severity;
      li.textContent = p.line ? `${p.line}:${p.column} ${p.message}` : p.message;
      return li;
    }));
  };
  return show;
}

function mountSchemaEditor(preview) {
  const host = document.createElement('div');
  host.className = 'cp-monaco';
  const list = document.createElement('ul');
  list.className = 'cp-monaco__problems';
  list.setAttribute('aria-label', t('Problems'));
  preview.append(host, list);
  let show = () => {};
  const editor = new MudMonaco({ value: POST_JSON, language: 'json', schema: POST_SCHEMA, onChange: () => setTimeout(show, 400) });
  host.appendChild(editor.getElement());
  show = problemsList(editor, list);
  editor.ready.then(() => setTimeout(show, 800));
  return () => editor.dispose();
}

function mountOutput(preview) {
  const host = document.createElement('div');
  host.className = 'cp-monaco';
  preview.appendChild(host);
  const editor = new MudMonaco({ value: OUTPUT_HTML, language: 'html', readOnly: true, languages: ['html', 'json', 'xml', 'plaintext'] });
  host.appendChild(editor.getElement());
  return () => editor.dispose();
}

function mountFallback(preview) {
  const host = document.createElement('div');
  host.className = 'cp-monaco cp-monaco--short';
  preview.appendChild(host);
  const editor = new MudMonaco({ value: '{ "plain": "textarea" }', language: 'json', monaco: false, languages: false });
  host.appendChild(editor.getElement());
  return () => editor.dispose();
}

export const MONACO_PAGE = {
  id: 'code-editor', label: 'Code editor', icon: 'code',
  render: () => componentPage({
    title: 'Code editor', kinds: ['JS', 'Blazor'],
    lead: '<code>MudMonaco</code> (pack <code>monaco</code>) is the Monaco editor, self-hosted: its ES modules are a chunk loaded the first time an editor opens, and its workers come from UI.MudTail’s own files, never a CDN. It starts as a textarea, so typing works at once, and stays one when Monaco can’t load. Colours follow the MudTail theme: switch to dark mode and the editor follows.',
    examples: [{
      title: 'JSON with a JSON Schema',
      description: 'DynamicFields exports each kind as a JSON Schema; here a post that breaks it (<code>"tone": "loud"</code>). Hover the squiggle, or press Ctrl+Space inside <code>"style"</code> for the allowed values. The list under it is <code>getProblems()</code>.',
      mount: mountSchemaEditor,
      code: `
        const editor = new MudMonaco({
          value: json, language: 'json',
          schema: await fetch('/api/dynamicfields/schemas/post.news').then((r) => r.json()),
          onChange: (text) => save(text),       // after typing stops
        });
        host.appendChild(editor.getElement());
        await editor.ready;                       // true: Monaco; false: the textarea stayed
        editor.getProblems();                     // [{ message, severity, line, column }]`,
    }, {
      title: 'Read-only output, with a language picker',
      description: 'The studio’s Output panel: the responsive HTML, AMP HTML or JSON-LD, read-only. <code>languages</code> limits the picker.',
      mount: mountOutput,
      code: `new MudMonaco({ value: html, language: 'html', readOnly: true, languages: ['html', 'json', 'xml', 'plaintext'] });`,
    }, {
      title: 'The textarea fallback',
      description: '<code>monaco: false</code> keeps the plain editor (it is also what shows while Monaco loads, and offline). Tab inserts two spaces; Escape then Tab leaves the field.',
      mount: mountFallback,
      code: `new MudMonaco({ value, language: 'json', monaco: false, languages: false });`,
    }, {
      title: 'From Blazor',
      language: 'C#',
      code: `
        _editor = await mud.InvokeAsync<IJSObjectReference>("create", "monaco", host,
            new { value = json, language = "json", schema = schemaJson, readOnly = !CanEdit },
            DotNetObjectReference.Create(this));   // OnMudEvent("change", text) · ("language", id) · ("ready", bool)

        await _editor.InvokeVoidAsync("setSchema", newSchema);
        var text = await _editor.InvokeAsync<string>("getValue");`,
    }],
    api: [{
      title: 'Options',
      rows: [
        ['value, language', 'The text and its language: json, html, css, scss, javascript, typescript, markdown, xml, yaml, csharp, razor, sql, plaintext'],
        ['schema', 'A JSON Schema (object or JSON text) for this editor only'],
        ['readOnly', 'Read-only, with a badge in the bar'],
        ['languages', 'The picker: <code>true</code> (all), a list of ids, or <code>false</code> (no bar)'],
        ['minimap, wordWrap, fontSize, ariaLabel', 'Editor options'],
        ['monaco', '<code>false</code>: stay a textarea'],
        ['onChange(text), onLanguage(id)', 'Typing stopped (debounce, default 200 ms); the picker changed'],
      ],
    }, {
      title: 'Methods',
      rows: [
        ['ready', 'A promise: true once Monaco runs, false when the textarea stays'],
        ['getValue(), setValue(text)', 'The text (<code>setValue</code> is one undo step and raises no onChange)'],
        ['setLanguage(id), setReadOnly(bool), setSchema(schema)', 'Change them live'],
        ['getProblems(), format()', 'Validation markers; format the document'],
        ['setMonacoWorkerBase(url)', 'Where the workers are: <code>…/js/monaco/</code>. UI.MudTail sets it; a bundler app leaves it unset'],
      ],
    }],
  }),
};

// ───────────── Style tokens ─────────────

const TOKENS_HTML = `
<div data-mb-page class="cp-tokens__page">
  <section data-mb-columns="1" data-mb-columns-md="2" data-mb-columns-lg="3" data-mb-spacing="md">
    <figure data-mb-aspect="16-9" data-mb-span-lg="2" data-mb-tone="accent">span 2 from lg</figure>
    <aside data-mb-tone="muted" data-mb-hidden="true" data-mb-hidden-md="false">hidden below md</aside>
    <p data-mb-tone="inverse" data-mb-order-md="-1">first from md</p>
    <p data-mb-tone="muted" data-mb-align="center" data-mb-span-md="full" data-mb-span-lg="1">full width on md</p>
  </section>
</div>`;

function mountTokens(preview) {
  const bar = document.createElement('div');
  bar.className = 'cp-row';
  const label = document.createElement('label');
  label.className = 'cp-designer__check';
  const range = document.createElement('input');
  range.type = 'range';
  range.min = '320';
  range.max = '1400';
  range.value = '900';
  const out = document.createElement('output');
  label.append(document.createTextNode(`${t('Page width')} `), range, out);
  bar.appendChild(label);
  preview.prepend(bar);
  const page = preview.querySelector('[data-mb-page]');
  const apply = () => {
    page.style.inlineSize = `${range.value}px`;
    out.textContent = ` ${range.value} px`;
  };
  range.addEventListener('input', apply);
  apply();
}

export const TOKENS_PAGE = {
  id: 'style-tokens', label: 'Style tokens', icon: 'palette',
  render: () => componentPage({
    title: 'Style tokens', kinds: ['CSS'],
    lead: 'The <code>editorial</code> pack is the CSS behind DynamicFields’ style properties (<code>width</code>, <code>align</code>, <code>spacing</code>, <code>tone</code>, <code>size</code>, <code>level</code>, <code>aspect</code>, <code>columns</code>, <code>layout</code>) and the designer’s per-view <code>order</code>, <code>hidden</code> and <code>span</code>. A renderer writes attributes, never classes: <code>data-mb-&lt;token&gt;</code> for the base and <code>data-mb-&lt;token&gt;-&lt;sm|md|lg|xl&gt;</code> where a view changes it. Breakpoints are container queries on <code>data-mb-page</code>, so the same CSS works in the canvas frame and on the live site; every rule is a logical property, and colours come from the palette.',
    examples: [{
      title: 'One page, any width',
      description: 'Drag the slider: the page is the container, so the layout changes with its width, not the window’s. Below md the aside is hidden; from md the inverse paragraph goes first; from lg the figure spans two of three columns.',
      html: TOKENS_HTML,
      mount: mountTokens,
    }],
    api: [{
      title: 'Tokens',
      rows: [
        ['data-mb-width', 'content · wide · full · half · third'],
        ['data-mb-align', 'start · center · end (text, and the block when narrower than the line)'],
        ['data-mb-spacing', 'none · sm · md · lg · xl (padding-block and the gap between children)'],
        ['data-mb-tone', 'default · muted · accent · inverse (palette colours)'],
        ['data-mb-size', 'sm · md · lg · xl (body text)'],
        ['data-mb-level', 'h1 … h6 (how a heading looks)'],
        ['data-mb-aspect', 'auto · 1-1 · 4-3 · 3-2 · 16-9 · 21-9 · 3-4 · 9-16 (media fill the box)'],
        ['data-mb-columns', '1 · 2 · 3 · 4 · 5 · 6 · 12 (a grid of equal columns)'],
        ['data-mb-layout', 'standard · wide · full (a region’s measure)'],
        ['data-mb-span', '1 … 12 · full (columns in the parent grid)'],
        ['data-mb-order', '-1 … 24 · first · last (visual order in this view)'],
        ['data-mb-hidden', 'true · false (holds until a wider breakpoint sets it again)'],
        ['-sm · -md · -lg · -xl', '640 · 768 · 1024 · 1280 px of the page container; Mobile is the base, Tablet md, Desktop lg'],
        ['--mb-*', 'The scales (spacing, measures, text sizes, tones), overridable on the page element'],
      ],
    }],
  }),
};

export const DESIGNER_PAGES = [CANVAS_PAGE, MONACO_PAGE, TOKENS_PAGE];

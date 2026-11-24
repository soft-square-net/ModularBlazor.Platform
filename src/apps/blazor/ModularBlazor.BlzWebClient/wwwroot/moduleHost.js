// Browser side of the module lifecycle. .NET (ActiveModuleService) calls activate/deactivate with one message per
// module; everything a module put on the page is recorded here so deactivate can take exactly that out again.
//
// A module script is an ES module. It may export:
//   default function(ctx)  or  init(ctx)   - runs on activation; may return an object (kept, see moduleHost.get)
//   dispose()                              - runs on deactivation (also looked up on the returned object)
// ctx = { moduleId }.
window.moduleHost = (() => {
  const registry = new Map(); // moduleId -> { styles: HTMLElement[], scripts: { mod, api }[] }
  // Modules whose script moved to another module: get(old) answers the new one's object (until the next major).
  const aliases = { 'Layouts.Default': 'UI.MudTail' };
  const OWNER_ATTR = 'data-module';

  const safeDispose = (target, id) => {
    if (typeof target?.dispose !== 'function') return;
    try {
      Promise.resolve(target.dispose()).catch(e => console.error(`[module ${id}] dispose failed`, e));
    } catch (e) {
      console.error(`[module ${id}] dispose failed`, e);
    }
  };

  // A module script fetched with its hash checked: <link rel=modulepreload integrity> puts the checked module in the
  // module map, and import() of the same url uses it. Its own imports (chunks) are not checked: they are hash-named
  // files under the same per-version url. Resolves false when the file does not match (or cannot be loaded).
  const preload = (url, integrity, entry) => new Promise(resolve => {
    const link = document.createElement('link');
    if (!link.relList?.supports?.('modulepreload')) { resolve(true); return; } // older browsers: plain import
    link.rel = 'modulepreload';
    link.crossOrigin = 'anonymous';
    link.integrity = integrity;
    link.addEventListener('load', () => resolve(true), { once: true });
    link.addEventListener('error', () => resolve(false), { once: true });
    link.href = url;
    document.head.appendChild(link);
    entry.styles.push(link); // removed with the module
  });

  // A script handed over as code (offline, from the cached package) whose package has other scripts (chunks): a blob
  // has no neighbouring files, so "./chunks/x.js" would not resolve. Each file it imports, statically or with
  // import(), becomes a blob too, leaves first, and the specifiers are rewritten to those blob urls. The urls stay
  // until the module is deactivated, since a chunk imported on first use needs its blob then.
  const RELATIVE_IMPORT = /(\bfrom\s*|\bimport\s*\(\s*|\bimport\s*)(["'`])(\.{1,2}\/[^"'`\s]+)\2/g;

  function resolvePath(from, spec) {
    const out = [];
    for (const part of (from.slice(0, from.lastIndexOf('/') + 1) + spec).split('/')) {
      if (part === '.' || part === '') continue;
      if (part === '..') out.pop(); else out.push(part);
    }
    return out.join('/');
  }

  function linkBlobs(moduleId, entryPath, entryCode, files, keep) {
    const lookup = new Map(Object.keys(files).map(p => [p.toLowerCase(), p]));
    const urls = new Map();
    const visiting = new Set();

    const rewrite = (path, code) => code.replace(RELATIVE_IMPORT, (match, before, quote, spec) => {
      const url = urlOf(resolvePath(path, spec));
      return url ? before + quote + url + quote : match;
    });

    function urlOf(path) {
      const key = lookup.get(path.toLowerCase());
      if (!key) return null;
      if (urls.has(key)) return urls.get(key);
      if (visiting.has(key)) throw new Error(`${key} is part of an import cycle; it cannot be loaded from memory`);
      visiting.add(key);
      const code = `${rewrite(key, files[key])}\n//# sourceURL=module://${moduleId}/${key}`;
      const url = URL.createObjectURL(new Blob([code], { type: 'text/javascript' }));
      visiting.delete(key);
      urls.set(key, url);
      keep.push(url);
      return url;
    }

    return rewrite(entryPath, entryCode);
  }

  // spec = { styles: [{ id, css?, href?, integrity? }], scripts: [{ id, code?, url?, integrity?, path?, files? }] }
  //   -> string[] of errors. path and files come with code: the script's path in the package and its other scripts.
  async function activate(moduleId, spec) {
    deactivate(moduleId); // activating twice must not leave two copies behind

    const entry = { styles: [], scripts: [], blobs: [] };
    registry.set(moduleId, entry);

    for (const s of spec?.styles ?? []) {
      // CSS packaged in the module lives in memory as a Blob; a <link> points at it, like a real stylesheet file.
      let el = document.createElement('link');
      el.rel = 'stylesheet';
      if (s.css != null) {
        const css = `${s.css}\n/*# sourceURL=module://${moduleId}/${s.id}.css */`;
        const blobUrl = URL.createObjectURL(new Blob([css], { type: 'text/css' }));
        // Once the browser has parsed it the blob is no longer needed.
        const release = () => URL.revokeObjectURL(blobUrl);
        el.addEventListener('load', release, { once: true });
        el.addEventListener('error', release, { once: true });
        el.href = blobUrl;
      } else if (s.href) {
        if (s.integrity) {
          // A file served by the WebApi: the browser checks its hash (cross-origin needs CORS for that).
          el.crossOrigin = 'anonymous';
          el.integrity = s.integrity;
        }
        el.href = s.href;
      } else continue;

      el.id = s.id;
      el.setAttribute(OWNER_ATTR, moduleId);
      document.head.appendChild(el);
      entry.styles.push(el);
    }

    const errors = [];
    for (const s of spec?.scripts ?? []) {
      let url = s.url;
      let blobUrl = null;
      if (s.code != null) {
        let source = s.code;
        if (s.files && s.path) {
          try {
            source = linkBlobs(moduleId, s.path, s.code, s.files, entry.blobs);
          } catch (e) {
            errors.push(`${s.id}: ${e?.message ?? e}`);
            continue;
          }
        }
        // sourceURL makes the script show up as a named file in the browser dev tools.
        const code = `${source}\n//# sourceURL=module://${moduleId}/${s.id}.js`;
        blobUrl = URL.createObjectURL(new Blob([code], { type: 'text/javascript' }));
        url = blobUrl;
      }
      if (!url) continue;

      try {
        if (s.integrity && !(await preload(url, s.integrity, entry))) {
          errors.push(`${s.id}: ${url} could not be loaded or does not match its hash`);
          continue;
        }
        const mod = await import(/* @vite-ignore */ url);
        const ctx = { moduleId };
        const init = typeof mod.default === 'function' ? mod.default : mod.init;
        const api = typeof init === 'function' ? await init(ctx) : null;
        entry.scripts.push({ mod, api });
      } catch (e) {
        errors.push(`${s.id}: ${e?.message ?? e}`);
      } finally {
        if (blobUrl) URL.revokeObjectURL(blobUrl);
      }
    }

    return errors;
  }

  function deactivate(moduleId) {
    const entry = registry.get(moduleId);
    if (!entry) return;

    for (const s of entry.scripts.reverse()) {
      safeDispose(s.api, moduleId);
      if (s.api !== s.mod) safeDispose(s.mod, moduleId);
    }
    for (const el of entry.styles) el.remove();
    for (const url of entry.blobs) URL.revokeObjectURL(url);

    registry.delete(moduleId);
  }

  return {
    activate,
    deactivate,
    isActive: moduleId => registry.has(moduleId),
    // Hands bytes to the browser as a download (the management page's "Download").
    saveFile: (fileName, bytes) => {
      const url = URL.createObjectURL(new Blob([bytes]));
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 10000);
    },
    // The object the module's script returned, for other scripts that need to talk to it.
    get: (moduleId, index = 0) =>
      registry.get(moduleId)?.scripts[index]?.api
      ?? (aliases[moduleId] ? registry.get(aliases[moduleId])?.scripts[index]?.api : null)
      ?? null,
  };
})();

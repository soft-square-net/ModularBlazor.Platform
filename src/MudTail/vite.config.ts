import tailwindcss from '@tailwindcss/vite';
import path from 'path';
import { defineConfig, type Plugin } from 'vite';

const root = import.meta.dirname;
const lib = path.resolve(root, 'src');

// The outputs of one config, picked with --mode:
//   lib    -> dist/mudtail.js         ES module for bundler consumers (gsap and lucide stay external)
//   blazor -> dist/mudtail.blazor.js  ES module for Blazor's JS.InvokeAsync("import", ...), with the large components
//             (dock, tree, image editor, explorer) in dist/chunks/, loaded on first use
//   monaco-workers -> dist/monaco/*.worker.js  Monaco's web workers (editor, json, css, html), which the 'monaco' area
//             starts from setMonacoBase(url); the host serves them next to its chunks
//   animator -> dist/mudtail.animator.js  the Animator Studio plugin (GSAP plugins inside, GSAP core from MudTail)
//   animator-player -> dist/mudtail.animator.player.js  the same plugin without the Studio, for sites that only play scenes
//   (none) -> the demo/showcase app in demo/ (dev server, or `build:demo` into dist-demo/)

// The Blazor build serves Monaco's stylesheet as the 'monaco' pack and its workers from dist/monaco/, so the stylesheets
// Monaco's modules import and the worker URLs they build for bundlers (`new URL('x.worker.js', import.meta.url)`) are
// left out: those would be inlined into the chunk as base64 in a library build, and a worker URL on the WebApi's
// origin could not be started from the app's origin anyway (MudMonaco starts them through a same-origin blob).
function monacoForBlazor(): Plugin {
  const isMonaco = (id: string) => /[\\/]node_modules[\\/]monaco-editor[\\/]/.test(id);
  return {
    name: 'mudtail-monaco-for-blazor',
    enforce: 'pre',
    resolveId(source, importer) {
      if (importer && isMonaco(importer) && source.endsWith('.css')) return '\0mudtail-empty-css';
      return null;
    },
    load(id) {
      return id === '\0mudtail-empty-css' ? 'export default undefined;' : null;
    },
    transform(code, id) {
      if (!isMonaco(id) || !code.includes('import.meta.url')) return null;
      // Each replacement stays on its line, so the source's own mapping still holds (map: null).
      const out = code
        .replace(/new Worker\(new URL\([^)]*\.worker\.js['"], import\.meta\.url\),\s*\{[^}]*\}\)/g, 'null')
        .replace(/new URL\(['"][^'"]+\.js['"], import\.meta\.url\)/g, 'undefined');
      return { code: out, map: null };
    },
  };
}

export default defineConfig(({ mode }) => {
  if (mode === 'monaco-workers') {
    const vs = path.resolve(root, 'node_modules/monaco-editor/esm/vs');
    return {
      publicDir: false,
      build: {
        outDir: 'dist/monaco',
        emptyOutDir: true,
        sourcemap: false,
        target: 'es2022',
        lib: {
          entry: {
            editor: path.resolve(vs, 'editor/editor.worker.js'),
            json: path.resolve(vs, 'languages/features/json/json.worker.js'),
            css: path.resolve(vs, 'languages/features/css/css.worker.js'),
            html: path.resolve(vs, 'languages/features/html/html.worker.js'),
          },
          formats: ['es'],
          fileName: (_format, name) => `${name}.worker.js`,
        },
        rolldownOptions: { output: { chunkFileNames: 'shared-[hash].js' } },
      },
    };
  }

  if (mode === 'animator' || mode === 'animator-player') {
    const playerOnly = mode === 'animator-player';
    // A plugin module: loadPlugin('./mudtail.animator.js') at runtime, or import 'mudtail/animator' in a bundler.
    // It never imports GSAP's core (it uses ctx.gsap), so only the GSAP plugins it registers are bundled.
    return {
      publicDir: false,
      build: {
        outDir: 'dist',
        emptyOutDir: false,
        sourcemap: true,
        target: 'es2022',
        lib: {
          entry: path.resolve(lib, playerOnly ? 'animator/player-index.js' : 'animator/index.js'),
          formats: ['es'],
          fileName: () => (playerOnly ? 'mudtail.animator.player.js' : 'mudtail.animator.js'),
        },
        rolldownOptions: { external: [/^gsap$/] },
      },
    };
  }

  if (mode === 'lib' || mode === 'blazor') {
    const isBlazor = mode === 'blazor';
    return {
      publicDir: false,
      plugins: isBlazor ? [monacoForBlazor()] : [],
      build: {
        outDir: 'dist',
        emptyOutDir: false, // the two library builds and the CSS build share dist/
        sourcemap: true,
        target: 'es2022',
        lib: {
          entry: isBlazor ? path.resolve(lib, 'blazor.js') : path.resolve(lib, 'index.js'),
          formats: ['es'],
          fileName: () => (isBlazor ? 'mudtail.blazor.js' : 'mudtail.js'),
        },
        rolldownOptions: {
          // Bundler consumers resolve gsap, lucide and monaco-editor themselves (and Monaco's workers and CSS with them).
          external: isBlazor ? [] : [/^gsap($|\/)/, /^lucide($|\/)/, /^monaco-editor($|\/)/],
          // The Blazor build loads the large components (src/areas) on first use: each is a chunk next to it.
          output: { chunkFileNames: 'chunks/[name]-[hash].js' },
        },
      },
    };
  }

  return {
    root: path.resolve(root, 'demo'),
    plugins: [tailwindcss()],
    resolve: {
      // The demo uses the library only through its public entry, like any consumer would.
      alias: [
        { find: /^mudtail\/animator\/player$/, replacement: path.resolve(lib, 'animator/player-index.js') },
        { find: /^mudtail\/animator$/, replacement: path.resolve(lib, 'animator/index.js') },
        { find: /^mudtail$/, replacement: path.resolve(lib, 'index.js') },
      ],
    },
    build: {
      outDir: path.resolve(root, 'dist-demo'),
      emptyOutDir: true,
      rolldownOptions: {
        input: {
          index: path.resolve(root, 'demo/index.html'), // framework dashboard
          site: path.resolve(root, 'demo/site.html'), // public-site showcase
          animator: path.resolve(root, 'demo/animator.html'), // GSAP Animator Studio (full screen)
        },
      },
    },
    server: {
      port: 3000,
      fs: { allow: [root] },
    },
  };
});

import { defineConfig } from 'vite';
import { dirname, resolve, relative } from 'path';
import { cpSync, existsSync, rmSync } from 'fs';
import { createRequire } from 'module';
import { globSync } from 'glob';

// 1. Grab style entries: every src/framework/*.css or *.scss that is not a partial (_name)
const scssInputs = globSync(['src/framework/**/*.{css,scss}'], {
  ignore: ['**/_*', '**/node_modules/**'],
  cwd: import.meta.dirname // Scope safely to current directory
});

const inputs = {
  // 2. Treat your Blazor JS Interop module as the primary entry point
  'module': resolve(import.meta.dirname, 'src/js/module.ts')
};

scssInputs.forEach(file => {
  const absolutePath = resolve(import.meta.dirname, file);
  // Creates clean relative structural naming matching your src/ framework layout
  const relativePath = relative(resolve(import.meta.dirname, 'src'), absolutePath).replace(/\.s?css$/, '');
  inputs[relativePath] = absolutePath;
});

const outDir = resolve(import.meta.dirname, '../wwwroot');

// The wwwroot also holds files this build does not make (emptyOutDir is off), so the chunks are cleared here: their
// names carry hashes, and old ones left behind would be embedded in the module's package. The stylesheet entries
// (src/framework/*.css) make empty JS entry files as well; those are dropped.
// Monaco's web workers (MudTail's dist/monaco: editor, json, css, html) go to js/monaco/, next to module.js; module.ts
// tells MudTail they are there (setMonacoBase). They are plain files, not part of this bundle.
const mudtailDist = resolve(dirname(createRequire(import.meta.url).resolve('mudtail/package.json')), 'dist');
const tidy = {
  name: 'ui-mudtail-tidy',
  buildStart() {
    rmSync(resolve(outDir, 'js/chunks'), { recursive: true, force: true });
    rmSync(resolve(outDir, 'js/monaco'), { recursive: true, force: true });
  },
  closeBundle() {
    const workers = resolve(mudtailDist, 'monaco');
    if (!existsSync(workers)) throw new Error(`Monaco's workers are missing (${workers}): build MudTail first (npm run build:mudtail).`);
    cpSync(workers, resolve(outDir, 'js/monaco'), { recursive: true });
  },
  generateBundle(_options, bundle) {
    for (const [file, output] of Object.entries(bundle)) {
      if (output.type === 'chunk' && output.isEntry && output.name !== 'module' && !output.code.trim()) delete bundle[file];
    }
  },
};

export default defineConfig({
  plugins: [tidy],
  build: {
    // 3. The module's wwwroot (embedded in the DLL by the Module SDK; the WebApi serves each file from its own URL)
    outDir,
    emptyOutDir: false, // Set to false if Blazor holds other static assets here to prevent deletion

    cssMinify: 'lightningcss', 
    sourcemap: true,
    // No preload helper for the chunks: it builds absolute urls from the page, which neither the WebApi's urls nor
    // the offline blobs (moduleHost links those itself) match. The chunks are small and load on first use.
    modulePreload: false,
    
    rollupOptions: {
      input: inputs,
      // moduleHost calls the entry's exports (default/init, dispose). An app build drops entry exports by
      // default, which tree-shakes module.js down to nothing.
      preserveEntrySignatures: 'exports-only',
      output: {
        // Formats your TypeScript interop module for Blazor's IJSRuntime (IJSObjectReference)
        format: 'es',
        
        // Handles Compiled JavaScript outputs (like module.js)
        entryFileNames: (chunkInfo) => {
          if (chunkInfo.name === 'module') {
            return 'js/module.js';
          }
          return 'js/chunks/[name].js';
        },

        // Code loaded on first use (MudTail's large components: dock, tree, image editor, explorer), next to module.js.
        chunkFileNames: 'js/chunks/[name]-[hash].js',
        
        // Directs your custom Material SCSS styles safely to your CSS directory
        assetFileNames: (assetInfo) => {
          if (assetInfo.name && assetInfo.name.endsWith('.css')) {
            // Strips nested framework directory tags to output clean root level files if desired
            const cleanName = assetInfo.name.replace('framework/', '');
            return `css/${cleanName.replace('.css', '.min.css')}`;
          }
          return 'assets/[name]-[hash][extname]';
        }
      }
    }
  }
});

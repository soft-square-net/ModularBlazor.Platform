// MudTail's sass build already compresses the pack; this module only ships it. The core stylesheet it builds on
// (tokens, components) is UI.MudTail's css/mudtail.min.css.
import { copyFileSync, mkdirSync } from 'fs';
import { resolve } from 'path';

const from = resolve(import.meta.dirname, '../../../../src/MudTail/dist/packs/site.css');
const to = resolve(import.meta.dirname, '../wwwroot/css/site.min.css');
mkdirSync(resolve(to, '..'), { recursive: true });
copyFileSync(from, to);
console.log(`site pack -> ${to}`);

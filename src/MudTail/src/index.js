// MudTail Material UI Framework: public JavaScript API.
// Everything a consumer may use is exported from here; anything else in src/ is internal.
// The core (core-api.js) and the large components (areas/*.js); the Blazor build (blazor.js) loads the areas on demand.

export * from './core-api.js';
export * from './areas/dock.js';
export * from './areas/tree.js';
export * from './areas/image-editor.js';
export * from './areas/explorer.js';

import { setPluginHost } from './core/plugins.js';
import * as mudtail from './index.js';
setPluginHost(mudtail); // runtime-loaded plugins get the whole API as ctx.mudtail

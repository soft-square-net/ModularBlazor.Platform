// The Explorer's English messages that need plural forms (the other English texts are the defaults in the code).
// Arabic is in src/locales/ar.js with MudTail's other texts.
import { addMessages } from '../core/i18n.js';

addMessages('en', {
  'explorer.status.items': { one: '{count} item', other: '{count} items' },
  'explorer.status.selected': { one: '{count} item selected', other: '{count} items selected' },
});

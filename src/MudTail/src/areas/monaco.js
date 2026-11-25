// MudTail area 'monaco': MudMonaco, the self-hosted Monaco editor, loaded on first use (blazor.js, load('monaco'))
// with the 'monaco' stylesheet pack. Monaco itself (src/monaco/runtime.js) is one more chunk, imported when the
// first editor is created.

export { MudMonaco, loadMonaco, setMonacoWorkerBase } from '../monaco/MudMonaco.js';
export { LANGUAGES as MONACO_LANGUAGES } from '../monaco/languages.js';

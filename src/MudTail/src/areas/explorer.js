// MudTail area 'explorer': a large component the Blazor build loads on first use (blazor.js, load('explorer')).

// File explorer (Windows 11 style): the shell, its parts, and the pure model (sorting, selection, history, formats)
export { MudExplorer } from '../explorer/MudExplorer.js';
export { MudFileView, DEFAULT_COLUMNS as EXPLORER_COLUMNS } from '../explorer/MudFileView.js';
export { MudCommandBar } from '../explorer/MudCommandBar.js';
export { MudAddressBar } from '../explorer/MudAddressBar.js';
export { MudContextMenu } from '../explorer/MudContextMenu.js';
export {
  sortItems, groupItems, compareNames, SelectionModel, NavHistory, formatSize, fileKind, typeLabel, nameError, uniqueName,
} from '../explorer/model.js';
export { kindIconSvg, itemIconSvg } from '../explorer/icons.js';

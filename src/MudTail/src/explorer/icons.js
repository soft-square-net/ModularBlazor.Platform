// The Explorer's item icons: flat, coloured, in the spirit of Windows 11 (a yellow folder, a white page with a folded
// corner and a coloured mark per kind). Drawn here, on a 48×48 grid; they scale for every view size.

import { fileKind } from './model.js';

const PAGE = `
  <path d="M12 4h17l11 11v27a2 2 0 0 1-2 2H12a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z" fill="var(--mx-page, #fff)" stroke="var(--mx-page-edge, #c4c4c4)" stroke-width="1"/>
  <path d="M29 4.5v8.5a2 2 0 0 0 2 2h8.5" fill="var(--mx-page-fold, #e6e6e6)" stroke="var(--mx-page-edge, #c4c4c4)" stroke-width="1" stroke-linejoin="round"/>`;

const MARKS = {
  text: '<path d="M16 22h16M16 27h16M16 32h11" stroke="#7a7a7a" stroke-width="2" stroke-linecap="round"/>',
  file: '<path d="M16 24h16M16 29h12" stroke="#a0a0a0" stroke-width="2" stroke-linecap="round"/>',
  code: '<path d="m20 23-4 4.5 4 4.5M28 23l4 4.5-4 4.5" fill="none" stroke="#2f7dd1" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/>',
  image: '<rect x="14" y="20" width="20" height="16" rx="2" fill="#3a96dd"/><circle cx="28.5" cy="24.5" r="2" fill="#fff"/><path d="m14 34 6-7 5 5 3-3 6 6v1a2 2 0 0 1-2 2H16a2 2 0 0 1-2-2z" fill="#1f6fb5"/>',
  video: '<rect x="14" y="21" width="20" height="14" rx="2" fill="#8a52c9"/><path d="m22 24.5 6 3.5-6 3.5z" fill="#fff"/>',
  audio: '<path d="M27 20v11.5a3.5 3.5 0 1 1-2-3.2V22l-6 1.5v10a3.5 3.5 0 1 1-2-3.2V21.5z" fill="#d9467a"/>',
  archive: '<path d="M22 4h4v4h-4zm-0 4h4v4h-4zm0 4h4v4h-4z" fill="#b8860b" opacity=".85"/><rect x="20.5" y="17" width="7" height="8" rx="1.5" fill="#c9971c"/><rect x="22.5" y="20" width="3" height="2" fill="#fff"/>',
  pdf: '<rect x="12" y="25" width="24" height="11" rx="2" fill="#d13438"/><text x="24" y="33.3" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="8" font-weight="700" fill="#fff">PDF</text>',
  doc: '<rect x="12" y="22" width="14" height="14" rx="2" fill="#2b579a"/><text x="19" y="32.5" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="9.5" font-weight="700" fill="#fff">W</text><path d="M28 25h6M28 29h6M28 33h6" stroke="#2b579a" stroke-width="1.6" stroke-linecap="round"/>',
  sheet: '<rect x="12" y="22" width="14" height="14" rx="2" fill="#217346"/><text x="19" y="32.5" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="9.5" font-weight="700" fill="#fff">X</text><path d="M28 25h6M28 29h6M28 33h6" stroke="#217346" stroke-width="1.6" stroke-linecap="round"/>',
  slides: '<rect x="12" y="22" width="14" height="14" rx="2" fill="#c43e1c"/><text x="19" y="32.5" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="9.5" font-weight="700" fill="#fff">P</text><path d="M28 25h6M28 29h6M28 33h6" stroke="#c43e1c" stroke-width="1.6" stroke-linecap="round"/>',
};

const FOLDER = `
  <path d="M4 12a4 4 0 0 1 4-4h10.3a4 4 0 0 1 2.9 1.2L24 12h16a4 4 0 0 1 4 4v20a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4z" fill="var(--mx-folder-back, #e8a531)"/>
  <path d="M4 18.5a4 4 0 0 1 4-4h32a4 4 0 0 1 4 4V36a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4z" fill="var(--mx-folder, #ffc83d)"/>`;

const svg = (body, size, label) =>
  `<svg class="mx-icon" xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 48 48" ${label ? `role="img" aria-label="${label}"` : 'aria-hidden="true"'}>${body}</svg>`;

/** The icon of a kind ('folder', 'image', 'pdf', …, 'file'). */
export function kindIconSvg(kind, size = 20) {
  if (kind === 'folder') return svg(FOLDER, size);
  return svg(PAGE + (MARKS[kind] ?? MARKS.file), size);
}

/** The icon for an item; an `icon` on the item (a MudTail icon name) is drawn by the caller instead. */
export function itemIconSvg(item, size = 20) {
  return kindIconSvg(fileKind(item), size);
}

/** The drive / place icons of the navigation pane ('home', 'gallery', …) are MudTail icons; this one is a drive. */
export function driveIconSvg(size = 20) {
  return svg(`
    <rect x="6" y="14" width="36" height="20" rx="4" fill="var(--mx-drive, #d6d6d6)" stroke="#9a9a9a"/>
    <rect x="10" y="27" width="20" height="3" rx="1.5" fill="#2f9e44"/>
    <circle cx="36" cy="28.5" r="1.8" fill="#6b6b6b"/>`, size);
}

// UI.MudTail script -> wwwroot/js/module.js
//
// moduleHost (ModularBlazor.BlzWebClient/wwwroot/moduleHost.js) imports this file when the module is activated: from
// its URL on the WebApi (chunks under js/chunks/ load next to it), or from a blob when the WebApi cannot be reached
// (the cached package). It loads on startup and stays active, whatever layout is shown.
//
// On activation it:
//   - applies the saved MudTail theme (palette, radius, dark mode) to <html>, and follows the system's light/dark
//     setting when the user chose "system";
//   - adds the shell's icon sprite (icons.ts) to <body>: <svg><use href="#mt-i-users"/></svg> draws an icon;
//   - starts the data-mud behaviours on <body>: every data-mud="…" / data-mud-reveal element that Blazor renders,
//     in this module or any other, gets its behaviour and loses it again when Blazor removes the element.
//
// The object returned by init() is what other modules get from
//   window.moduleHost.get('UI.MudTail')            (JS; 'Layouts.Default' still works, an alias until the next major)
//   MudTailRuntime (C#), or JS.InvokeAsync<IJSObjectReference>("moduleHost.get", "UI.MudTail")
// It is MudTail's Blazor interop (create, behavior, observeTheme, toast, …) plus iconNames/addIcons and setMode, so
// modules share one runtime. On deactivation everything created through it is disposed.
import * as MudTail from 'mudtail/blazor';
import { SHELL_ICONS, type IconNode } from './icons';

interface ModuleContext {
  moduleId: string;
}

const SPRITE_ID = 'mt-icons';
const SVG = 'http://www.w3.org/2000/svg';
const MODES = ['light', 'dark', 'system'];

// ───────────── icons ─────────────

function sprite(): SVGSVGElement {
  let svg = document.getElementById(SPRITE_ID) as SVGSVGElement | null;
  if (!svg) {
    svg = document.createElementNS(SVG, 'svg') as SVGSVGElement;
    svg.id = SPRITE_ID;
    svg.setAttribute('aria-hidden', 'true');
    svg.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden';
    document.body.prepend(svg);
  }
  return svg;
}

function symbol(name: string, def: IconNode | string) {
  const id = `mt-i-${name}`;
  document.getElementById(id)?.remove();
  const el = document.createElementNS(SVG, 'symbol');
  el.id = id;
  el.setAttribute('viewBox', '0 0 24 24');
  if (typeof def === 'string') {
    el.innerHTML = def; // the inner markup of a 24×24 stroke icon, like Lucide's
  } else {
    for (const [tag, attrs] of def) {
      const child = document.createElementNS(SVG, tag);
      for (const [k, v] of Object.entries(attrs)) child.setAttribute(k, String(v));
      el.appendChild(child);
    }
  }
  sprite().appendChild(el);
}

const names = new Set<string>();

/** Adds icons to the sprite (or replaces them): { name: lucideIconNode | '<path d="…"/>' }. Returns all names. */
function addIcons(icons: Record<string, IconNode | string>): string[] {
  for (const [name, def] of Object.entries(icons ?? {})) {
    if (!/^[a-z0-9-]+$/.test(name)) continue;
    symbol(name, def);
    names.add(name);
  }
  window.dispatchEvent(new CustomEvent('mud-icons-changed', { detail: [...names] }));
  return [...names];
}

// ───────────── light / dark / system ─────────────

const dark = window.matchMedia?.('(prefers-color-scheme: dark)');

function followSystem() {
  if (currentMode() === 'system') {
    const isDark = !!dark?.matches;
    if (isDark !== !!document.documentElement.getAttribute('data-theme')) MudTail.setTheme({ isDark });
  }
}

function currentMode(): string {
  try {
    const saved = JSON.parse(localStorage.getItem('mud_vanilla_theme_config') || '{}');
    return MODES.includes(saved.mode) ? saved.mode : saved.isDark === true ? 'dark' : saved.isDark === false ? 'light' : 'system';
  } catch {
    return 'system';
  }
}

/** Sets the colour mode the user chose: 'light', 'dark' or 'system' (follows the operating system). */
function setMode(mode: string) {
  if (!MODES.includes(mode)) return MudTail.setTheme({});
  const isDark = mode === 'dark' || (mode === 'system' && !!dark?.matches);
  return MudTail.setTheme({ mode, isDark });
}

// ───────────── media queries, for Blazor ─────────────

interface DotNetRef {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/**
 * Calls dotNetRef.OnMedia(bool[]) with whether each query matches: once now, then on every change. For a layout that
 * changes behaviour, not only looks, with the width (an overlay drawer below 1024px). Returns a handle with dispose().
 */
function watchMedia(queries: string[], dotNetRef: DotNetRef) {
  const lists = (queries ?? []).map((q) => window.matchMedia(q));
  const emit = () => dotNetRef.invokeMethodAsync('OnMedia', lists.map((l) => l.matches)).catch(() => {});
  lists.forEach((l) => l.addEventListener('change', emit));
  emit();
  const handle = { dispose: () => lists.forEach((l) => l.removeEventListener('change', emit)) };
  watchers.add(handle);
  return handle;
}

const watchers = new Set<{ dispose(): void }>();

export default function init(ctx: ModuleContext) {
  MudTail.init({ autoInit: true });
  addIcons(SHELL_ICONS);
  dark?.addEventListener('change', followSystem);
  followSystem();

  return {
    ...MudTail,
    moduleId: ctx.moduleId,
    /** The icon names the sprite has (for a layout to fall back to a letter). */
    iconNames: () => [...names],
    addIcons,
    /** 'light' | 'dark' | 'system' */
    mode: currentMode,
    setMode,
    watchMedia,
    dispose() {
      watchers.forEach((w) => w.dispose());
      watchers.clear();
      // Every handle created through the shared runtime (here or by other modules) belongs to this runtime, so it
      // goes with it. Theme variables stay on <html>; the sprite goes.
      dark?.removeEventListener('change', followSystem);
      document.getElementById(SPRITE_ID)?.remove();
      names.clear();
      MudTail.dispose();
    },
  };
}

// Vanilla JS MudAutocomplete Component
import { getIconSvg } from '../core/icons.js';
import { t } from '../core/i18n.js';

export function createAutocomplete(options = {}) {
  if (typeof options.search === 'function') return createRemoteAutocomplete(options);
  const {
    label = t('autocomplete.label', {}, 'Select Country / Region'),
    placeholder = t('autocomplete.placeholder', {}, 'Type to search...'),
    items = [
      'United States',
      'United Kingdom',
      'Germany',
      'France',
      'Japan',
      'Canada',
      'Australia',
      'Egypt',
      'United Arab Emirates',
      'Netherlands',
      'Sweden',
      'Switzerland',
      'Singapore',
      'Brazil',
      'India',
    ],
    onSelect = null,
    signal = undefined, // AbortSignal: aborting it removes the document-level listener
  } = options;

  const escapeRegExp = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

  const wrapper = document.createElement('div');
  wrapper.className = 'mud-input-control mud-autocomplete-wrapper';

  wrapper.innerHTML = `
    <label class="mud-input-label">${getIconSvg('search', 14)} ${label}</label>
    <div class="mud-input-wrapper">
      <input type="text" class="mud-input-slot" placeholder="${placeholder}" />
      <span class="mud-input-adornment-end" style="cursor:pointer;" data-mud-clear>${getIconSvg('close', 16)}</span>
    </div>
    <div class="mud-autocomplete-popover" data-mud-popover></div>
  `;

  const input = wrapper.querySelector('input');
  const popover = wrapper.querySelector('[data-mud-popover]');
  const clearBtn = wrapper.querySelector('[data-mud-clear]');
  let highlightedIndex = -1;
  let filteredItems = [...items];

  function renderList(query = '') {
    filteredItems = items.filter((item) =>
      item.toLowerCase().includes(query.toLowerCase())
    );

    if (filteredItems.length === 0) {
      popover.innerHTML = '<div style="padding:10px 14px;color:var(--mud-palette-text-secondary);font-size:0.8125rem;">' + t('autocomplete.noMatches', {}, 'No matches found') + '</div>';
      popover.classList.add('open');
      return;
    }

    popover.innerHTML = filteredItems
      .map((item, idx) => {
        let text = item;
        if (query) {
          const regex = new RegExp(`(${escapeRegExp(query)})`, 'gi');
          text = item.replace(regex, '<span class="matched">$1</span>');
        }
        return `<div class="mud-autocomplete-item ${idx === highlightedIndex ? 'highlighted' : ''}" data-index="${idx}">${text}</div>`;
      })
      .join('');

    popover.classList.add('open');

    // Attach click events to items
    popover.querySelectorAll('.mud-autocomplete-item').forEach((el) => {
      el.addEventListener('click', () => {
        const idx = parseInt(el.dataset.index, 10);
        selectItem(filteredItems[idx]);
      });
    });
  }

  function selectItem(item) {
    input.value = item;
    popover.classList.remove('open');
    highlightedIndex = -1;
    if (onSelect) onSelect(item);
  }

  input.addEventListener('input', (e) => {
    highlightedIndex = -1;
    renderList(e.target.value);
  });

  input.addEventListener('focus', () => {
    renderList(input.value);
  });

  input.addEventListener('keydown', (e) => {
    if (!popover.classList.contains('open')) return;

    if (e.key === 'ArrowDown') {
      e.preventDefault();
      highlightedIndex = Math.min(filteredItems.length - 1, highlightedIndex + 1);
      renderList(input.value);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      highlightedIndex = Math.max(0, highlightedIndex - 1);
      renderList(input.value);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (highlightedIndex >= 0 && highlightedIndex < filteredItems.length) {
        selectItem(filteredItems[highlightedIndex]);
      }
    } else if (e.key === 'Escape') {
      popover.classList.remove('open');
    }
  });

  clearBtn.addEventListener('click', () => {
    input.value = '';
    renderList('');
    input.focus();
  });

  // Close on outside click
  document.addEventListener('click', (e) => {
    if (!wrapper.contains(e.target)) {
      popover.classList.remove('open');
    }
  }, { signal });

  return wrapper;
}

const escapeHtml = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

/**
 * Remote mode: items come from `search(q, { skip, take, signal })`, which resolves to
 * `{ items: [{ key, text }], hasMore }` (a lookup page). Items are `{ key, text }`; the text is escaped.
 *
 * Options: label, placeholder, search, value ({ key, text } | null), values ([{ key, text }], multiple),
 * multiple, minChars (0: the first page opens on focus), debounce (ms, 250), pageSize (20), disabled,
 * onSelect(item | null) (single), onChange(items) (multiple), signal.
 * A request still running when a newer one starts is aborted (its signal) and its answer ignored.
 * Scrolling to the end of the list, or "Load more", fetches the next page. Free text is never a value:
 * leaving the field restores the selected item's text.
 */
export function createRemoteAutocomplete(options = {}) {
  const {
    label = '',
    placeholder = t('autocomplete.placeholder', {}, 'Type to search...'),
    search,
    multiple = false,
    minChars = 0,
    debounce = 250,
    pageSize = 20,
    onSelect = null,
    onChange = null,
    signal = undefined,
  } = options;

  let selected = multiple ? null : (options.value ?? null);
  let selection = multiple ? [...(options.values ?? [])] : [];
  let disabled = !!options.disabled;
  let items = [];
  let hasMore = false;
  let highlighted = -1;
  let lastQuery = null;
  let loading = false;
  let error = false;
  let sequence = 0;
  let controller = null;
  let timer = null;

  const wrapper = document.createElement('div');
  wrapper.className = 'mud-input-control mud-autocomplete-wrapper mud-autocomplete--remote';
  wrapper.innerHTML = `
    ${label ? `<label class="mud-input-label">${getIconSvg('search', 14)} ${escapeHtml(label)}</label>` : ''}
    <div class="mud-autocomplete-chips" data-mud-chips hidden></div>
    <div class="mud-input-wrapper">
      <input type="text" class="mud-input-slot" autocomplete="off" role="combobox" aria-autocomplete="list" aria-expanded="false" placeholder="${escapeHtml(placeholder)}" />
      <span class="mud-input-adornment-end" style="cursor:pointer;" data-mud-clear title="${escapeHtml(t('autocomplete.clear', {}, 'Clear'))}">${getIconSvg('close', 16)}</span>
    </div>
    <div class="mud-autocomplete-popover" role="listbox" data-mud-popover></div>
  `;
  const input = wrapper.querySelector('input');
  const popover = wrapper.querySelector('[data-mud-popover]');
  const chips = wrapper.querySelector('[data-mud-chips]');
  const clearBtn = wrapper.querySelector('[data-mud-clear]');

  const open = () => { popover.classList.add('open'); input.setAttribute('aria-expanded', 'true'); };
  const close = () => { popover.classList.remove('open'); input.setAttribute('aria-expanded', 'false'); highlighted = -1; };
  const status = (text) => `<div class="mud-autocomplete-status">${escapeHtml(text)}</div>`;

  function mark(text, query) {
    const safe = escapeHtml(text);
    if (!query) return safe;
    const needle = escapeHtml(query).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return safe.replace(new RegExp(`(${needle})`, 'gi'), '<span class="matched">$1</span>');
  }

  /** What to search: the typed text; the selected item's own text (still shown) searches nothing, so the first page opens. */
  function query() {
    const text = input.value.trim();
    return !multiple && selected && input.value === selected.text ? '' : text;
  }

  function render() {
    const q = query();
    if (error) { popover.innerHTML = status(t('autocomplete.error', {}, 'Could not load the list')); open(); return; }
    if (loading && items.length === 0) { popover.innerHTML = status(t('autocomplete.loading', {}, 'Loading...')); open(); return; }
    if (items.length === 0) { popover.innerHTML = status(t('autocomplete.noMatches', {}, 'No matches found')); open(); return; }
    const chosen = new Set(multiple ? selection.map((s) => s.key) : selected ? [selected.key] : []);
    popover.innerHTML = items.map((item, i) =>
      `<div class="mud-autocomplete-item ${i === highlighted ? 'highlighted' : ''} ${chosen.has(item.key) ? 'selected' : ''}" role="option" aria-selected="${chosen.has(item.key)}" data-index="${i}">${mark(item.text, q)}</div>`
    ).join('') + (hasMore ? `<div class="mud-autocomplete-more" data-mud-more>${escapeHtml(loading ? t('autocomplete.loading', {}, 'Loading...') : t('autocomplete.more', {}, 'Load more'))}</div>` : '');
    open();
  }

  function renderChips() {
    if (!multiple) return;
    chips.hidden = selection.length === 0;
    chips.innerHTML = selection.map((s, i) =>
      `<span class="mud-autocomplete-chip">${escapeHtml(s.text)}<button type="button" data-chip="${i}" aria-label="${escapeHtml(t('autocomplete.remove', {}, 'Remove'))}" ${disabled ? 'disabled' : ''}>${getIconSvg('close', 12)}</button></span>`
    ).join('');
  }

  async function load(reset) {
    const q = query();
    if (q.length < minChars) { items = []; hasMore = false; close(); return; }
    controller?.abort();
    controller = new AbortController();
    const mine = ++sequence;
    const skip = reset ? 0 : items.length;
    if (reset) { items = []; highlighted = -1; }
    loading = true; error = false;
    render();
    try {
      const page = await search(q, { skip, take: pageSize, signal: controller.signal });
      if (mine !== sequence) return; // a newer request owns the list
      items = reset ? [...(page?.items ?? [])] : items.concat(page?.items ?? []);
      hasMore = !!page?.hasMore;
      lastQuery = q;
    } catch (e) {
      if (mine !== sequence || e?.name === 'AbortError') return;
      error = true;
    } finally {
      if (mine === sequence) { loading = false; if (document.activeElement === input || popover.classList.contains('open')) render(); }
    }
  }

  function schedule() {
    clearTimeout(timer);
    timer = setTimeout(() => load(true), debounce);
  }

  function choose(item) {
    if (!item) return;
    if (multiple) {
      if (!selection.some((s) => s.key === item.key)) selection = [...selection, item];
      input.value = '';
      renderChips();
      onChange?.(selection);
      render();
      input.focus();
      return;
    }
    selected = item;
    input.value = item.text;
    close();
    onSelect?.(item);
  }

  input.addEventListener('input', () => { highlighted = -1; schedule(); });
  input.addEventListener('focus', () => {
    if (disabled) return;
    if (!multiple && selected && input.value === selected.text) input.select(); // type over the selected text
    if (lastQuery !== query() || items.length === 0) load(true); else render();
  });
  input.addEventListener('blur', () => {
    setTimeout(() => {
      if (wrapper.contains(document.activeElement)) return;
      close();
      if (!multiple) input.value = selected?.text ?? '';
    }, 150);
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'Backspace' && multiple && input.value === '' && selection.length) {
      selection = selection.slice(0, -1); renderChips(); onChange?.(selection); return;
    }
    if (!popover.classList.contains('open')) { if (e.key === 'ArrowDown') { e.preventDefault(); load(true); } return; }
    if (e.key === 'ArrowDown') { e.preventDefault(); highlighted = Math.min(items.length - 1, highlighted + 1); render(); if (highlighted === items.length - 1 && hasMore && !loading) load(false); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); highlighted = Math.max(0, highlighted - 1); render(); }
    else if (e.key === 'Enter') { e.preventDefault(); choose(items[highlighted]); }
    else if (e.key === 'Escape') { close(); if (!multiple) input.value = selected?.text ?? ''; }
  });
  popover.addEventListener('mousedown', (e) => e.preventDefault()); // keep the focus in the input
  popover.addEventListener('click', (e) => {
    if (e.target.closest('[data-mud-more]')) { if (!loading) load(false); return; }
    const row = e.target.closest('.mud-autocomplete-item');
    if (row) choose(items[parseInt(row.dataset.index, 10)]);
  });
  popover.addEventListener('scroll', () => {
    if (hasMore && !loading && popover.scrollTop + popover.clientHeight >= popover.scrollHeight - 24) load(false);
  });
  chips.addEventListener('click', (e) => {
    const button = e.target.closest('[data-chip]');
    if (!button || disabled) return;
    selection = selection.filter((_, i) => i !== parseInt(button.dataset.chip, 10));
    renderChips();
    onChange?.(selection);
  });
  clearBtn.addEventListener('click', () => {
    if (disabled) return;
    input.value = '';
    if (!multiple && selected) { selected = null; onSelect?.(null); }
    lastQuery = null;
    input.focus();
  });
  document.addEventListener('click', (e) => { if (!wrapper.contains(e.target)) close(); }, { signal });
  signal?.addEventListener('abort', () => { clearTimeout(timer); controller?.abort(); });

  function setDisabled(value) {
    disabled = !!value;
    input.disabled = disabled;
    wrapper.classList.toggle('mud-disabled', disabled);
    renderChips();
  }

  input.value = selected?.text ?? '';
  renderChips();
  setDisabled(disabled);

  /** Imperative API for hosts (Blazor): the element carries it as `mudAutocomplete`. */
  wrapper.mudAutocomplete = {
    setValue(value) { selected = value ?? null; input.value = selected?.text ?? ''; },
    setValues(values) { selection = [...(values ?? [])]; renderChips(); },
    setDisabled,
    /** Forgets the loaded pages, e.g. when the parent of a dependent list changed. */
    reset() { controller?.abort(); sequence++; items = []; hasMore = false; lastQuery = null; loading = false; close(); },
    focus() { input.focus(); },
  };
  return wrapper;
}

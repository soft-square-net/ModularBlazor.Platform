const MIN_ZOOM = 0.25;
const MAX_ZOOM = 4;
const ZOOM_STEP = 0.2;
const SVG_NS = 'http://www.w3.org/2000/svg';

// Stroke icons (24x24, drawn with currentColor), so they follow the theme and the hover colour.
const ICONS = {
	'zoom-out': 'M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16zM21 21l-4.35-4.35M8 11h6',
	'zoom-in': 'M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16zM21 21l-4.35-4.35M11 8v6M8 11h6',
	fit: 'M8 3H5a2 2 0 0 0-2 2v3M16 3h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3M21 16v3a2 2 0 0 1-2 2h-3',
	reset: 'M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5',
};

function makeIcon(path) {
	const icon = document.createElementNS(SVG_NS, 'svg');
	icon.setAttribute('viewBox', '0 0 24 24');
	icon.setAttribute('aria-hidden', 'true');
	icon.setAttribute('focusable', 'false');
	icon.setAttribute('fill', 'none');
	icon.setAttribute('stroke', 'currentColor');
	icon.setAttribute('stroke-width', '2');
	icon.setAttribute('stroke-linecap', 'round');
	icon.setAttribute('stroke-linejoin', 'round');
	const iconPath = document.createElementNS(SVG_NS, 'path');
	iconPath.setAttribute('d', path);
	icon.append(iconPath);
	return icon;
}

function setupViewer(viewer) {
	if (viewer.dataset.ready === 'true') return;
	const stage = viewer.querySelector(':scope > .mermaid-diagram');
	const svg = stage?.querySelector('svg');
	if (!stage || !svg) return;
	const arabic = document.documentElement.lang.startsWith('ar');
	const labels = arabic
		? { zoomIn: 'تكبير', zoomOut: 'تصغير', fit: 'ملاءمة العرض', reset: 'الحجم الأصلي', zoom: 'مستوى التكبير', tools: 'أدوات المخطط' }
		: { zoomIn: 'Zoom in', zoomOut: 'Zoom out', fit: 'Fit to view', reset: 'Actual size', zoom: 'Zoom level', tools: 'Diagram tools' };

	const buttons = {};
	let toolbar = viewer.querySelector(':scope > .mermaid-toolbar');
	if (!toolbar) {
		toolbar = document.createElement('div');
		// not-content: keep Starlight's markdown spacing (margin-top between siblings) out of the toolbar.
		toolbar.className = 'mermaid-toolbar not-content';
		toolbar.setAttribute('role', 'toolbar');
		toolbar.setAttribute('aria-label', labels.tools);
		const addButton = (action, label) => {
			const button = document.createElement('button');
			button.type = 'button';
			button.className = 'mermaid-control';
			button.dataset.mermaidAction = action;
			button.setAttribute('aria-label', label);
			button.title = label;
			button.append(makeIcon(ICONS[action]));
			toolbar.append(button);
		};
		addButton('zoom-out', labels.zoomOut);
		const indicator = document.createElement('output');
		indicator.className = 'mermaid-zoom-level';
		indicator.setAttribute('aria-live', 'polite');
		indicator.setAttribute('aria-label', labels.zoom);
		indicator.textContent = '100%';
		toolbar.append(indicator);
		addButton('zoom-in', labels.zoomIn);
		const divider = document.createElement('span');
		divider.className = 'mermaid-toolbar-divider';
		divider.setAttribute('aria-hidden', 'true');
		toolbar.append(divider);
		addButton('fit', labels.fit);
		addButton('reset', labels.reset);
		viewer.insertBefore(toolbar, stage);
	}
	for (const button of toolbar.querySelectorAll('[data-mermaid-action]')) buttons[button.dataset.mermaidAction] = button;

	viewer.dataset.ready = 'true';
	let zoom = 1;
	let panX = 0;
	let panY = 0;
	let drag = null;
	const zoomLabel = toolbar.querySelector('.mermaid-zoom-level');

	function render() {
		svg.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
		if (zoomLabel) zoomLabel.value = `${Math.round(zoom * 100)}%`;
		if (buttons['zoom-in']) buttons['zoom-in'].disabled = zoom >= MAX_ZOOM - 1e-6;
		if (buttons['zoom-out']) buttons['zoom-out'].disabled = zoom <= MIN_ZOOM + 1e-6;
		stage.classList.toggle('is-zoomed', zoom !== 1 || panX !== 0 || panY !== 0);
	}

	// The svg's untransformed box inside the stage (SVG elements have no offsetLeft/offsetWidth).
	function untransformedBox() {
		const stageBox = stage.getBoundingClientRect();
		const box = svg.getBoundingClientRect();
		return {
			left: box.left - stageBox.left - panX,
			top: box.top - stageBox.top - panY,
			width: box.width / zoom,
			height: box.height / zoom,
		};
	}

	// Point (x, y) in the stage's box, from the stage's top-left corner.
	function setZoom(nextZoom, x = stage.clientWidth / 2, y = stage.clientHeight / 2) {
		const bounded = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, nextZoom));
		const { left: originX, top: originY } = untransformedBox();
		const ratio = bounded / zoom;
		panX = x - originX - (x - originX - panX) * ratio;
		panY = y - originY - (y - originY - panY) * ratio;
		zoom = bounded;
		render();
	}

	function reset() {
		zoom = 1;
		panX = 0;
		panY = 0;
		render();
	}

	// Scale the whole diagram into the visible stage and centre it.
	function fit() {
		const style = getComputedStyle(stage);
		const availableWidth = stage.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
		const availableHeight = Math.max(window.innerHeight * 0.7, 240);
		const { left, width, height } = untransformedBox();
		if (!width || !height) return reset();
		const next = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, Math.min(availableWidth / width, availableHeight / height)));
		zoom = next;
		const contentLeft = parseFloat(style.paddingLeft);
		panX = contentLeft + (availableWidth - width * next) / 2 - left;
		panY = 0;
		render();
	}

	toolbar.addEventListener('click', (event) => {
		const button = event.target.closest('[data-mermaid-action]');
		if (!button || button.disabled) return;
		switch (button.dataset.mermaidAction) {
			case 'zoom-in': setZoom(zoom + ZOOM_STEP); break;
			case 'zoom-out': setZoom(zoom - ZOOM_STEP); break;
			case 'fit': fit(); break;
			case 'reset': reset(); break;
		}
	});

	stage.addEventListener('wheel', (event) => {
		if (!event.ctrlKey && !event.metaKey) return;
		event.preventDefault();
		const bounds = stage.getBoundingClientRect();
		setZoom(zoom + (event.deltaY < 0 ? ZOOM_STEP : -ZOOM_STEP), event.clientX - bounds.left, event.clientY - bounds.top);
	}, { passive: false });

	stage.addEventListener('pointerdown', (event) => {
		if (event.button !== 0 || event.target.closest('a, button')) return;
		if (event.pointerType !== 'mouse' && zoom === 1 && panX === 0 && panY === 0) return; // let touch scroll the page
		drag = { x: event.clientX, y: event.clientY, panX, panY };
		stage.setPointerCapture(event.pointerId);
		stage.classList.add('is-panning');
	});

	stage.addEventListener('pointermove', (event) => {
		if (!drag) return;
		panX = drag.panX + event.clientX - drag.x;
		panY = drag.panY + event.clientY - drag.y;
		render();
	});

	function endDrag() {
		drag = null;
		stage.classList.remove('is-panning');
	}
	stage.addEventListener('pointerup', endDrag);
	stage.addEventListener('pointercancel', endDrag);

	stage.addEventListener('keydown', (event) => {
		if (event.key === '+' || event.key === '=') { event.preventDefault(); setZoom(zoom + ZOOM_STEP); }
		if (event.key === '-') { event.preventDefault(); setZoom(zoom - ZOOM_STEP); }
		if (event.key === '0') { event.preventDefault(); reset(); }
		if (event.key === 'f' || event.key === 'F') { event.preventDefault(); fit(); }
	});

	render();
}

function setupAll() {
	document.querySelectorAll('.mermaid-viewer').forEach(setupViewer);
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', setupAll);
else setupAll();
// Starlight pages can be swapped by Astro's client router; set up the new page's diagrams too.
document.addEventListener('astro:page-load', setupAll);

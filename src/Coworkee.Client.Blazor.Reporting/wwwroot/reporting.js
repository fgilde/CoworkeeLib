const base = new URL('.', import.meta.url).href;
const loaded = {};

function script(path, global) {
    return loaded[path] ??= new Promise((resolve, reject) => {
        if (window[global]) {
            resolve(window[global]);
            return;
        }
        const tag = document.createElement('script');
        tag.src = base + path;
        tag.onload = () => resolve(window[global]);
        tag.onerror = reject;
        document.head.appendChild(tag);
    });
}

function style(path) {
    if (!document.querySelector(`link[data-cw-style="${path}"]`)) {
        const link = document.createElement('link');
        link.rel = 'stylesheet';
        link.href = base + path;
        link.dataset.cwStyle = path;
        document.head.appendChild(link);
    }
}

async function gridStack() {
    style('lib/gridstack/gridstack.min.css');
    const module = await script('lib/gridstack/gridstack-all.js', 'GridStack');
    return module.GridStack ?? module;
}

function layout(items) {
    return items.map(n => ({ id: n.id, x: n.x, y: n.y, w: n.w, h: n.h }));
}

export async function init(element, dotnet, editable, columns, cellHeight) {
    const GridStack = await gridStack();
    const grid = GridStack.init({
        column: columns,
        cellHeight,
        margin: 8,
        float: false,
        animate: true,
        staticGrid: !editable,
        handle: '.cw-widget-drag',
        resizable: { handles: 'se,e,s,w' },
        columnOpts: { breakpoints: [{ w: 560, c: 1 }] },
    }, element);
    element._grid = grid;
    element._columns = columns;
    element._column = grid.getColumn();
    for (const item of gridItems(element)) {
        item._cwPos = item.dataset.pos;
    }
    grid.on('change', (_, items) => {
        // layouts of the one column view on phones and of the switch between the views are not saved
        if (!columnChanged(element) && grid.getColumn() === columns && items?.length) {
            dotnet.invokeMethodAsync('OnLayoutChanged', layout(items));
        }
    });
    // GridStack switches the columns on resize, mostly without a change event; this observer runs after its own
    element._observer = new ResizeObserver(() => columnChanged(element));
    element._observer.observe(element);
    // a render during a drag or resize is synced after it
    grid.on('dragstop resizestop', () => element._pending && setTimeout(() => place(element, false)));
}

// The phone view (one column) and back: GridStack restores the layout it kept from before, which may predate Blazor's positions
// (e.g. designing started on the phone). The items go to Blazor's positions once GridStack is done.
function columnChanged(element) {
    const column = element._grid?.getColumn();
    if (column === element._column) {
        return false;
    }
    element._column = column;
    if (column === element._columns) {
        setTimeout(() => place(element, true));
    }
    return true;
}

export function setEditable(element, editable) {
    element._grid?.setStatic(!editable);
}

// the placeholder GridStack shows while dragging is an item too, pointing at the dragged node
const gridItems = element => element.querySelectorAll(':scope > .grid-stack-item:not(.grid-stack-placeholder)');
const position = node => `${node.x},${node.y},${node.w},${node.h}`;
const idle = node => !node._moving && !node._resizing;

/** Takes up items Blazor added, lets go of those it removed and moves those Blazor gave another position. */
export function sync(element) {
    place(element, false);
}

// Blazor's position of an item is its data-pos (GridStack writes the gs-* attributes itself). Items whose data-pos changed since the last
// sync (all: every item) and that GridStack shows elsewhere are taken out and placed again with the new ones, all in one batch:
// updated one by one they would push each other around. Not in the phone view, whose positions are GridStack's own.
function place(element, all) {
    const grid = element._grid;
    if (!grid) {
        return;
    }
    // while an item is dragged or resized its node is the placeholder's, and a batch would report the positions on the way
    element._pending = grid.engine.nodes.some(n => !idle(n));
    if (element._pending) {
        return;
    }
    grid.batchUpdate();
    for (const node of [...grid.engine.nodes]) {
        if (!node.el?.isConnected) {
            grid.removeWidget(node.el, false, false);
        }
    }
    for (const item of grid.getColumn() === element._columns ? gridItems(element) : []) {
        const node = item.gridstackNode;
        const pos = item.dataset.pos;
        if (!node || node.el !== item || !idle(node) || !pos) {
            continue;
        }
        if ((all || pos !== item._cwPos) && pos !== position(node)) {
            grid.removeWidget(item, false, false);
            const [x, y, w, h] = pos.split(',');
            Object.entries({ 'gs-x': x, 'gs-y': y, 'gs-w': w, 'gs-h': h }).forEach(([name, value]) => item.setAttribute(name, value));
        }
        item._cwPos = pos;
    }
    for (const item of gridItems(element)) {
        if (!item.gridstackNode) {
            grid.makeWidget(item);
            item._cwPos = item.dataset.pos;
        }
    }
    grid.batchUpdate(false);
}

export function dispose(element) {
    element._observer?.disconnect();
    element._grid?.destroy(false);
    element._grid = null;
}

export async function png(element, fileName, background) {
    const htmlToImage = await script('lib/html-to-image/html-to-image.js', 'htmlToImage');
    const url = await htmlToImage.toPng(element, {
        backgroundColor: background,
        pixelRatio: 2,
        filter: node => !(node.classList?.contains('cw-widget-tools')),
    });
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
}

export function print() {
    window.print();
}

export function fullscreen(element) {
    if (document.fullscreenElement) {
        document.exitFullscreen();
    } else {
        element?.requestFullscreen?.();
    }
}

/** The colours of the current MudBlazor theme, read from its CSS variables. */
export function palette() {
    const css = getComputedStyle(document.documentElement);
    const v = name => css.getPropertyValue(`--mud-palette-${name}`).trim();
    const background = v('surface') || v('background') || '#ffffff';
    const rgb = background.match(/\d+/g)?.slice(0, 3).map(Number) ?? [255, 255, 255];
    const hex = background.startsWith('#') ? [1, 3, 5].map(i => parseInt(background.slice(i, i + 2), 16)) : rgb;
    const luminance = (0.299 * hex[0] + 0.587 * hex[1] + 0.114 * hex[2]) / 255;
    return {
        dark: luminance < 0.5,
        colors: ['primary', 'secondary', 'tertiary', 'info', 'success', 'warning', 'error'].map(v).filter(c => c),
        named: Object.fromEntries(['success', 'warning', 'error', 'info'].map(n => [n, v(n)]).filter(e => e[1])),
        text: v('text-secondary'),
        lines: v('lines-default'),
        surface: background,
        font: css.getPropertyValue('--mud-typography-default-family').trim() || getComputedStyle(document.body).fontFamily,
    };
}

/**
 * Endless scrolling: while the element at the end of a scroll area is in view, asks .NET for the next page (LoadMoreAsync, true while more follow).
 * Called again after each load, so a page too short to fill the area loads the next one at once.
 */
export function watchEnd(end, dotnet) {
    const area = end?.parentElement;
    if (!area) {
        return;
    }
    const near = () => end.getBoundingClientRect().top <= area.getBoundingClientRect().bottom + 48;
    const more = async () => {
        if (end._cwLoading) {
            return;
        }
        end._cwLoading = true;
        try {
            while (end.isConnected && near() && await dotnet.invokeMethodAsync('LoadMoreAsync')) { /* next page */ }
        } catch {
            end._cwObserver?.disconnect();
        } finally {
            end._cwLoading = false;
        }
    };
    if (!end._cwObserver) {
        end._cwObserver = new IntersectionObserver(entries => entries.some(e => e.isIntersecting) && more(), { root: area, rootMargin: '0px 0px 48px 0px' });
        end._cwObserver.observe(end);
    }
    more();
}

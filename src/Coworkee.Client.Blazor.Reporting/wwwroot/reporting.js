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
    // layouts of the one column view on phones are not saved
    grid.on('change', (_, items) => {
        if (grid.getColumn() === columns && items?.length) {
            dotnet.invokeMethodAsync('OnLayoutChanged', layout(items));
        }
    });
    element._grid = grid;
}

export function setEditable(element, editable) {
    element._grid?.setStatic(!editable);
}

/** Takes up items Blazor added and lets go of those it removed. */
export function sync(element) {
    const grid = element._grid;
    if (!grid) {
        return;
    }
    grid.batchUpdate();
    for (const node of [...grid.engine.nodes]) {
        if (!node.el?.isConnected) {
            grid.removeWidget(node.el, false, false);
        }
    }
    for (const item of element.querySelectorAll(':scope > .grid-stack-item')) {
        if (!item.gridstackNode) {
            grid.makeWidget(item);
        }
    }
    grid.batchUpdate(false);
}

export function dispose(element) {
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
        text: v('text-secondary'),
        lines: v('lines-default'),
        surface: background,
        font: css.getPropertyValue('--mud-typography-default-family').trim() || getComputedStyle(document.body).fontFamily,
    };
}

export function download(fileName, contentType, content) {
    const url = URL.createObjectURL(new Blob([content], { type: contentType }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
}

let pointerX = null;

export function trackPointer() {
    if (!window.__coworkeePointer) {
        window.__coworkeePointer = true;
        document.addEventListener('pointerdown', e => pointerX = e.clientX, true);
    }
}

// MudBlazor.Extensions applies the options of a new dialog to the first .mud-dialog it has not extended yet. A dialog opened with the plain
// MudBlazor ShowAsync is never extended, so a side sheet opened from inside it styled the outer dialog and stayed hidden itself.
// When a dialog is added, the ones already open are marked as extended. ponytail: goes when MudEx targets the dialog by its id.
export function guardNestedDialogs() {
    if (window.__coworkeeNestedDialogs) return;
    window.__coworkeeNestedDialogs = true;
    new MutationObserver(mutations => {
        const added = mutations.flatMap(m => Array.from(m.addedNodes)).filter(n => n.nodeType === 1 && (n.matches('.mud-dialog') || n.querySelector('.mud-dialog')));
        if (added.length === 0) return;
        document.querySelectorAll('.mud-dialog:not([data-mud-extended=true])').forEach(d => {
            if (!added.some(n => n.contains(d))) {
                d.__extended = true;
                d.setAttribute('data-mud-extended', 'true');
            }
        });
    }).observe(document.body, { childList: true, subtree: true });
}

export function pointerOnLeft() {
    const rtl = document.dir === 'rtl' || document.documentElement.dir === 'rtl';
    return !rtl && pointerX !== null && pointerX < window.innerWidth / 2;
}

export function resizeImage(dataUrl, max) {
    return new Promise(resolve => {
        const image = new Image();
        image.onload = () => {
            const scale = Math.min(1, max / Math.max(image.width, image.height));
            const canvas = document.createElement('canvas');
            canvas.width = Math.round(image.width * scale);
            canvas.height = Math.round(image.height * scale);
            canvas.getContext('2d').drawImage(image, 0, 0, canvas.width, canvas.height);
            resolve(canvas.toDataURL('image/webp', 0.9));
        };
        image.onerror = () => resolve(null);
        image.src = dataUrl;
    });
}

// null brings back the icon the page came with
export function setFavicon(href) {
    let links = [...document.querySelectorAll('link[rel~="icon"]')];
    if (!links.length) {
        if (!href) {
            return;
        }
        links = [document.head.appendChild(Object.assign(document.createElement('link'), { rel: 'icon' }))];
    }
    for (const link of links) {
        link.dataset.cwHref ??= link.getAttribute('href') ?? '';
        link.dataset.cwType ??= link.getAttribute('type') ?? '';
        link.setAttribute('href', href ?? link.dataset.cwHref);
        link.setAttribute('type', href ? '' : link.dataset.cwType);
    }
}

// the server-rendered loading screen of Coworkee.Bff (absent in apps without it); the cookie lets the next one use the user's mode, theme and language
export function hideLoadingScreen(mode, theme, culture) {
    const values = Object.entries({ mode, theme, culture }).filter(([, value]) => value).map(([key, value]) => `${key}=${encodeURIComponent(value)}`);
    document.cookie = `coworkee-loading=${values.join('&')}; path=/; max-age=31536000; samesite=lax`;
    window.coworkeeLoadingScreen?.hide();
}

// the setup page of a second tab leaves once the system is set up: it looks again on focus and when another tab finishes (storage event)
export function watchSetup(dotnet) {
    const recheck = () => dotnet.invokeMethodAsync('RecheckAsync');
    const heard = e => e.key === 'coworkee-setup-done' && recheck();
    window.addEventListener('focus', recheck);
    window.addEventListener('storage', heard);
    return { dispose: () => { window.removeEventListener('focus', recheck); window.removeEventListener('storage', heard); } };
}

export function setupDone() {
    try {
        localStorage.setItem('coworkee-setup-done', String(Date.now()));
    } catch {
        // storage blocked: the other tabs look again when they get the focus
    }
}

export function onHotkey(dotnet, key) {
    const handler = e => {
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === key) {
            e.preventDefault();
            dotnet.invokeMethodAsync('ToggleFromHotkey');
        }
    };
    document.addEventListener('keydown', handler);
    return { dispose: () => document.removeEventListener('keydown', handler) };
}

export function observeStuck(sentinel, root) {
    const appbar = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--mud-appbar-height')) || 0;
    const observer = new IntersectionObserver(([entry]) => root.toggleAttribute('stuck', entry.boundingClientRect.top < entry.rootBounds.top),
        { threshold: 1, rootMargin: `-${appbar}px 0px 0px 0px` });
    observer.observe(sentinel);
    const matchToolbar = () => root.style.setProperty('--cw-stuck-height', `${root.querySelector('.mud-ex-toolbar-sticky')?.offsetHeight ?? 0}px`);
    const resize = new ResizeObserver(matchToolbar);
    resize.observe(root);
    return { dispose: () => { observer.disconnect(); resize.disconnect(); } };
}

export function submitOnEnter(element, dotnet) {
    if (!element) {
        return;
    }

    element.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            dotnet.invokeMethodAsync('SubmitFromEnter');
        }
    });
}

export function scrollToEnd(element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}

// keeps the given text in an input the user is not typing in: Blazor writing an empty value then is ignored
export function keepText(selector, text) {
    const input = document.querySelector(selector);
    if (!input) {
        return;
    }
    if (input.__cwKept === undefined) {
        const native = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
        Object.defineProperty(input, 'value', {
            configurable: true,
            get() { return native.get.call(this); },
            set(value) {
                if (!value && this.__cwKept && document.activeElement !== this) {
                    return;
                }
                native.set.call(this, value);
            }
        });
    }
    input.__cwKept = text;
    if (document.activeElement !== input && input.value !== text) {
        input.value = text;
    }
}

export function focus(element) {
    element?.focus();
}

let monacoLoading = null;

const loadedScripts = new Set();

// a failed tag is removed and a loaded one is remembered, so a retry never adds a script twice
async function loadScript(src) {
    if (loadedScripts.has(src)) {
        return;
    }

    await new Promise((resolve, reject) => {
        const script = Object.assign(document.createElement('script'), { src, onload: resolve, onerror: () => (script.remove(), reject(new Error(`Cannot load ${src}`))) });
        document.head.appendChild(script);
    });
    loadedScripts.add(src);
}

// BlazorMonaco's own scripts, loaded once and in this order, so host apps need no script tags
export function loadMonaco() {
    monacoLoading ??= (async () => {
        if (!window.monaco) {
            const base = '_content/BlazorMonaco';
            await loadScript(`${base}/jsInterop.js`);
            await loadScript(`${base}/lib/monaco-editor/min/vs/loader.js`);
            await loadScript(`${base}/lib/monaco-editor/min/vs/editor/editor.main.js`);
        }
        // editor.main.js defines window.monaco asynchronously after its script has loaded
        for (let i = 0; !window.monaco && i < 200; i++) {
            await new Promise(r => setTimeout(r, 50));
        }
        if (!window.monaco) {
            monacoLoading = null;
        }
        return !!window.monaco;
    })().catch(() => (monacoLoading = null, false));
    return monacoLoading;
}

export function prefersDark() {
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

export function utcOffsetMinutes(year, month, day, hour, minute) {
    return -new Date(year, month - 1, day, hour, minute).getTimezoneOffset();
}

// the Quill mention module only accepts letters while typing, placeholders also contain dots, digits and underscores
export async function widenMentionChars(root) {
    for (let attempt = 0; attempt < 50; attempt++) {
        const container = root?.querySelector('.ql-container');
        const mention = container && window.Quill?.find(container)?.getModule('mention');
        if (mention?.options) {
            mention.options.allowedChars = /^[\p{L}\p{N}._\s]*$/u;
            return true;
        }

        await new Promise(resolve => setTimeout(resolve, 100));
    }

    return false;
}

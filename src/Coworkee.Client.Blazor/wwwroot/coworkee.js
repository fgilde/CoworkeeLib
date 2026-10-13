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
    const observer = new IntersectionObserver(([entry]) => root.toggleAttribute('stuck', entry.intersectionRatio < 1),
        { threshold: 1, rootMargin: `-${appbar}px 0px 0px 0px` });
    observer.observe(sentinel);
    return { dispose: () => observer.disconnect() };
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

export function focus(element) {
    element?.focus();
}

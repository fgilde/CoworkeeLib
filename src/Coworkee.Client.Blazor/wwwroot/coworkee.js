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

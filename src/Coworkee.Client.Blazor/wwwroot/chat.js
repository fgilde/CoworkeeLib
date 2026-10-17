// The composer of a chat: files from the picker, drag and drop and pasted images upload with progress; Enter sends unless a mention list is open.
const mentionLists = '.tt-mention-list, .ql-mention-list-container';

function mentionListOpen() {
    return [...document.querySelectorAll(mentionLists)].some(list => list.offsetParent !== null);
}

export function attach(host, input, dotnet, options) {
    let url = options.url;
    let maxBytes = options.maxBytes;

    const upload = file => {
        const id = crypto.randomUUID();
        dotnet.invokeMethodAsync('UploadStarted', id, file.name || 'pasted.png', file.size);
        if (file.size > maxBytes) {
            dotnet.invokeMethodAsync('UploadFailed', id, 413);
            return;
        }

        const request = new XMLHttpRequest();
        let reported = 0;
        request.open('POST', `${url}?name=${encodeURIComponent(file.name || 'pasted.png')}`);
        request.setRequestHeader('X-CSRF', '1');
        request.setRequestHeader('Content-Type', file.type || 'application/octet-stream');
        request.upload.onprogress = e => {
            const now = Date.now();
            if (e.lengthComputable && now - reported > 150) {
                reported = now;
                dotnet.invokeMethodAsync('UploadProgress', id, e.loaded, e.total);
            }
        };
        request.onload = () => request.status >= 200 && request.status < 300
            ? dotnet.invokeMethodAsync('UploadDone', id, request.responseText)
            : dotnet.invokeMethodAsync('UploadFailed', id, request.status);
        request.onerror = () => dotnet.invokeMethodAsync('UploadFailed', id, 0);
        request.send(file);
    };

    const files = list => [...(list || [])].filter(f => f.kind === undefined || f.kind === 'file').map(f => f.getAsFile ? f.getAsFile() : f).filter(Boolean);

    const onDrop = e => {
        const dropped = files(e.dataTransfer?.files);
        if (dropped.length) {
            e.preventDefault();
            e.stopPropagation();
            host.classList.remove('cw-chat-drop');
            dropped.forEach(upload);
        }
    };
    const onDragOver = e => {
        if ([...(e.dataTransfer?.types || [])].includes('Files')) {
            e.preventDefault();
            host.classList.add('cw-chat-drop');
        }
    };
    const onDragLeave = () => host.classList.remove('cw-chat-drop');
    const onPaste = e => {
        const pasted = files(e.clipboardData?.items);
        if (pasted.length) {
            e.preventDefault();
            e.stopPropagation();
            pasted.forEach(upload);
        }
    };
    const onKeyDown = e => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing && !mentionListOpen()) {
            e.preventDefault();
            e.stopPropagation();
            dotnet.invokeMethodAsync('SendFromEnter');
        }
    };
    const onPick = () => {
        files(input.files).forEach(upload);
        input.value = '';
    };

    host.addEventListener('drop', onDrop, true);
    host.addEventListener('dragover', onDragOver, true);
    host.addEventListener('dragleave', onDragLeave, true);
    host.addEventListener('paste', onPaste, true);
    host.addEventListener('keydown', onKeyDown, true);
    input.addEventListener('change', onPick);

    return {
        pick: () => input.click(),
        configure: (newUrl, newMax) => { url = newUrl; maxBytes = newMax; },
        dispose: () => {
            host.removeEventListener('drop', onDrop, true);
            host.removeEventListener('dragover', onDragOver, true);
            host.removeEventListener('dragleave', onDragLeave, true);
            host.removeEventListener('paste', onPaste, true);
            host.removeEventListener('keydown', onKeyDown, true);
            input.removeEventListener('change', onPick);
        },
    };
}

export function scrollToEnd(element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}

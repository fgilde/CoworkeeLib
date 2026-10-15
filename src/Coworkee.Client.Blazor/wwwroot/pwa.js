// Installable app and Web Push; only active on pages that link a web manifest (Coworkee.Bff's CoworkeePwa component)
let installPrompt = null;

const supported = () => 'serviceWorker' in navigator && !!document.querySelector('link[rel="manifest"]');

export async function start(dotnet) {
    if (!supported()) {
        return false;
    }
    window.addEventListener('beforeinstallprompt', event => {
        event.preventDefault();
        installPrompt = event;
        dotnet.invokeMethodAsync('OnInstallAvailable');
    });
    const registration = await navigator.serviceWorker.register('/service-worker.js', { scope: '/' });
    // a new version waits until the user agrees to reload; the first install of a worker is no update
    const waiting = () => navigator.serviceWorker.controller && dotnet.invokeMethodAsync('OnUpdateReady');
    if (registration.waiting) {
        waiting();
    }
    registration.addEventListener('updatefound', () => {
        const worker = registration.installing;
        worker?.addEventListener('statechange', () => worker.state === 'installed' && waiting());
    });
    let reloading = false;
    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (!reloading && sessionStorage.getItem('coworkee-update') === '1') {
            reloading = true;
            sessionStorage.removeItem('coworkee-update');
            location.reload();
        }
    });
    return true;
}

export async function applyUpdate() {
    const registration = await navigator.serviceWorker.getRegistration('/');
    if (registration?.waiting) {
        sessionStorage.setItem('coworkee-update', '1');
        registration.waiting.postMessage('skip-waiting');
    } else {
        location.reload();
    }
}

export async function install() {
    if (!installPrompt) {
        return false;
    }
    const prompt = installPrompt;
    installPrompt = null;
    await prompt.prompt();
    return (await prompt.userChoice).outcome === 'accepted';
}

// 'unsupported', 'denied', 'default' or 'granted', and the endpoint of this browser's subscription
export async function pushState() {
    if (!supported() || !('PushManager' in window) || !('Notification' in window)) {
        return { permission: 'unsupported', endpoint: null };
    }
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();
    return { permission: Notification.permission, endpoint: subscription?.endpoint ?? null };
}

export async function subscribe(publicKey) {
    if (await Notification.requestPermission() !== 'granted') {
        return null;
    }
    const registration = await navigator.serviceWorker.ready;
    let subscription = await registration.pushManager.getSubscription();
    const key = Uint8Array.from(atob(publicKey.replace(/-/g, '+').replace(/_/g, '/')), c => c.charCodeAt(0));
    // a subscription made with another server key cannot receive this server's pushes
    if (subscription && !same(subscription.options.applicationServerKey, key)) {
        await subscription.unsubscribe();
        subscription = null;
    }
    subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
    const json = subscription.toJSON();
    return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, device: device() };
}

export async function unsubscribe() {
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();
    await subscription?.unsubscribe();
    return subscription?.endpoint ?? null;
}

function same(buffer, key) {
    const bytes = buffer ? new Uint8Array(buffer) : new Uint8Array();
    return bytes.length === key.length && bytes.every((b, i) => b === key[i]);
}

function device() {
    const agent = navigator.userAgent;
    const browser = /Edg\//.test(agent) ? 'Edge' : /OPR\//.test(agent) ? 'Opera' : /Firefox\//.test(agent) ? 'Firefox' : /Chrome\//.test(agent) ? 'Chrome' : /Safari\//.test(agent) ? 'Safari' : 'Browser';
    const system = /Android/.test(agent) ? 'Android' : /iPhone|iPad/.test(agent) ? 'iOS' : /Windows/.test(agent) ? 'Windows' : /Mac OS X/.test(agent) ? 'macOS' : /Linux/.test(agent) ? 'Linux' : '';
    return system ? `${browser} · ${system}` : browser;
}

/** The MudBlazor.Extensions web components (mudex-webcomponents on npm); host the bundle yourself by passing another address to useCoworkee. */
// unpkg: jsDelivr answers 502 for parts of this package (the .NET runtime files)
export const defaultMudExUrl = 'https://unpkg.com/mudex-webcomponents@9.11.3/mudex.js';

type MudExRuntime = { ready: Promise<unknown>; setDarkMode?: (dark: boolean) => void };

declare global {
  interface Window { MudEx?: MudExRuntime }
}

let loading: Promise<MudExRuntime> | undefined;

/** Loads the MudEx runtime once; the first call downloads the .NET runtime (a few MB), later ones reuse it. */
export function loadMudEx(url = defaultMudExUrl, dark?: boolean): Promise<MudExRuntime> {
  loading ??= new Promise<MudExRuntime>((resolve, reject) => {
    if (window.MudEx) return resolve(window.MudEx);
    const script = document.createElement('script');
    script.src = url;
    script.onload = () => window.MudEx ? resolve(window.MudEx) : reject(new Error(`${url} did not start MudEx`));
    script.onerror = () => { loading = undefined; reject(new Error(`${url} could not be loaded`)); };
    document.head.append(script);
  }).then(async (mudEx) => {
    await mudEx.ready;
    if (dark !== undefined) mudEx.setDarkMode?.(dark);
    return mudEx;
  });
  return loading;
}

/**
 * A MudEx web component in its own frame: the bundle brings MudBlazor's page styles, which would otherwise reach the desktop and every app
 * after the first preview. The frame shares the page's origin (srcdoc), so the component reads the app's files with the session cookie;
 * closing the window drops the runtime with the frame.
 */
export function mudExFrame(tag: string, attributes: Record<string, string>, options: { url?: string; dark?: boolean; title?: string } = {}): HTMLIFrameElement {
  const attribute = (value: string) => value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
  const element = `<${tag} ${Object.entries(attributes).map(([name, value]) => `${name}="${attribute(value)}"`).join(' ')}></${tag}>`;
  const frame = document.createElement('iframe');
  frame.className = 'cw-mudex-frame';
  frame.title = options.title ?? tag;
  frame.srcdoc = `<!doctype html><html><head><meta charset="utf-8"><base href="${attribute(location.href)}">
<style>html,body{margin:0;height:100%;overflow:hidden}${tag}{display:block;width:100%;height:100%}</style>
<script src="${attribute(options.url ?? defaultMudExUrl)}"></script></head><body>${element}
<script>window.MudEx && window.MudEx.ready.then(function () { if (window.MudEx.setDarkMode) window.MudEx.setDarkMode(${options.dark ? 'true' : 'false'}); });</script></body></html>`;
  return frame;
}

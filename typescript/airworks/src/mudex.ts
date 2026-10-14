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

/** Where mudExFrame finds the frame page: next to the self-hosted bundle (the template copies dist/mudex-frame.html there). */
export const defaultMudExFrameUrl = '/mudex/coworkee-frame.html';

/**
 * A MudEx web component in its own frame: the bundle brings MudBlazor's page styles, which would otherwise reach the desktop and every app
 * after the first preview. The frame page lives on the app's origin next to mudex.js, so the component reads the app's files with the session
 * cookie; closing the window drops the runtime with the frame.
 */
export function mudExFrame(tag: string, attributes: Record<string, string>, options: { frameUrl?: string; dark?: boolean; title?: string } = {}): HTMLIFrameElement {
  const frame = document.createElement('iframe');
  frame.className = 'cw-mudex-frame';
  frame.title = options.title ?? tag;
  frame.src = `${options.frameUrl ?? defaultMudExFrameUrl}#${encodeURIComponent(JSON.stringify({ tag, attributes, dark: !!options.dark }))}`;
  return frame;
}
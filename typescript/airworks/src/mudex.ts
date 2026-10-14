/** The MudBlazor.Extensions web components (mudex-webcomponents on npm); host the bundle yourself by passing another address to useCoworkee. */
export const defaultMudExUrl = 'https://cdn.jsdelivr.net/npm/mudex-webcomponents@9.11.3/mudex.js';

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

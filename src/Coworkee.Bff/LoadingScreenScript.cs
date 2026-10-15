using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Bff;

/// <summary>
/// Script of <see cref="LoadingScreenMarkup"/>: follows Blazor's progress, offers <c>window.coworkeeLoadingScreen.hide()</c> and <c>fail(detail)</c>,
/// and turns into the error message as soon as the start fails: Blazor shows its error UI (download or boot error, unhandled exception, circuit
/// failure), a framework script cannot be loaded, or an error or rejected promise of the framework goes unhandled. The last console error is the detail.
/// A layout that hides it once its theme is applied claims the screen with an element <c>[data-coworkee-loading-claim]</c>; without a claim the
/// screen goes as soon as the app rendered its first content into the body. A claiming layout that never calls hide() loses the screen 30 s
/// after the download finished (or after the start when Blazor reports no progress).
/// It is its own cacheable file, never inline, so pages with a Content-Security-Policy without <c>'unsafe-inline'</c> run it.
/// </summary>
internal static class LoadingScreenScript
{
    public const string Source = """
        (()=>{const el=document.getElementById('coworkee-loading');if(!el)return;const root=document.documentElement;let since=Date.now(),timer,failed=false,last='';
        el.querySelectorAll('.cwl-media').forEach(m=>{const drop=()=>m.parentNode&&m.parentNode.remove();m.addEventListener('error',drop);if(m.error||(m.complete&&!m.naturalWidth))drop();});
        el.querySelector('.cwl-reload').addEventListener('click',()=>location.reload());
        const video=el.querySelector('video');if(video&&!matchMedia('(prefers-reduced-motion: reduce)').matches)video.play().catch(()=>{});
        const log=console.error,text=x=>x instanceof Error?(x.stack||x.message):String(x),ours=s=>/_framework|blazor|dotnet|webassembly/i.test(s||'');
        console.error=(...a)=>{last=a.map(text).join(' ').slice(0,4000);return log.apply(console,a);};
        const onError=e=>{const t=e.target,src=t&&t!==window&&(t.src||t.href);if(src){if(ours(src))fail('Could not load '+src);return;}
        if(ours(e.filename)||ours(e.error&&e.error.stack))fail(e.error?text(e.error):e.message);};
        const onRejection=e=>{const r=text(e.reason);if(ours(r))fail(r);};
        const ui=document.getElementById('blazor-error-ui'),watch=new MutationObserver(()=>{if(ui.style.display&&ui.style.display!=='none')fail();});
        const statics=new Set(),skip=/^(SCRIPT|STYLE|LINK|TEMPLATE|NOSCRIPT)$/,claimed=()=>!!document.querySelector('[data-coworkee-loading-claim]');
        const rendered=()=>[...document.body.children].some(n=>!statics.has(n)&&!skip.test(n.tagName));
        const check=()=>{if(!claimed()&&rendered())requestAnimationFrame(()=>requestAnimationFrame(()=>{if(!claimed())hide();}));};
        const app=new MutationObserver(check),observe=()=>{[...document.body.children].forEach(n=>statics.add(n));app.observe(document.body,{childList:true,subtree:true});check();};
        const stop=()=>{clearInterval(timer);console.error=log;removeEventListener('error',onError,true);removeEventListener('unhandledrejection',onRejection);watch.disconnect();app.disconnect();removeEventListener('DOMContentLoaded',observe);};
        const hide=()=>{if(failed||el.classList.contains('cwl-done'))return;stop();el.classList.add('cwl-done');setTimeout(()=>el.remove(),600);};
        const fail=detail=>{if(failed||el.classList.contains('cwl-done'))return;failed=true;stop();el.classList.remove('cwl-indeterminate');el.classList.add('cwl-failed');
        ['role','aria-valuenow','aria-valuemin','aria-valuemax'].forEach(a=>el.removeAttribute(a));el.querySelector('.cwl-error').setAttribute('role','alert');
        el.querySelector('.cwl-detail').textContent=detail||last||'Blazor reported an error during the start.';el.querySelector('.cwl-reload').focus();};
        window.coworkeeLoadingScreen={hide,fail};
        addEventListener('error',onError,true);addEventListener('unhandledrejection',onRejection);if(ui)watch.observe(ui,{attributes:true,attributeFilter:['style']});
        const tick=()=>{const p=parseFloat(getComputedStyle(root).getPropertyValue('--blazor-load-percentage'));
        if(isNaN(p)){el.classList.add('cwl-indeterminate');el.removeAttribute('aria-valuenow');}else{el.classList.remove('cwl-indeterminate');el.setAttribute('aria-valuenow',String(Math.round(p)));if(p<100)since=Date.now();}
        if(Date.now()-since>30000)hide();};
        timer=setInterval(tick,100);tick();if(document.readyState==='loading')addEventListener('DOMContentLoaded',observe);else observe();})();
        """;

    public const string Path = "/bff/loading-screen/script.js";

    private static readonly string Version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source)))[..16].ToLowerInvariant();

    public static string Url => $"{Path}?v={Version}";

    public static IResult Serve(HttpContext context, string? v)
    {
        context.Response.Headers.CacheControl = v == Version ? "public, max-age=31536000, immutable" : "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.Text(Source, "text/javascript");
    }
}

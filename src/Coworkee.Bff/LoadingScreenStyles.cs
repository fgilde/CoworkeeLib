namespace Coworkee.Bff;

/// <summary>Style of <see cref="LoadingScreenMarkup"/>, scoped to <c>#coworkee-loading</c>.</summary>
internal static class LoadingScreenStyles
{
    public const string Css = """
        #coworkee-loading{position:fixed;inset:0;z-index:2147483000;display:flex;align-items:center;justify-content:center;overflow:hidden;
        background:var(--cwl-bg);color:var(--cwl-ink);font:14px/1.4 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;transition:opacity .45s ease,visibility 0s linear .45s}
        #coworkee-loading.cwl-done{opacity:0;visibility:hidden;pointer-events:none}
        #coworkee-loading .cwl-layer{position:absolute;inset:0}
        #coworkee-loading .cwl-blob{position:absolute;width:60vmax;height:60vmax;border-radius:50%;filter:blur(70px);opacity:.32;will-change:transform}
        #coworkee-loading .cwl-b1{background:var(--cwl-primary);top:-24vmax;left:-18vmax;animation:cwl-a 16s ease-in-out infinite alternate}
        #coworkee-loading .cwl-b2{background:var(--cwl-secondary);bottom:-28vmax;right:-20vmax;animation:cwl-b 21s ease-in-out infinite alternate}
        #coworkee-loading .cwl-b3{background:var(--cwl-tertiary);width:36vmax;height:36vmax;top:38%;left:42%;opacity:.18;animation:cwl-c 25s ease-in-out infinite alternate}
        #coworkee-loading .cwl-media{position:absolute;inset:0;width:100%;height:100%;object-fit:cover}
        #coworkee-loading .cwl-veil{position:absolute;inset:0;background:linear-gradient(140deg,color-mix(in srgb,var(--cwl-primary) 50%,transparent),color-mix(in srgb,var(--cwl-bg) 78%,transparent))}
        #coworkee-loading .cwl-center{position:relative;display:flex;flex-direction:column;align-items:center;gap:18px}
        #coworkee-loading .cwl-ring{position:relative;width:136px;height:136px;display:grid;place-items:center;border-radius:50%;
        background:color-mix(in srgb,var(--cwl-bg) 55%,transparent);box-shadow:0 12px 48px color-mix(in srgb,var(--cwl-primary) 28%,transparent)}
        #coworkee-loading .cwl-ring::before{content:"";position:absolute;inset:0;border-radius:50%;
        background:conic-gradient(var(--cwl-primary) var(--blazor-load-percentage,0%),color-mix(in srgb,var(--cwl-ink) 14%,transparent) 0);
        -webkit-mask:radial-gradient(farthest-side,transparent calc(100% - 6px),#000 calc(100% - 5px));mask:radial-gradient(farthest-side,transparent calc(100% - 6px),#000 calc(100% - 5px))}
        #coworkee-loading.cwl-indeterminate .cwl-ring::before{background:conic-gradient(transparent 0 40%,var(--cwl-primary));animation:cwl-spin 1.1s linear infinite}
        #coworkee-loading .cwl-logo{width:76px;height:76px;object-fit:contain;animation:cwl-pulse 2.6s ease-in-out infinite}
        #coworkee-loading .cwl-mark{width:56px;height:56px;border-radius:30%;background:linear-gradient(135deg,var(--cwl-primary),var(--cwl-secondary));animation:cwl-pulse 2.6s ease-in-out infinite}
        #coworkee-loading .cwl-text{min-height:1.4em;font-variant-numeric:tabular-nums;letter-spacing:.06em;opacity:.8}
        #coworkee-loading .cwl-text::after{content:var(--blazor-load-percentage-text,"")}
        #coworkee-loading.cwl-indeterminate .cwl-text::after{content:""}
        @keyframes cwl-a{to{transform:translate(20vmax,14vmax) scale(1.15)}}
        @keyframes cwl-b{to{transform:translate(-16vmax,-18vmax) scale(.88)}}
        @keyframes cwl-c{to{transform:translate(-22vmax,12vmax) scale(1.3)}}
        @keyframes cwl-spin{to{transform:rotate(1turn)}}
        @keyframes cwl-pulse{50%{transform:scale(1.07);opacity:.82}}
        @media (prefers-reduced-motion:reduce){#coworkee-loading *,#coworkee-loading *::before{animation:none!important}#coworkee-loading{transition:none}
        #coworkee-loading.cwl-indeterminate .cwl-ring::before{background:conic-gradient(var(--cwl-primary) 0 30%,color-mix(in srgb,var(--cwl-ink) 14%,transparent) 0)}}
        #coworkee-loading .cwl-error{display:none;position:relative;flex-direction:column;align-items:center;gap:12px;box-sizing:border-box;width:min(520px,calc(100vw - 32px));
        padding:28px 32px;border-radius:18px;text-align:center;background:color-mix(in srgb,var(--cwl-bg) 84%,transparent);backdrop-filter:blur(10px);
        box-shadow:0 16px 56px color-mix(in srgb,var(--cwl-primary) 24%,transparent)}
        #coworkee-loading.cwl-failed .cwl-center{display:none}
        #coworkee-loading.cwl-failed .cwl-error{display:flex}
        #coworkee-loading .cwl-error-title{font-size:20px;font-weight:600}
        #coworkee-loading .cwl-error-text{margin:0;opacity:.85}
        #coworkee-loading .cwl-reload{font:inherit;font-weight:600;padding:9px 26px;border:0;border-radius:999px;background:var(--cwl-primary);color:var(--cwl-on-primary);cursor:pointer}
        #coworkee-loading .cwl-reload:focus-visible{outline:3px solid color-mix(in srgb,var(--cwl-primary) 45%,transparent);outline-offset:3px}
        #coworkee-loading .cwl-details{align-self:stretch;font-size:12px;opacity:.8}
        #coworkee-loading .cwl-details summary{cursor:pointer}
        #coworkee-loading .cwl-detail{text-align:start;white-space:pre-wrap;word-break:break-word;max-height:160px;overflow:auto;margin:8px 0 0;padding:8px;border-radius:8px;
        background:color-mix(in srgb,var(--cwl-ink) 8%,transparent)}
        #coworkee-loading #blazor-error-ui,body:has(#coworkee-loading.cwl-failed) #blazor-error-ui{display:none!important}
        """;
}

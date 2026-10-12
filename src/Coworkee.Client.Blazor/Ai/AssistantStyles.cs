namespace Coworkee.Client.Blazor.Ai;

/// <summary>The look of the assistant button and window, from the theme's palette, so it follows the theme and dark mode.</summary>
internal static class AssistantStyles
{
    public const string Css =
        """
        .cw-ai-fab { position: fixed; right: 24px; bottom: 24px; z-index: 1150; width: 56px; height: 56px; border: 0; border-radius: 50%; cursor: pointer;
          color: var(--mud-palette-primary-text); display: grid; place-items: center;
          background: linear-gradient(135deg, var(--mud-palette-primary), var(--mud-palette-secondary, var(--mud-palette-primary-darken)));
          box-shadow: 0 6px 20px -4px var(--mud-palette-primary); transition: transform .2s ease, box-shadow .2s ease; }
        .cw-ai-fab:hover { transform: translateY(-2px) scale(1.04); box-shadow: 0 10px 26px -4px var(--mud-palette-primary); }
        .cw-ai-fab:focus-visible { outline: 3px solid var(--mud-palette-primary-lighten); outline-offset: 3px; }
        .cw-ai-fab::after { content: ""; position: absolute; inset: 0; border-radius: 50%; box-shadow: 0 0 0 0 var(--mud-palette-primary); animation: cw-ai-pulse 3.2s ease-out infinite; }
        .cw-ai-fab.open::after { animation: none; }
        .cw-ai-fab .mud-icon-root { transition: transform .25s ease; }
        .cw-ai-fab.open .mud-icon-root { transform: rotate(90deg); }
        @keyframes cw-ai-pulse { 0% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--mud-palette-primary) 55%, transparent); } 70%, 100% { box-shadow: 0 0 0 14px transparent; } }
        @keyframes cw-ai-in { from { opacity: 0; transform: translateY(12px) scale(.98); } to { opacity: 1; transform: none; } }
        @keyframes cw-ai-dot { 0%, 80%, 100% { opacity: .25; } 40% { opacity: 1; } }
        @media (prefers-reduced-motion: reduce) { .cw-ai-fab::after, .cw-ai-window { animation: none !important; } }

        .cw-ai-window { position: fixed; right: 24px; bottom: 92px; z-index: 1150; width: 420px; height: min(640px, calc(100vh - 140px));
          display: flex; flex-direction: column; overflow: hidden; border-radius: 16px; background: var(--mud-palette-surface);
          border: 1px solid var(--mud-palette-lines-default); box-shadow: var(--mud-elevation-12); animation: cw-ai-in .18s ease-out; }
        .cw-ai-window.large { width: min(980px, calc(100vw - 48px)); height: calc(100vh - 140px); }
        .cw-ai-window.full { inset: var(--mud-appbar-height, 64px) 0 0 0; width: auto; height: auto; border-radius: 0; border: 0; }
        @media (max-width: 600px) { .cw-ai-window, .cw-ai-window.large, .cw-ai-window.full { inset: 0; width: auto; height: auto; border-radius: 0; z-index: 1350; } }

        .cw-ai-header { display: flex; align-items: center; gap: 4px; padding: 6px 8px 6px 14px; color: var(--mud-palette-primary-text);
          background: linear-gradient(135deg, var(--mud-palette-primary), var(--mud-palette-secondary, var(--mud-palette-primary-darken))); }
        .cw-ai-header .mud-icon-button { color: inherit; }
        .cw-ai-title { flex: 1; min-width: 0; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .cw-ai-body { flex: 1; display: flex; min-height: 0; position: relative; }
        .cw-ai-sessions { width: 250px; flex: none; display: flex; flex-direction: column; border-right: 1px solid var(--mud-palette-lines-default); background: var(--mud-palette-background-gray); }
        .cw-ai-window.small .cw-ai-sessions { position: absolute; inset: 0 auto 0 0; z-index: 2; width: 82%; box-shadow: var(--mud-elevation-8); }
        .cw-ai-session { display: flex; align-items: center; gap: 4px; padding: 6px 6px 6px 12px; cursor: pointer; border-radius: 8px; margin: 1px 6px; }
        .cw-ai-session:hover { background: var(--mud-palette-action-default-hover); }
        .cw-ai-session.active { background: color-mix(in srgb, var(--mud-palette-primary) 14%, transparent); }
        .cw-ai-session .cw-ai-session-actions { visibility: hidden; }
        .cw-ai-session:hover .cw-ai-session-actions, .cw-ai-session.active .cw-ai-session-actions { visibility: visible; }
        .cw-ai-chat { flex: 1; display: flex; flex-direction: column; min-width: 0; }
        .cw-ai-messages { flex: 1; overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 12px; }
        .cw-ai-window.large .cw-ai-messages > *, .cw-ai-window.full .cw-ai-messages > * { max-width: 860px; width: 100%; align-self: center; }
        .cw-ai-msg { display: flex; flex-direction: column; gap: 6px; }
        .cw-ai-msg.user { align-items: flex-end; }
        .cw-ai-bubble { max-width: 88%; padding: 8px 12px; border-radius: 14px; overflow-wrap: anywhere; }
        .cw-ai-msg.user .cw-ai-bubble { white-space: pre-wrap; background: var(--mud-palette-primary); color: var(--mud-palette-primary-text); border-bottom-right-radius: 4px; }
        .cw-ai-msg.assistant .cw-ai-bubble { background: var(--mud-palette-background-gray); border-bottom-left-radius: 4px; }
        .cw-ai-bubble .mud-markdown-body p:last-child { margin-bottom: 0; }
        .cw-ai-bubble pre { max-width: 100%; overflow-x: auto; }
        .cw-ai-typing span { display: inline-block; width: 6px; height: 6px; margin-right: 3px; border-radius: 50%; background: currentColor; animation: cw-ai-dot 1.2s infinite; }
        .cw-ai-typing span:nth-child(2) { animation-delay: .2s; }
        .cw-ai-typing span:nth-child(3) { animation-delay: .4s; }
        .cw-ai-confirm { border: 1px solid var(--mud-palette-warning); border-radius: 12px; padding: 10px 12px; max-width: 88%; }
        .cw-ai-composer { display: flex; align-items: flex-end; gap: 8px; padding: 10px 12px; border-top: 1px solid var(--mud-palette-lines-default); }
        .cw-ai-input { flex: 1; resize: none; max-height: 160px; min-height: 40px; padding: 9px 12px; border-radius: 12px; font: inherit; color: var(--mud-palette-text-primary);
          background: var(--mud-palette-background); border: 1px solid var(--mud-palette-lines-inputs); outline: none; field-sizing: content; }
        .cw-ai-input:focus { border-color: var(--mud-palette-primary); }
        .cw-ai-empty { margin: auto; text-align: center; max-width: 320px; color: var(--mud-palette-text-secondary); }
        """;
}

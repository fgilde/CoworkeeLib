namespace Coworkee.Client.Blazor.Chat;

/// <summary>The look of messages, the composer and the mini chat dock; theme colors only, so every theme and dark mode fit.</summary>
internal static class ChatStyles
{
    public const string Css = """
        .cw-chat-thread { display: flex; flex-direction: column; min-height: 0; height: 100%; }
        .cw-chat-messages { flex: 1; min-height: 0; overflow-y: auto; display: flex; flex-direction: column; gap: 4px; padding: 4px 2px; }
        .cw-chat-row { display: flex; align-items: flex-end; gap: 6px; }
        .cw-chat-row.mine { flex-direction: row-reverse; }
        .cw-chat-bubble { max-width: 78%; padding: 6px 10px; border-radius: 14px; background: var(--mud-palette-background-gray); color: var(--mud-palette-text-primary); overflow-wrap: anywhere; }
        .cw-chat-row.mine .cw-chat-bubble { background: var(--mud-palette-primary); color: var(--mud-palette-primary-text); border-bottom-right-radius: 4px; }
        .cw-chat-row.theirs .cw-chat-bubble { border-bottom-left-radius: 4px; }
        .cw-chat-bubble.deleted { opacity: .7; font-style: italic; }
        .cw-chat-html p { margin: 0; }
        .cw-chat-html ul, .cw-chat-html ol { margin: 2px 0; padding-left: 20px; }
        .cw-chat-html blockquote { margin: 2px 0; padding-left: 8px; border-left: 3px solid currentColor; opacity: .85; }
        .cw-chat-html pre, .cw-chat-html code { font-family: ui-monospace, Consolas, monospace; font-size: .85em; background: rgba(127,127,127,.18); border-radius: 4px; padding: 0 3px; }
        .cw-chat-html pre { padding: 6px; white-space: pre-wrap; }
        .cw-chat-html a { color: inherit; text-decoration: underline; }
        .cw-chat-html a.cw-mention, .cw-chat-html .cw-mention { text-decoration: none; font-weight: 600; padding: 0 4px; border-radius: 8px; background: rgba(127,127,127,.2); }
        .cw-chat-html .cw-restricted { font-weight: 400; font-style: italic; opacity: .8; }
        .cw-chat-html .cw-restricted::before { content: "🔒 "; }
        .cw-chat-sender { font-size: .75rem; font-weight: 600; color: var(--mud-palette-primary); }
        .cw-chat-row.mine .cw-chat-sender { color: inherit; }
        .cw-chat-quote { font-size: .8rem; border-left: 3px solid currentColor; padding-left: 6px; margin-bottom: 4px; opacity: .8; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
        .cw-chat-meta { font-size: .7rem; opacity: .7; display: flex; gap: 4px; justify-content: flex-end; align-items: center; }
        .cw-chat-muted { opacity: .7; }
        .cw-chat-ellipsis { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; }
        .cw-chat-image img { display: block; max-width: 100%; max-height: 220px; border-radius: 8px; margin-top: 4px; }
        .cw-chat-file { display: flex; gap: 6px; align-items: center; color: inherit; text-decoration: none; margin-top: 4px; padding: 4px 6px; border-radius: 8px; background: rgba(127,127,127,.18); }
        .cw-chat-reactions { display: flex; flex-wrap: wrap; gap: 2px; margin-top: 2px; }
        .cw-chat-reaction { border: 1px solid var(--mud-palette-lines-default); background: var(--mud-palette-surface); color: var(--mud-palette-text-primary); border-radius: 10px; padding: 0 6px; font-size: .75rem; cursor: pointer; }
        .cw-chat-reaction.own { border-color: var(--mud-palette-primary); }
        .cw-chat-actions { opacity: 0; transition: opacity .15s; }
        .cw-chat-row:hover .cw-chat-actions, .cw-chat-actions:focus-within { opacity: 1; }
        @media (hover: none) { .cw-chat-actions { opacity: .6; } }
        .cw-chat-typing { font-size: .75rem; opacity: .7; padding: 0 6px; font-style: italic; }
        .cw-chat-composer { border-top: 1px solid var(--mud-palette-lines-default); padding-top: 4px; }
        .cw-chat-composer.cw-chat-drop { outline: 2px dashed var(--mud-palette-primary); outline-offset: 2px; }
        .cw-chat-context { font-size: .8rem; padding: 2px 6px; border-left: 3px solid var(--mud-palette-primary); margin-bottom: 4px; }
        .cw-chat-editor { min-width: 0; }
        .cw-chat-editor .tt-container, .cw-chat-editor .ql-container { max-height: 140px; overflow-y: auto; }
        .cw-chat-thread.compact .cw-chat-editor .ql-toolbar, .cw-chat-thread.compact .cw-chat-editor .tt-toolbar { display: none; }
        .cw-chat-person { cursor: pointer; border-radius: 6px; }
        .cw-chat-person:hover { background: var(--mud-palette-action-default-hover); }
        .cw-chat-dock { position: fixed; bottom: 0; right: 96px; z-index: 1140; display: flex; flex-direction: row-reverse; align-items: flex-end; gap: 8px; pointer-events: none; }
        .cw-chat-window { pointer-events: auto; width: 328px; background: var(--mud-palette-surface); color: var(--mud-palette-text-primary); border: 1px solid var(--mud-palette-lines-default);
          border-bottom: 0; border-radius: 10px 10px 0 0; box-shadow: var(--mud-elevation-8); display: flex; flex-direction: column; }
        .cw-chat-window.open { height: 440px; }
        .cw-chat-window-head { display: flex; align-items: center; gap: 6px; padding: 4px 4px 4px 8px; cursor: pointer; border-bottom: 1px solid var(--mud-palette-lines-default); min-height: 44px; }
        .cw-chat-window-head .mud-icon-button { padding: 4px; }
        .cw-chat-window-body { flex: 1; min-height: 0; padding: 0 6px 6px; }
        @media (max-width: 600px) {
          .cw-chat-dock { right: 8px; left: 8px; justify-content: flex-start; flex-wrap: wrap-reverse; }
          .cw-chat-window { width: auto; flex: 1 1 140px; }
          .cw-chat-window.open { position: fixed; inset: 0; height: auto; width: auto; border-radius: 0; z-index: 1400; }
        }
        """;
}

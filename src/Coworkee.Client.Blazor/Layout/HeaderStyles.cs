namespace Coworkee.Client.Blazor.Layout;

/// <summary>
/// The app bars at the left or right edge (narrow columns of icons) and the room the page and the navigation leave them; the layout sets the
/// cw-bar-* classes from <see cref="Navigation.HeaderLayout"/>.
/// </summary>
internal static class HeaderStyles
{
    public const string Css =
        """
        :root { --cw-side-bar-width: 64px; }
        .cw-side-bar { position: fixed; top: 0; bottom: 0; width: var(--cw-side-bar-width); z-index: calc(var(--mud-zindex-appbar) + 2);
          display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 12px 0; overflow-y: auto;
          background: var(--mud-palette-appbar-background); color: var(--mud-palette-appbar-text); }
        .cw-side-bar-left { inset-inline-start: 0; border-inline-end: 1px solid var(--mud-palette-lines-default); }
        .cw-side-bar-right { inset-inline-end: 0; border-inline-start: 1px solid var(--mud-palette-lines-default); }
        .cw-bar-spacer { flex: 1 1 auto; }
        .cw-side-bar .cw-palette-trigger-text, .cw-side-bar .cw-palette-kbd, .cw-side-bar .mud-button-end-icon { display: none; }
        .cw-side-bar .cw-palette-trigger { padding: 8px; }
        .cw-side-bar .mud-button-root:not(.mud-icon-button) { min-width: 0; padding: 4px; margin: 0; }
        .cw-side-bar .mud-button-root:not(.mud-icon-button) .mud-button-label { flex-direction: column; gap: 2px; font-size: .7rem; line-height: 1.1; }
        .cw-side-bar .cw-user-name { margin: 0 !important; max-width: calc(var(--cw-side-bar-width) - 8px); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .cw-bar-right .mud-main-content { padding-inline-end: var(--cw-side-bar-width); }
        .cw-bar-right .mud-appbar { padding-inline-end: var(--cw-side-bar-width); }
        .cw-bar-left .mud-main-content { padding-inline-start: var(--cw-side-bar-width); }
        .cw-bar-left .mud-appbar { padding-inline-start: var(--cw-side-bar-width); }
        .cw-bar-left .mud-drawer.mud-drawer-pos-left { left: var(--cw-side-bar-width); }
        .mud-application-layout-rtl .cw-bar-left .mud-drawer.mud-drawer-pos-right { right: var(--cw-side-bar-width); }
        .cw-no-top-bar .mud-main-content { padding-top: 0; }
        .cw-bar-bottom .mud-main-content { padding-bottom: var(--mud-appbar-height); }
        /* what floats in a corner (the assistant) keeps clear of the bars; left and right follow the reading direction */
        .cw-bar-left { --cw-edge-start: var(--cw-side-bar-width); }
        .cw-bar-right { --cw-edge-end: var(--cw-side-bar-width); }
        .cw-bar-bottom { --cw-edge-bottom: var(--mud-appbar-height); }
        .cw-no-top-bar { --cw-edge-top: 0px; }
        .cw-drawer-head { display: flex; align-items: center; justify-content: space-between; gap: 4px; min-height: var(--mud-appbar-height); padding: 0 8px; }
        .cw-drawer-brand { display: flex; align-items: center; min-width: 0; overflow: hidden; }
        .mud-drawer--closed .cw-drawer-head { justify-content: center; padding: 0; }
        """;
}

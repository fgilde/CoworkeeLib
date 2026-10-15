namespace Coworkee.Client.Blazor.Layout;

/// <summary>
/// Phone and touch rules of the shell and the library pages: safe areas of notched devices, touch targets of at least 44 px, page headers that wrap,
/// edit sheets and dialogs that fill the phone, facet menus as bottom sheets and an action column that stays visible while a wide table scrolls.
/// </summary>
internal static class MobileStyles
{
    public const string Css =
        """
        .mud-layout .mud-appbar { padding-top: env(safe-area-inset-top); padding-left: env(safe-area-inset-left); padding-right: env(safe-area-inset-right); }
        .mud-layout .mud-main-content { margin-top: env(safe-area-inset-top); padding-bottom: env(safe-area-inset-bottom);
          padding-left: env(safe-area-inset-left); padding-right: env(safe-area-inset-right); }
        .mud-layout .mud-drawer { padding-top: env(safe-area-inset-top); padding-bottom: env(safe-area-inset-bottom); }

        @media (pointer: coarse) {
          .mud-icon-button { min-width: 44px; min-height: 44px; }
          .mud-nav-link, .mud-menu-item, .mud-list-item { min-height: 44px; }
        }

        @media (min-width: 600px) {
          .mud-table-root .cw-row-actions { position: sticky; inset-inline-end: 0; z-index: 1; background-color: var(--mud-palette-surface); }
          .mud-table-hover .mud-table-row:hover > .cw-row-actions { background-color: var(--mud-palette-table-hover); }
        }

        @media (max-width: 599.98px) {
          .mud-main-content > .mud-container { padding-left: 12px; padding-right: 12px; }
          .cw-user-name, .mud-drawer [data-testid='nav-pin'] { display: none; }
          .cw-page-header { flex-wrap: wrap; row-gap: 8px; }
          .cw-page-header > :first-child { flex: 1 1 100%; }
          .cw-table-search { flex: 1 1 100%; max-width: none !important; margin-right: 0 !important; }
          .cw-group-row:has(.cw-group-zone:not(.grouped)) { display: none; }

          .mud-dialog-container .mud-dialog { max-width: calc(100vw - 16px) !important; }
          .mud-dialog-container .mud-dialog[class*="height-full"], .mud-dialog-container .mud-dialog.mud-ex-object-edit-dialog {
            inset: 0 !important; width: 100vw !important; max-width: 100vw !important; height: 100dvh !important; max-height: 100dvh !important;
            margin: 0 !important; border-radius: 0 !important; padding-top: env(safe-area-inset-top); padding-bottom: env(safe-area-inset-bottom); }

          .mud-popover.cw-facet-popover {
            position: fixed !important; inset: auto 0 0 0 !important; width: 100vw !important; max-width: 100vw !important; max-height: 80dvh !important;
            overflow-y: auto; transform: none !important; border-radius: 16px 16px 0 0 !important; padding-bottom: env(safe-area-inset-bottom); }
          .mud-popover.cw-facet-popover .cw-facet-card { width: auto; max-width: none; }

          .cw-ai-fab { right: 16px; bottom: calc(16px + env(safe-area-inset-bottom)); }
        }
        """;
}

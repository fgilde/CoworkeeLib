namespace Coworkee.Client.Blazor.Ai;

/// <summary>Moves the assistant's button and window to the corner of the display setting; phones keep the window full screen.</summary>
internal static class AssistantPlacementStyles
{
    public const string Css =
        """
        .cw-ai-launcher.left .cw-ai-fab { inset-inline-end: auto; inset-inline-start: calc(24px + var(--cw-edge-start, 0px)); }
        @media (min-width: 601px) {
          .cw-ai-launcher.left .cw-ai-window:not(.full) { inset-inline-end: auto; inset-inline-start: calc(24px + var(--cw-edge-start, 0px)); }
          .cw-ai-launcher.docked .cw-ai-window:not(.full) { top: calc(var(--mud-appbar-height, 64px) + 8px); bottom: auto; }
        }
        """;
}

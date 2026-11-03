using Coworkee.Client.Blazor.CommandPalette;

namespace Coworkee.Client.Blazor.Components;

public sealed record PaletteHit(CommandPaletteItem Item, int Score, IReadOnlyList<int> Positions);

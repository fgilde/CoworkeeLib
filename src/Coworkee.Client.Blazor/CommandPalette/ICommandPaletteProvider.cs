namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>Adds items to the command palette (Ctrl+K); register with AddCommandPaletteProvider.</summary>
public interface ICommandPaletteProvider
{
    /// <summary>Items for the text typed so far, empty when the palette just opened. The palette filters items unless they are <see cref="CommandPaletteItem.Matched"/>.</summary>
    Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken);
}

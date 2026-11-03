namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>An action of the app in the palette, e.g. "New product"; <see cref="Href"/> or <see cref="Run"/> does it.</summary>
public sealed record CommandPaletteCommand(string Title, string Icon, string? Permission = null, string? Href = null, Func<IServiceProvider, Task>? Run = null, string? Keywords = null);

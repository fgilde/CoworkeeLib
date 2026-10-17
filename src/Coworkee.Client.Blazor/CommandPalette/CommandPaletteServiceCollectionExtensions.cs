using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.CommandPalette;

public static class CommandPaletteServiceCollectionExtensions
{
    /// <summary>Adds an own source of palette items, e.g. help articles or customers from another service.</summary>
    public static IServiceCollection AddCommandPaletteProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, ICommandPaletteProvider =>
        services.AddScoped<ICommandPaletteProvider, TProvider>();

    /// <summary>An action under "Actions", e.g. new("New product", Icons.Material.Outlined.Add, CatalogPermissions.Products.Create, Href: "/catalog/products?new=1").</summary>
    public static IServiceCollection AddCommandPaletteCommand(this IServiceCollection services, CommandPaletteCommand command) =>
        services.AddSingleton(command);

    internal static IServiceCollection AddCommandPalette(this IServiceCollection services) =>
        services
            .AddCommandPaletteProvider<PagesPaletteProvider>()
            .AddCommandPaletteProvider<ActionsPaletteProvider>()
            .AddCommandPaletteProvider<ChatPaletteProvider>()
            .AddCommandPaletteProvider<AssistantPaletteProvider>();
}

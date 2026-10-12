using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Ai;

/// <summary>The providers of <see cref="AiProviderCatalog"/> as cards with logo and tags; <see cref="Value"/> is the chosen id.</summary>
public partial class AiProviderPicker
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string> ValueChanged { get; set; }

    private static string CardStyle(bool selected) =>
        "height:100%;transition:border-color .15s;" + (selected ? "border:2px solid var(--mud-palette-primary);" : "border:2px solid transparent;outline:1px solid var(--mud-palette-lines-default);outline-offset:-2px;");

    private static IEnumerable<(string Text, Color Color)> Tags(AiProviderInfo provider)
    {
        if (provider.Local)
        {
            yield return ("Local", Color.Success);
        }

        if (provider.FreeTier)
        {
            yield return ("Free tier", Color.Info);
        }

        if (provider.Id == AiProviderCatalog.Custom)
        {
            yield return ("Any address", Color.Default);
        }
        else if (provider.Key == AiField.Required)
        {
            yield return ("Key", Color.Default);
        }
    }

    private Task SelectAsync(AiProviderInfo provider) => provider.Id == Value ? Task.CompletedTask : ValueChanged.InvokeAsync(provider.Id);

    private Task KeyAsync(KeyboardEventArgs e, AiProviderInfo provider) => e.Key is "Enter" or " " ? SelectAsync(provider) : Task.CompletedTask;
}

using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>Renders every MudExObjectEdit field dense when the theme is; a model's own IObjectMetaConfiguration replaces it.</summary>
public sealed class DenseObjectEditMeta<T>(ThemeService themes) : IObjectMetaConfiguration<T>
{
    public Task ConfigureAsync(ObjectEditMeta<T> meta)
    {
        if (themes.Theme.Dense)
        {
            // MudEx drops attributes an editor does not have
            meta.AllProperties.WithAdditionalAttribute(nameof(MudTextField<string>.Margin), Margin.Dense);
            meta.AllProperties.WithAdditionalAttribute(nameof(MudCheckBox<bool>.Dense), true);
        }

        return Task.CompletedTask;
    }
}

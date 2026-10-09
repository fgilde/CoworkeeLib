namespace Coworkee.Client.Blazor.Components.About;

/// <summary>The about dialog renders its <see cref="Sections"/> in order; remove, insert or swap them, or replace the whole <see cref="AboutDialog"/>.</summary>
public sealed class AboutOptions
{
    public List<Type> Sections { get; } = [typeof(AboutHeader), typeof(AboutCredits), typeof(AboutLinks), typeof(AboutFooter)];

    public List<AboutCredit> Credits { get; } =
    [
        new("Coworkee", "https://fgilde.github.io/CoworkeeLib", "https://fgilde.github.io/CoworkeeLib/assets/icon.svg", AboutVersion.Of(typeof(CoworkeeClientOptions).Assembly)),
        new("Nextended", "https://fgilde.github.io/Nextended", "https://raw.githubusercontent.com/fgilde/Nextended/main/icon-readme.png", AboutVersion.Of(typeof(Nextended.Core.Extensions.ObjectExtensions).Assembly)),
        new("MudBlazor.Extensions", "https://mudex.org", "https://www.mudex.org/sample-data/logo.png", AboutVersion.Of(typeof(MudBlazor.Extensions.Options.DialogOptionsEx).Assembly)),
    ];
}

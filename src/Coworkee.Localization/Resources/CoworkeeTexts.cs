namespace Coworkee.Localization.Resources;

/// <summary>Texts of the Coworkee shell, admin pages and MudBlazor components.</summary>
internal sealed class CoworkeeTexts : ILocalizationResourceContributor
{
    public void Define(LocalizationResourceContext context) => context.AddEmbeddedJson(typeof(CoworkeeTexts).Assembly);
}

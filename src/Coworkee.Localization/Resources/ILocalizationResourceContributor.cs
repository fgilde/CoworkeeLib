namespace Coworkee.Localization.Resources;

/// <summary>Default texts of a module. Keys are the English texts; English needs no resource.</summary>
public interface ILocalizationResourceContributor
{
    void Define(LocalizationResourceContext context);
}

namespace Coworkee.Application.Localization;

/// <summary>The default language of the current organisation, else of the system; the localization module provides it.</summary>
public interface IDefaultLanguage
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
}

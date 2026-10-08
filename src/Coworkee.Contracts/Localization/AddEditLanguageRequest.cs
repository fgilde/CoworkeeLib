namespace Coworkee.Contracts.Localization;

public sealed class AddEditLanguageRequest
{
    public string Culture { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool IsDefault { get; set; }
}

namespace Coworkee.Localization;

/// <summary>Fails the start of a host whose configured languages are no cultures or miss the default.</summary>
internal sealed class LocalizationOptionsValidation : Microsoft.Extensions.Options.IValidateOptions<LocalizationOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, LocalizationOptions options)
    {
        var unknown = options.Languages.Where(c => !Languages.InitialLanguages.IsCulture(c)).ToList();
        if (unknown.Count > 0)
        {
            return Microsoft.Extensions.Options.ValidateOptionsResult.Fail($"{LocalizationOptions.Section}:Languages: unknown cultures {string.Join(", ", unknown)}.");
        }

        return options is { DefaultLanguage.Length: > 0, Languages.Count: > 0 } && !options.Languages.Contains(options.DefaultLanguage, StringComparer.OrdinalIgnoreCase)
            ? Microsoft.Extensions.Options.ValidateOptionsResult.Fail($"{LocalizationOptions.Section}:DefaultLanguage: {options.DefaultLanguage} is not one of the languages.")
            : Microsoft.Extensions.Options.ValidateOptionsResult.Success;
    }
}

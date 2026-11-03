namespace Coworkee.Theming;

/// <summary>Fails the start of a host whose app themes cannot be seeded.</summary>
internal sealed class ThemingOptionsValidation : Microsoft.Extensions.Options.IValidateOptions<ThemingOptions>
{
    public Microsoft.Extensions.Options.ValidateOptionsResult Validate(string? name, ThemingOptions options) =>
        ThemeSeeds.Problem(options) is { } problem ? Microsoft.Extensions.Options.ValidateOptionsResult.Fail(problem) : Microsoft.Extensions.Options.ValidateOptionsResult.Success;
}

namespace Coworkee.Contracts.Identity;

public sealed record CompleteSetupRequest(
    string SetupToken, string TenantName, string AdminEmail, string AdminPassword, string? AdminFirstName, string? AdminLastName,
    IReadOnlyDictionary<string, string?>? Settings = null,
    Guid? ThemeId = null);

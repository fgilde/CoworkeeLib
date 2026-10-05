namespace Coworkee.Identity.Setup;

public sealed record SeedUser(string Email, string Password, string? FirstName = null, string? LastName = null, bool IsAdmin = false)
{
    public IReadOnlyList<string> Roles { get; init; } = [];
}

using Microsoft.Extensions.Configuration;

namespace Coworkee.Identity.Setup;

/// <summary>
/// The first administrator of a deployment (<c>Coworkee:InitialAdmin:Email</c> and <c>:Password</c>, deploy parameters of the app host):
/// the migrations set up an empty database with it. Without both the system waits for the setup page and its token.
/// </summary>
public static class InitialAdmin
{
    public const string Section = "Coworkee:InitialAdmin";

    public static SeedUser? From(IConfiguration configuration) =>
        configuration[$"{Section}:Email"] is { Length: > 0 } email && configuration[$"{Section}:Password"] is { Length: > 0 } password
            ? new SeedUser(email, password, configuration[$"{Section}:FirstName"] ?? "Admin", configuration[$"{Section}:LastName"], IsAdmin: true)
            : null;
}

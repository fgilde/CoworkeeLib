namespace MyApp.Migrations;

/// <summary>
/// Development users and roles, generated with the solution: seeded into the app and, with Keycloak, into its realm with the same password (see LOGIN.md).
/// The passwords are for development only; a deployment starts without them.
/// </summary>
public static class DemoUsers
{
    public static readonly DemoRole[] Roles =
    [
    ];

    public static readonly DemoUser[] All =
    [
        new("admin@myapp.local", "Administrator", null, "Cw1!SEED_PASSWORD", IsAdmin: true),
    ];
}

public sealed record DemoUser(string Email, string FirstName, string? LastName, string Password, bool IsAdmin = false, string[]? Roles = null);

public sealed record DemoRole(string Name, string? Description, string[] Permissions);

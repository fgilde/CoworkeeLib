using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>A user on the command line: email[:role,role][:admin], e.g. ada@shop.test:Product Manager or bob@shop.test:admin.</summary>
internal static class UserSyntax
{
    public const string Format = "email[:role,role][:admin]";

    public const string Description = "A development user, seeded with a generated password (and into Keycloak with the same one): " + Format + "; repeat for more users.";

    public static UserSpec Parse(string text)
    {
        var parts = text.Split(':', StringSplitOptions.TrimEntries);
        var user = new UserSpec { Email = parts[0] };
        foreach (var part in parts.Skip(1))
        {
            if (part.Equals("admin", StringComparison.OrdinalIgnoreCase))
            {
                user.Admin = true;
            }
            else
            {
                user.Roles.AddRange(part.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return user;
    }
}

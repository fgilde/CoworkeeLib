using System.Text.RegularExpressions;

namespace Coworkee.Cli.Spec;

public static partial class SpecValidator
{
    /// <summary>A role or display name that can go into C# string literals and Markdown as it is.</summary>
    public static bool IsSafeName(string? name) => name is { Length: > 0 and <= 64 } && SafeName().IsMatch(name);

    public static bool IsEmail(string? email) => email is { Length: <= 128 } && Email().IsMatch(email);

    /// <summary>What ASP.NET Core Identity asks of a password by default: 8 characters, upper and lower case, a digit and a symbol; no quotes or backslashes, it goes into code.</summary>
    public static bool IsPassword(string? password) => password is { Length: >= 8 and <= 128 }
        && password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c))
        && !password.Any(c => c is '"' or '\\' or '`' or '|' || char.IsControl(c) || char.IsWhiteSpace(c));

    private static void UsersAndRoles(List<string> errors, AppSpec spec)
    {
        var permissions = PermissionCatalog.For(spec).ToHashSet(StringComparer.Ordinal);
        var builtIn = PermissionCatalog.BuiltInRoles(spec);
        for (var i = 0; i < spec.Roles.Count; i++)
        {
            var role = spec.Roles[i];
            if (!IsSafeName(role.Name))
            {
                errors.Add($"roles[{i}].name: '{role.Name}' must be 1 to 64 letters, digits, spaces, '.', '-' or '_'.");
            }
            else if (builtIn.Contains(role.Name, StringComparer.OrdinalIgnoreCase) || spec.Roles.Take(i).Any(r => r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"roles[{i}].name: '{role.Name}' exists already.");
            }

            if (role.Description is { } description && !IsSafeTitle(description))
            {
                errors.Add($"roles[{i}].description: must not contain quotes, backslashes, <, >, @ or control characters.");
            }

            foreach (var permission in role.Permissions.Where(p => !permissions.Contains(p)))
            {
                errors.Add($"roles[{i}].permissions: '{permission}' is not a permission of the app; known are {string.Join(", ", permissions)}.");
            }
        }

        var roles = new HashSet<string>([.. builtIn, .. spec.Roles.Select(r => r.Name)], StringComparer.OrdinalIgnoreCase);
        var admin = $"admin@{spec.Name.ToLowerInvariant()}.local";
        for (var i = 0; i < spec.Users.Count; i++)
        {
            var user = spec.Users[i];
            if (!IsEmail(user.Email))
            {
                errors.Add($"users[{i}].email: '{user.Email}' is not an email address.");
            }
            else if (user.Email.Equals(admin, StringComparison.OrdinalIgnoreCase) || spec.Users.Take(i).Any(u => u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"users[{i}].email: '{user.Email}' exists already.");
            }

            if (user.DisplayName is { } name && !IsSafeName(name))
            {
                errors.Add($"users[{i}].displayName: '{name}' must be 1 to 64 letters, digits, spaces, '.', '-' or '_'.");
            }

            if (user.Password is { } password && !IsPassword(password))
            {
                errors.Add($"users[{i}].password: needs 8 or more characters with upper and lower case letters, a digit and a symbol, and no quotes, backslashes, | or spaces.");
            }

            foreach (var role in user.Roles.Where(r => !roles.Contains(r)))
            {
                errors.Add($"users[{i}].roles: '{role}' is no role of the spec or the template ({string.Join(", ", roles)}).");
            }
        }
    }

    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} ._-]*$")]
    private static partial Regex SafeName();

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")]
    private static partial Regex Email();
}

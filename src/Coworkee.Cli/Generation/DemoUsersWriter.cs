using System.Globalization;
using System.Security.Cryptography;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>The users and roles of a spec go into DemoUsers.cs of the migrations project (seed and Keycloak realm) and into LOGIN.md.</summary>
internal static class DemoUsersWriter
{
    public static void Write(Solution solution, AppSpec spec)
    {
        if (spec.Users.Count == 0 && spec.Roles.Count == 0)
        {
            return;
        }

        var users = spec.Users.Select(u => (Spec: u, Password: u.Password ?? NewPassword())).ToList();
        var file = Path.Combine(Path.GetDirectoryName(solution.Project("Migrations"))!, "DemoUsers.cs");
        var code = File.ReadAllText(file);
        code = InsertBeforeClose(code, "DemoRole[] Roles =", spec.Roles.Select(r =>
            $"        new({Literal(r.Name)}, {(r.Description is null ? "null" : Literal(r.Description))}, [{string.Join(", ", r.Permissions.Select(Literal))}]),"));
        code = InsertBeforeClose(code, "DemoUser[] All =", users.Select(u =>
        {
            var (first, last) = Names(u.Spec);
            return $"        new({Literal(u.Spec.Email)}, {Literal(first)}, {(last is null ? "null" : Literal(last))}, {Literal(u.Password)}, IsAdmin: {(u.Spec.Admin ? "true" : "false")}, Roles: [{string.Join(", ", u.Spec.Roles.Select(Literal))}]),";
        }));
        File.WriteAllText(file, code);

        var login = Path.Combine(solution.Root, "LOGIN.md");
        if (File.Exists(login))
        {
            var lines = File.ReadAllLines(login).ToList();
            var last = lines.FindLastIndex(l => l.StartsWith("| `", StringComparison.Ordinal));
            lines.InsertRange(last + 1, users.Select(u =>
                $"| `{u.Spec.Email}` | `{u.Password}` | {new SignInUser(u.Spec.Email, u.Password, u.Spec.Admin, u.Spec.Roles).RoleText} |"));
            File.WriteAllLines(login, lines);
        }
    }

    // ponytail: same shape as the template's generated administrator password
    public static string NewPassword() => "Cw1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>First and last name of the display name, or of the email's local part (ada.lovelace becomes Ada Lovelace).</summary>
    public static (string First, string? Last) Names(UserSpec user)
    {
        var words = (user.DisplayName ?? string.Join(' ', user.Email.Split('@')[0].Split('.', '_', '-', '+').Where(w => w.Length > 0)
            .Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]))).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => (user.Email, null),
            1 => (words[0], null),
            _ => (words[0], words[1]),
        };
    }

    private static string InsertBeforeClose(string code, string marker, IEnumerable<string> lines)
    {
        var close = code.IndexOf("    ];", code.IndexOf(marker, StringComparison.Ordinal), StringComparison.Ordinal);
        var newLine = code.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return code.Insert(close, string.Concat(lines.Select(l => l + newLine)));
    }

    // the validator lets no quotes or backslashes through, so a plain literal is enough
    private static string Literal(string value) => $"\"{value}\"";
}

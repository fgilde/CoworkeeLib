using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

/// <summary>The framed sign-in notice at the end of coworkee new: where to sign in, every user with password and roles, the Keycloak note.</summary>
internal static class SignInBox
{
    public static IReadOnlyList<string> Lines(SignInInfo info)
    {
        var email = info.Users.Max(u => u.Email.Length);
        var password = info.Users.Max(u => u.Password.Length);
        List<string> body =
        [
            $"coworkee run, then open {info.WebResource} from the Aspire dashboard and sign in as:",
            string.Empty,
            .. info.Users.Select(u => $"  {u.Email.PadRight(email)}  {u.Password.PadRight(password)}  {u.RoleText}".TrimEnd()),
            string.Empty,
        ];
        if (info.Keycloak)
        {
            body.Add("The Keycloak button takes the same users and passwords.");
        }

        body.Add("Development passwords only, also in LOGIN.md; change them before you deploy.");
        var width = body.Max(l => l.Length);
        return
        [
            "+-- Sign in " + new string('-', width - 9) + "+",
            .. body.Select(l => $"| {l.PadRight(width)} |"),
            "+" + new string('-', width + 2) + "+",
        ];
    }
}

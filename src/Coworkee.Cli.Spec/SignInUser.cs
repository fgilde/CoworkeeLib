namespace Coworkee.Cli.Spec;

public sealed record SignInUser(string Email, string Password, bool IsAdmin, IReadOnlyList<string> Roles)
{
    public string RoleText => string.Join(", ", (IsAdmin ? ["Administrator"] : Array.Empty<string>()).Concat(Roles));
}

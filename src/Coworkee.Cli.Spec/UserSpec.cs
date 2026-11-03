using System.ComponentModel;

namespace Coworkee.Cli.Spec;

public sealed class UserSpec
{
    public string Email { get; set; } = string.Empty;

    [Description("First and last name (default: the part of the email before @).")]
    public string? DisplayName { get; set; }

    [Description("Names of roles of the spec or built-in ones.")]
    public List<string> Roles { get; set; } = [];

    [Description("Administrator with every permission.")]
    public bool Admin { get; set; }

    [Description("Development password (default: generated); at least 8 characters with upper and lower case letters, a digit and a symbol.")]
    public string? Password { get; set; }
}

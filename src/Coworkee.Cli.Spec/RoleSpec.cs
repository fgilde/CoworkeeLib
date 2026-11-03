using System.ComponentModel;

namespace Coworkee.Cli.Spec;

public sealed class RoleSpec
{
    [Description("Role name, e.g. Sales Manager.")]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Description("Permissions of the app's modules, e.g. Sales.Orders.View; validation names the known ones.")]
    public List<string> Permissions { get; set; } = [];
}

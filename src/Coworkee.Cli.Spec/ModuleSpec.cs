using System.ComponentModel;

namespace Coworkee.Cli.Spec;

public sealed class ModuleSpec
{
    [Description("Module name, e.g. Sales; becomes the project <App>.Sales.")]
    public string Name { get; set; } = string.Empty;

    public List<EntitySpec> Entities { get; set; } = [];
}

using System.ComponentModel;

namespace Coworkee.Cli.Spec;

public sealed class EnumSpec
{
    [Description("Enum name, e.g. OrderStatus.")]
    public string Name { get; set; } = string.Empty;

    [Description("Values in their order, e.g. [\"Open\", \"Paid\", \"Shipped\"]; the first is the default.")]
    public List<string> Values { get; set; } = [];
}

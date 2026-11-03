using Coworkee.Studio.Localization;

namespace Coworkee.Studio.Views;

public sealed record McpTool(string Name, string Description)
{
    public string Text => L.Get(Description);
}

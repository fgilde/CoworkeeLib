using Coworkee.Studio.Localization;

namespace Coworkee.Studio.Views;

public sealed record CliCommandHelp(string Command, string Description)
{
    public string Text => L.Get(Description);
}

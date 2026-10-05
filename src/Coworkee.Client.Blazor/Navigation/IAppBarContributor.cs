namespace Coworkee.Client.Blazor.Navigation;

public sealed record AppBarItem(Type Component, int Order = 0, IDictionary<string, object>? Parameters = null);

public interface IAppBarContributor
{
    IEnumerable<AppBarItem> Items { get; }
}

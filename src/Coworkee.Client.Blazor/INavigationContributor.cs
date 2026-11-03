namespace Coworkee.Client.Blazor;

public interface INavigationContributor
{
    IEnumerable<CoworkeeNavItem> Items { get; }
}

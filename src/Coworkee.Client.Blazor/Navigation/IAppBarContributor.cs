namespace Coworkee.Client.Blazor.Navigation;

public interface IAppBarContributor
{
    IEnumerable<AppBarItem> Items { get; }
}

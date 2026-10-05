using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeNavMenu
{
    [Inject] private IEnumerable<INavigationContributor> Contributors { get; set; } = null!;

    [Inject] private IOptions<NavigationMenuOptions> MenuOptions { get; set; } = null!;

    private NavigationMenuOptions Options => MenuOptions.Value;

    private IEnumerable<CoworkeeNavItem> Items => Contributors.SelectMany(c => c.Items);
}

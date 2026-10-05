using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class CoworkeeAppBar
{
    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Parameter] public bool Dark { get; set; }

    [Parameter] public EventCallback ToggleDrawer { get; set; }

    [Parameter] public EventCallback ToggleDark { get; set; }

    private IEnumerable<AppBarItem> Items => Contributors.SelectMany(c => c.Items).OrderBy(i => i.Order);
}

using Coworkee.Contracts.ApiDocs;

namespace Coworkee.Client.Blazor.Navigation;

internal sealed class ApiDocsNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items => [new("API", "/swagger", MudBlazor.Icons.Material.Outlined.Api, ApiDocsPermissions.View, ForceLoad: true, Order: -20)];
}

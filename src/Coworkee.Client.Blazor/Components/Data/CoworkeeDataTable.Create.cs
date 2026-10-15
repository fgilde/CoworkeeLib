using System.Web;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    /// <summary>"?new=1" on the page opens the create dialog once, e.g. from a "New product" command of the palette.</summary>
    public const string CreateParameter = "new";

    [Inject] private Security.PermissionStore Permissions { get; set; } = null!;

    private async Task CreateFromUrlAsync()
    {
        if (!OnCreate.HasDelegate || HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query)[CreateParameter] != "1")
        {
            return;
        }

        Nav.NavigateTo(Nav.GetUriWithQueryParameter(CreateParameter, (string?)null), replace: true);
        if (CreatePermission is null || await Permissions.HasAsync(CreatePermission))
        {
            await CreateAsync();
        }
    }
}

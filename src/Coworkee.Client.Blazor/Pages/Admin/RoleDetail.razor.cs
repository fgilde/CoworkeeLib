using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class RoleDetail
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    private RoleDto? _role;

    [Parameter] public Guid Id { get; set; }

    protected override async Task OnParametersSetAsync() => _role = (await Api.GetRolesAsync()).FirstOrDefault(r => r.Id == Id);
}

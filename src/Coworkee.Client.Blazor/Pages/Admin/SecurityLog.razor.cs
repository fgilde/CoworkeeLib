using Coworkee.Client.Blazor.Auditing;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class SecurityLog
{
    private static readonly string[] SearchFields = [nameof(SecurityEventDto.Action), nameof(SecurityEventDto.Ip), nameof(SecurityEventDto.Reason)];
    private CoworkeeDataTable<SecurityEventDto> _table = null!;
    private DateRange? _range;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private NavigationManager Navigation { get; set; } = null!;

    /// <summary>The user page links here with <c>?user=</c>.</summary>
    [SupplyParameterFromQuery(Name = "user")] private Guid? UserFilter { get; set; }

    private string? Filter => ODataFilter.And(
        UserFilter is { } user ? $"{nameof(SecurityEventDto.UserId)} eq {user}" : null,
        LogPeriod.From(_range, nameof(SecurityEventDto.At)),
        LogPeriod.To(_range, nameof(SecurityEventDto.At)));

    private void ClearUser() => Navigation.NavigateTo(Links.Href("/admin/security-log")!);

    private async Task RangeAsync(DateRange? range)
    {
        _range = range;
        StateHasChanged();
        await Task.Yield();
        await _table.ReloadAsync();
    }
}

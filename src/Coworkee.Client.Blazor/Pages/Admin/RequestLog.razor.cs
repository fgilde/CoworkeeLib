using Coworkee.Client.Blazor.Auditing;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class RequestLog
{
    private static readonly string[] SearchFields = [nameof(RequestLogDto.Path), nameof(RequestLogDto.RequestType), nameof(RequestLogDto.Ip)];
    private CoworkeeDataTable<RequestLogDto> _table = null!;
    private DateRange? _range;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private string? Filter => ODataFilter.And(LogPeriod.From(_range, nameof(RequestLogDto.At)), LogPeriod.To(_range, nameof(RequestLogDto.At)));

    private static Color StatusColor(int status) => status switch
    {
        >= 500 => Color.Error,
        >= 400 => Color.Warning,
        _ => Color.Success,
    };

    private async Task RangeAsync(DateRange? range)
    {
        _range = range;
        StateHasChanged();
        await Task.Yield();
        await _table.ReloadAsync();
    }
}

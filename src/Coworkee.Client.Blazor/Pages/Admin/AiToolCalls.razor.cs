using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class AiToolCalls
{
    private static readonly string[] SearchFields = [nameof(AiToolCallDto.Tool), nameof(AiToolCallDto.Input)];
    private IReadOnlyDictionary<Guid, string> _names = new Dictionary<Guid, string>();

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private async Task LoadNamesAsync(IReadOnlyList<AiToolCallDto> calls)
    {
        var ids = calls.Select(c => c.UserId).OfType<Guid>().Distinct().ToList();
        if (ids.Count > 0)
        {
            await Snackbar.RunAsync(async () => _names = await Api.GetUserNamesAsync(ids));
        }
    }
}

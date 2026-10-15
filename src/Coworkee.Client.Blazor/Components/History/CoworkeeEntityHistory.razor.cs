using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.History;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

/// <summary>
/// The changes of one record over time (who, when, which fields), for entity sets registered with AddEntityHistory on the server.
/// Users with the restore permission can go back to an earlier state or undo a single change; both run through the app's update command.
/// </summary>
public partial class CoworkeeEntityHistory
{
    private EntityHistoryDto? _history;

    [Inject] private IEntityHistoryApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid Id { get; set; }

    /// <summary>The label of a field; by default "BrandId" reads "Brand", "ShowLogo" "Show logo", translated.</summary>
    [Parameter] public Func<string, string>? FieldLabel { get; set; }

    [Parameter] public EventCallback OnRestored { get; set; }

    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    public async Task ReloadAsync()
    {
        try
        {
            _history = await Api.GetAsync(EntitySet, Id);
        }
        catch (ApiException exception)
        {
            _history = new EntityHistoryDto(false, false, []);
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
        }
    }

    private string Label(string property) => FieldLabel?.Invoke(property) ?? DefaultLabel(L, property);

    internal static string DefaultLabel(CoworkeeLocalizer l, string property) =>
        l[PropertyLabels.Humanize(property.Length > 2 && property.EndsWith("Id", StringComparison.Ordinal) ? property[..^2] : property)];

    private static Color ActionColor(string action) => action switch
    {
        "Deleted" => Color.Error,
        "Restored" => Color.Secondary,
        "Created" => Color.Success,
        _ => Color.Default,
    };

    private async Task RestoreAsync(EntityHistoryEntryDto entry, string? property)
    {
        if (await EntityHistoryRestoreDialog.ConfirmAsync(Dialogs, Api, Snackbar, L, EntitySet, Id, new EntityHistoryRestoreRequest(entry.Id, property), Label))
        {
            await ReloadAsync();
            await OnRestored.InvokeAsync();
        }
    }
}

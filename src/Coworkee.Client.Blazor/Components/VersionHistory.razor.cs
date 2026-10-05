using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public partial class VersionHistory
{
    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private static readonly System.Text.Json.JsonSerializerOptions Indented = new() { WriteIndented = true };
    private IReadOnlyList<EntityVersionDto>? _versions;
    private EntityVersionDetailDto? _detail;

    [Parameter, EditorRequired] public string Type { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid Id { get; set; }

    [Parameter] public EventCallback OnRestored { get; set; }

    protected override async Task OnParametersSetAsync() => await ReloadAsync();

    public async Task ReloadAsync() => _versions = await Api.GetVersionsAsync(Type, Id);

    private async Task ShowAsync(int revision) => _detail = await Api.GetVersionAsync(Type, Id, revision);

    private async Task RestoreAsync(int revision)
    {
        try
        {
            await Api.RestoreVersionAsync(Type, Id, revision);
            Snackbar.Add($"Version {revision} restored.", Severity.Success);
            await OnRestored.InvokeAsync();
            await ReloadAsync();
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Message, Severity.Error);
        }
    }
}

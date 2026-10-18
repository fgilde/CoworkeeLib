using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class SystemStatus
{
    private static readonly Dictionary<string, string> SectionIcons = new(StringComparer.Ordinal)
    {
        ["versions"] = MudBlazor.Icons.Material.Outlined.Info,
        ["database"] = MudBlazor.Icons.Material.Outlined.Storage,
        ["jobs"] = MudBlazor.Icons.Material.Outlined.Schedule,
        ["mail"] = MudBlazor.Icons.Material.Outlined.Email,
        ["realtime"] = MudBlazor.Icons.Material.Outlined.Bolt,
        ["cache"] = MudBlazor.Icons.Material.Outlined.Memory,
        ["files"] = MudBlazor.Icons.Material.Outlined.FolderOpen,
        ["translation"] = MudBlazor.Icons.Material.Outlined.Translate,
        ["ai"] = MudBlazor.Icons.Material.Outlined.AutoAwesome,
        ["retention"] = MudBlazor.Icons.Material.Outlined.AutoDelete,
    };

    private SystemStatusDto? _status;
    private string? _error;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override Task OnInitializedAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        _busy = true;
        try
        {
            _status = await Api.GetSystemStatusAsync();
            _error = null;
        }
        catch (ApiException exception)
        {
            _error = exception.Status == 403 ? L["The status of the installation is shown in the system organisation."] : exception.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private string? LinkOf(SystemStatusSection section) => section.Link is null ? null : section.ExternalLink ? section.Link : Links.Href(section.Link);

    private static string IconOf(string key) => SectionIcons.GetValueOrDefault(key, MudBlazor.Icons.Material.Outlined.MonitorHeart);

    private static Color ColorOf(SystemStatusLevel level) => level switch
    {
        SystemStatusLevel.Ok => Color.Success,
        SystemStatusLevel.Warning => Color.Warning,
        SystemStatusLevel.Error => Color.Error,
        _ => Color.Default,
    };

    private static Color WorstColor(SystemStatusSection section) =>
        ColorOf(section.Entries.Select(e => e.Level).DefaultIfEmpty(SystemStatusLevel.Info).Max());
}

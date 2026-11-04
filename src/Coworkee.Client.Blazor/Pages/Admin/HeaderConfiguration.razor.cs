using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The app bar of the installation (<see cref="HeaderSettings.Items"/>) and where the assistant shows (<see cref="AiSettingNames.Display"/>), saved as system wide settings.</summary>
public partial class HeaderConfiguration
{
    private static readonly (string Display, string Label, string Icon)[] Displays =
    [
        (AssistantDisplay.BottomRight, "Floating, bottom right", Icons.Material.Outlined.SouthEast),
        (AssistantDisplay.BottomLeft, "Floating, bottom left", Icons.Material.Outlined.SouthWest),
        (AssistantDisplay.AppBar, "App bar button", Icons.Material.Outlined.WebAsset),
        (AssistantDisplay.Navigation, "Navigation entry", Icons.Material.Outlined.Menu),
        (AssistantDisplay.Off, "Only Ctrl+I", Icons.Material.Outlined.Keyboard),
    ];

    private static readonly (HeaderPlacement Zone, string Label, string Icon)[] Zones =
    [
        (HeaderPlacement.Bar, "In the bar", Icons.Material.Outlined.WebAsset),
        (HeaderPlacement.Overflow, "In the overflow menu", Icons.Material.Filled.MoreVert),
        (HeaderPlacement.Hidden, "Hidden", Icons.Material.Outlined.VisibilityOff),
    ];

    // kept grouped by placement, so the list order is the order within each zone
    private List<(HeaderItem Item, HeaderPlacement Placement)>? _items;
    private readonly Dictionary<string, (IReadOnlyList<string> Permissions, PermissionMatch Match)> _access = new(StringComparer.Ordinal);
    private MudDropContainer<HeaderItem>? _drop;
    private bool _reorder;
    private string _display = AssistantDisplay.BottomRight;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private IReadOnlyList<HeaderItem> Available => HeaderItems.All(Contributors);

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task LoadAsync() => Snackbar.RunAsync(async () =>
    {
        var values = (await Api.GetSettingsAsync(SettingScope.Global)).ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        var configured = HeaderItems.Parse(values.GetValueOrDefault(HeaderSettings.Items));
        _access.Clear();
        foreach (var setting in configured.Where(c => c.Id != HeaderItems.User && c.Permissions is { Count: > 0 }))
        {
            _access.TryAdd(setting.Id, (setting.Permissions!, setting.Match));
        }

        Show(HeaderItems.Arrange(Available, configured));
        _display = values.GetValueOrDefault(AiSettingNames.Display) is { } display && AssistantDisplay.All.Contains(display) ? display : AssistantDisplay.BottomRight;
    });

    private void Reset()
    {
        _access.Clear();
        Show(HeaderItems.Arrange(Available, []));
    }

    private void Show(IEnumerable<(HeaderItem Item, HeaderPlacement Placement)> items)
    {
        _items = [.. items.OrderBy(i => i.Placement)];
        _reorder = true;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // the zones remember the order they saw first; once the container has the new list they forget it and follow the list
        if (_reorder && _drop is not null)
        {
            _reorder = false;
            _drop.Refresh();
        }
    }

    private HeaderPlacement PlacementOf(HeaderItem item) => _items!.Find(i => i.Item.Id == item.Id).Placement;

    private bool CanMove(int index, int by) => index + by >= 0 && index + by < _items!.Count && _items[index + by].Placement == _items[index].Placement;

    private void Move(int index, int by)
    {
        (_items![index], _items[index + by]) = (_items[index + by], _items[index]);
        Show(_items);
    }

    private void Place(int index, HeaderPlacement placement)
    {
        _items![index] = (_items[index].Item, placement);
        Show(_items);
    }

    private void Dropped(MudItemDropInfo<HeaderItem> drop)
    {
        var placement = Enum.Parse<HeaderPlacement>(drop.DropzoneIdentifier);
        if (drop.Item is not { } item || (item.Id == HeaderItems.User && placement != HeaderPlacement.Bar))
        {
            Show(_items!);
            return;
        }

        var rest = _items!.Where(i => i.Item.Id != item.Id).ToList();
        rest.Insert(rest.Count(i => i.Placement < placement) + Math.Clamp(drop.IndexInZone, 0, rest.Count(i => i.Placement == placement)), (item, placement));
        Show(rest);
    }

    private (IReadOnlyList<string> Permissions, PermissionMatch Match) Access(string id) =>
        _access.TryGetValue(id, out var access) ? access : ([], PermissionMatch.Any);

    private async Task EditAccessAsync(HeaderItem item)
    {
        var access = Access(item.Id);
        if (await HeaderAccessDialog.EditAsync(Dialogs, L["Visible for {0}", L[item.Title]], access.Permissions, access.Match) is not { } chosen)
        {
            return;
        }

        if (chosen.Permissions.Count == 0)
        {
            _access.Remove(item.Id);
        }
        else
        {
            _access[item.Id] = chosen;
        }

        // the drop zones render their items once; refreshing them shows the lock at once
        Show(_items!);
    }

    private static Color Selected(HeaderPlacement placement, HeaderPlacement button) => placement == button ? Color.Primary : Color.Default;

    private static string Pressed(HeaderPlacement placement, HeaderPlacement button) => placement == button ? "true" : "false";

    private async Task SaveAsync()
    {
        _busy = true;
        var defaults = HeaderItems.Arrange(Available, []).Select(i => (i.Item.Id, i.Placement));
        // the default header is saved as none, so items added later keep finding their default place
        var header = _access.Count == 0 && _items!.Select(i => (i.Item.Id, i.Placement)).SequenceEqual(defaults)
            ? null
            : HeaderItems.Serialize(_items!.Select(i => _access.TryGetValue(i.Item.Id, out var access)
                ? new HeaderItemSetting(i.Item.Id, i.Placement, access.Permissions, access.Match)
                : new HeaderItemSetting(i.Item.Id, i.Placement)));
        await Snackbar.RunAsync(
            () => Api.SetSettingsAsync(SettingScope.Global, new Dictionary<string, string?>(StringComparer.Ordinal) { [HeaderSettings.Items] = header, [AiSettingNames.Display] = _display }),
            L["Saved"]);
        _busy = false;
    }
}

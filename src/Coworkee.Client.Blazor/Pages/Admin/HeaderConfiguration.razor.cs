using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Client.Blazor.Security;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The app bars of the installation: their edges (<see cref="HeaderSettings.Position"/>, <see cref="HeaderSettings.SecondPosition"/>), their items
/// (<see cref="HeaderSettings.Items"/>) and where the assistant shows (<see cref="AiSettingNames.Display"/>), saved as system wide settings.
/// </summary>
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

    private static readonly (HeaderPosition Position, string Label, string Icon)[] Positions =
    [
        (HeaderPosition.Top, "Top edge", Icons.Material.Outlined.BorderTop),
        (HeaderPosition.Right, "Right edge", Icons.Material.Outlined.BorderRight),
        (HeaderPosition.Bottom, "Bottom edge", Icons.Material.Outlined.BorderBottom),
        (HeaderPosition.Left, "Left edge", Icons.Material.Outlined.BorderLeft),
    ];

    // kept grouped by placement, so the list order is the order within each zone
    private List<(HeaderItem Item, HeaderPlacement Placement)>? _items;
    private readonly Dictionary<string, (IReadOnlyList<string> Permissions, PermissionMatch Match)> _access = new(StringComparer.Ordinal);
    private MudDropContainer<HeaderItem>? _drop;
    private bool _reorder;
    private HeaderPosition _position = HeaderPosition.Top;
    private HeaderPosition? _second;
    private string _display = AssistantDisplay.BottomRight;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private IReadOnlyList<HeaderItem> Available => HeaderItems.All(Contributors);

    // the second bar's areas only while there is one
    private IEnumerable<(HeaderPlacement Zone, string Label, string Icon)> Zones
    {
        get
        {
            yield return (HeaderPlacement.Bar, "App bar", Icons.Material.Outlined.WebAsset);
            yield return (HeaderPlacement.Overflow, "More menu of the app bar", Icons.Material.Filled.MoreVert);
            if (_second is not null)
            {
                yield return (HeaderPlacement.SecondBar, "Second app bar", Icons.Material.Outlined.ViewSidebar);
                yield return (HeaderPlacement.SecondOverflow, "More menu of the second bar", Icons.Material.Filled.MoreVert);
            }

            yield return (HeaderPlacement.Hidden, "Hidden", Icons.Material.Outlined.VisibilityOff);
        }
    }

    private IEnumerable<(int Bar, HeaderPosition Position)> Bars => _second is { } second ? [(0, _position), (1, second)] : [(0, _position)];

    private string PreviewPageStyle
    {
        get
        {
            string Edge(HeaderPosition position) => _position == position || _second == position ? "26px" : "0";
            return $"top:{Edge(HeaderPosition.Top)};bottom:{Edge(HeaderPosition.Bottom)};left:{Edge(HeaderPosition.Left)};right:{Edge(HeaderPosition.Right)}";
        }
    }

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task LoadAsync() => Snackbar.RunAsync(async () =>
    {
        var values = (await Api.GetSettingsAsync(SettingScope.Global)).ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        var layout = HeaderLayout.From(values);
        _position = layout.Position;
        _second = layout.Second;
        var configured = HeaderItems.Parse(values.GetValueOrDefault(HeaderSettings.Items));
        _access.Clear();
        foreach (var setting in configured.Where(c => c.Id != HeaderItems.User && c.Permissions is { Count: > 0 }))
        {
            _access.TryAdd(setting.Id, (setting.Permissions!, setting.Match));
        }

        Show(HeaderItems.Arrange(Available, configured).Select(i => (i.Item, layout.Effective(i.Placement))));
        _display = values.GetValueOrDefault(AiSettingNames.Display) is { } display && AssistantDisplay.All.Contains(display) ? display : AssistantDisplay.BottomRight;
    });

    private void Reset()
    {
        _access.Clear();
        _position = HeaderPosition.Top;
        _second = null;
        Show(HeaderItems.Arrange(Available, []));
    }

    private void SetPosition(HeaderPosition position)
    {
        // the second bar gives way to the first
        if (_second == position)
        {
            _second = Positions.Select(p => p.Position).First(p => p != position);
        }

        _position = position;
    }

    private void SetSecond(HeaderPosition position) => _second = position;

    private void ToggleSecond(bool on)
    {
        if (on)
        {
            _second = _position == HeaderPosition.Right ? HeaderPosition.Left : HeaderPosition.Right;
            return;
        }

        // without the second bar its items are the first bar's
        _second = null;
        Show(_items!.Select(i => (i.Item, HeaderLayout.Default.Effective(i.Placement))));
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

    private IEnumerable<HeaderItem> InZone(HeaderPlacement placement) => _items!.Where(i => i.Placement == placement).Select(i => i.Item);

    private HeaderPlacement PlacementOf(HeaderItem item) => _items!.Find(i => i.Item.Id == item.Id).Placement;

    // the account menu stays in a bar: without it nobody signs out; a spacer is removed instead of hidden
    private static bool CanPlace(HeaderItem item, HeaderPlacement placement) =>
        (item.Id != HeaderItems.User || placement is HeaderPlacement.Bar or HeaderPlacement.SecondBar)
        && !(HeaderItems.IsSpacer(item.Id) && placement == HeaderPlacement.Hidden);

    // a new spacer starts the first bar: what follows it moves to the far end; dragged on, it splits wherever it lands
    private void AddSpacer()
    {
        var items = _items!;
        var spacer = HeaderItems.SpacerItem(HeaderItems.NewSpacerId(items.Select(i => i.Item.Id)));
        items.Insert(items.Count(i => i.Placement < HeaderPlacement.Bar), (spacer, HeaderPlacement.Bar));
        Show(items);
    }

    private void Remove(int index)
    {
        _items!.RemoveAt(index);
        Show(_items);
    }

    private static Color ZoneColor(HeaderPlacement placement) => placement switch
    {
        HeaderPlacement.Bar or HeaderPlacement.Overflow => Color.Primary,
        HeaderPlacement.SecondBar or HeaderPlacement.SecondOverflow => Color.Secondary,
        _ => Color.Default,
    };

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
        if (drop.Item is not { } item || !CanPlace(item, placement))
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
            () => Api.SetSettingsAsync(SettingScope.Global, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [HeaderSettings.Items] = header,
                [HeaderSettings.Position] = _position == HeaderPosition.Top ? null : _position.ToString(),
                [HeaderSettings.SecondPosition] = _second?.ToString(),
                [AiSettingNames.Display] = _display,
            }),
            L["Saved"]);
        _busy = false;
    }
}

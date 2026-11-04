using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Navigation;
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

    private List<(HeaderItem Item, HeaderPlacement Placement)>? _items;
    private string _display = AssistantDisplay.BottomRight;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private IEnumerable<IAppBarContributor> Contributors { get; set; } = null!;

    [Inject] private CoworkeePages Links { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private IReadOnlyList<HeaderItem> Available => HeaderItems.All(Contributors);

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task LoadAsync() => Snackbar.RunAsync(async () =>
    {
        var values = (await Api.GetSettingsAsync(SettingScope.Global)).ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        _items = [.. HeaderItems.Arrange(Available, HeaderItems.Parse(values.GetValueOrDefault(HeaderSettings.Items)))];
        _display = values.GetValueOrDefault(AiSettingNames.Display) is { } display && AssistantDisplay.All.Contains(display) ? display : AssistantDisplay.BottomRight;
    });

    private void Reset() => _items = [.. HeaderItems.Arrange(Available, [])];

    private void Move(int index, int by) => (_items![index], _items[index + by]) = (_items[index + by], _items[index]);

    private void Place(int index, HeaderPlacement placement) => _items![index] = (_items[index].Item, placement);

    private static Color Selected(HeaderPlacement placement, HeaderPlacement button) => placement == button ? Color.Primary : Color.Default;

    private static string Pressed(HeaderPlacement placement, HeaderPlacement button) => placement == button ? "true" : "false";

    private async Task SaveAsync()
    {
        _busy = true;
        var defaults = HeaderItems.Arrange(Available, []).Select(i => (i.Item.Id, i.Placement));
        // the default header is saved as none, so items added later keep finding their default place
        var header = _items!.Select(i => (i.Item.Id, i.Placement)).SequenceEqual(defaults) ? null : HeaderItems.Serialize(_items!.Select(i => new HeaderItemSetting(i.Item.Id, i.Placement)));
        await Snackbar.RunAsync(
            () => Api.SetSettingsAsync(SettingScope.Global, new Dictionary<string, string?>(StringComparer.Ordinal) { [HeaderSettings.Items] = header, [AiSettingNames.Display] = _display }),
            L["Saved"]);
        _busy = false;
    }
}

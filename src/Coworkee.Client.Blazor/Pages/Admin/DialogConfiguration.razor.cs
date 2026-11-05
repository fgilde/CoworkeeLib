using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Dialogs;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Extensions;
using MudBlazor.Extensions.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>
/// The options of each <see cref="DialogPart"/>, saved as <see cref="DialogSettings.Options"/> (system wide); only the parts that differ from the app's
/// <see cref="DialogOptionsContributor"/> are saved. Listed only when the contributor allows it (<see cref="DialogOptionsContributor.AdminEditable"/>).
/// </summary>
public partial class DialogConfiguration
{
    private static readonly (DialogPart Part, string Label, string Description)[] Parts =
    [
        (DialogPart.SideSheet, "Side sheets", "Edit forms and detail sheets at full height. They open on the side the user clicked on, whatever position is set here."),
        (DialogPart.RightSheet, "Right sheets", "Narrow sheets at full height on the right, like the about dialog and version histories."),
        (DialogPart.Small, "Small dialogs", "Small centered dialogs such as confirmations."),
        (DialogPart.Fullscreen, "Full screen", "Editors over the whole screen, like the code editor."),
        (DialogPart.Progress, "Progress", "Progress that cannot be closed until it ends, like an export."),
        (DialogPart.ListItem, "List items", "An item of a list edited in a settings form."),
    ];

    private Dictionary<DialogPart, DialogOptionsEx>? _parts;
    private int _tab;
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeDialogOptions DialogOptions { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    /// <summary>The navigation entry shows only when the app lets administrators change its dialogs.</summary>
    public static Task<bool> VisibleAsync(IServiceProvider services) =>
        Task.FromResult(services.GetService<DialogOptionsContributor>()?.AdminEditable == true);

    protected override Task OnInitializedAsync() => DialogOptions.Contributor.AdminEditable ? LoadAsync() : Task.CompletedTask;

    private Task LoadAsync() => Snackbar.RunAsync(async () =>
    {
        var value = (await Api.GetSettingsAsync(SettingScope.Global)).FirstOrDefault(v => v.Name == DialogSettings.Options)?.Value;
        var saved = CoworkeeDialogOptions.Parse(value);
        _parts = Parts.ToDictionary(p => p.Part, p => saved.GetValueOrDefault(p.Part) ?? Default(p.Part));
    });

    private DialogOptionsEx Default(DialogPart part) => DialogOptions.Contributor.Of(part);

    private bool Changed(DialogPart part) => !CoworkeeDialogOptions.Same(_parts![part], Default(part));

    private void Reset(DialogPart part) => _parts![part] = Default(part);

    private async Task PreviewAsync(DialogPart part, string label) =>
        await Dialogs.ShowInformationAsync(L[label], L["This is how the dialog opens."], Icons.Material.Outlined.Info, _parts![part].CloneOptions());

    private async Task SaveAsync()
    {
        _busy = true;
        var changed = _parts!.Where(p => Changed(p.Key)).ToDictionary(p => p.Key, p => p.Value);
        await Snackbar.RunAsync(
            () => Api.SetSettingsAsync(SettingScope.Global, new Dictionary<string, string?>(StringComparer.Ordinal) { [DialogSettings.Options] = CoworkeeDialogOptions.Serialize(changed) }),
            L["Saved"]);
        _busy = false;
    }
}

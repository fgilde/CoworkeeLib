using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Files;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Files;

/// <summary>
/// A stored file chosen in a side sheet with the file manager: <c>Value</c> for one, <c>Values</c> with <c>Multiple</c> for several.
/// As editor: <c>RenderWith&lt;CoworkeeFilePicker, Guid?&gt;(p =&gt; p.Value)</c>.
/// </summary>
public partial class CoworkeeFilePicker
{
    private readonly Dictionary<Guid, string> _names = [];

    [Inject] private IFilesApi Api { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public Guid? Value { get; set; }

    [Parameter] public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter] public IReadOnlyList<Guid>? Values { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<Guid>> ValuesChanged { get; set; }

    [Parameter] public bool Multiple { get; set; }

    /// <summary>An HTML accept list such as "image/*,.pdf"; other files are not offered.</summary>
    [Parameter] public string? Accept { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Disabled { get; set; }

    private IReadOnlyList<Guid> Selected => Multiple ? Values ?? [] : Value is { } id ? [id] : [];

    protected override async Task OnParametersSetAsync()
    {
        foreach (var id in Selected.Where(id => !_names.ContainsKey(id)).ToList())
        {
            try
            {
                _names[id] = (await Api.GetFileAsync(id)).Name;
            }
            catch (ApiException)
            {
                _names[id] = L["Unavailable file"];
            }
        }
    }

    private string NameOf(Guid id) => _names.GetValueOrDefault(id) ?? "…";

    private async Task ChooseAsync()
    {
        if (await FilePickerDialog.ShowAsync(Dialogs, Multiple, Accept) is not { Count: > 0 } files)
        {
            return;
        }

        foreach (var file in files)
        {
            _names[file.Id] = file.Name;
        }

        await SetAsync(Multiple ? Selected.Concat(files.Select(f => f.Id)).Distinct().ToList() : [files[0].Id]);
    }

    private Task RemoveAsync(Guid id) => SetAsync(Selected.Where(s => s != id).ToList());

    private async Task SetAsync(IReadOnlyList<Guid> ids)
    {
        if (Multiple)
        {
            Values = ids;
            await ValuesChanged.InvokeAsync(ids);
            return;
        }

        Value = ids.Count > 0 ? ids[0] : null;
        await ValueChanged.InvokeAsync(Value);
    }
}

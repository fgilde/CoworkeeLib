using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>Saves, applies, shares and resets named views of a table.</summary>
public partial class DataTableViewMenu
{
    private IReadOnlyList<SavedTableView> _views = [];
    private string? _name;
    private bool _default;

    [Inject] private Localization.CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private ITableViewStore Store { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    /// <summary>Key the views are kept under.</summary>
    [Parameter, EditorRequired] public string Table { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Func<DataTableState> Current { get; set; } = null!;

    [Parameter] public EventCallback<DataTableState> OnApply { get; set; }

    [Parameter] public EventCallback OnReset { get; set; }

    /// <summary>Address that opens the table as it is now.</summary>
    [Parameter, EditorRequired] public Func<string> Link { get; set; } = null!;

    /// <summary>Name of the view the table shows.</summary>
    [Parameter] public string? Active { get; set; }

    [Parameter] public EventCallback<string?> ActiveChanged { get; set; }

    protected override async Task OnParametersSetAsync() => _views = await Store.GetAsync(Table);

    private async Task ApplyAsync(SavedTableView view)
    {
        await ActiveChanged.InvokeAsync(view.Name);
        await OnApply.InvokeAsync(view.State);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_name))
        {
            return;
        }

        var name = _name.Trim();
        _views = await Store.SaveAsync(Table, new SavedTableView(name, Current()) { IsDefault = _default });
        await ActiveChanged.InvokeAsync(name);
        Snackbar.Add(L["View saved"], Severity.Success);
        (_name, _default) = (null, false);
    }

    private async Task OverwriteAsync(SavedTableView view)
    {
        _views = await Store.SaveAsync(Table, view with { State = Current() });
        Snackbar.Add(L["View saved"], Severity.Success);
    }

    private async Task ToggleDefaultAsync(SavedTableView view) => _views = await Store.SaveAsync(Table, view with { IsDefault = !view.IsDefault });

    private async Task DeleteAsync(SavedTableView view)
    {
        _views = await Store.DeleteAsync(Table, view.Name);
        if (view.Name == Active)
        {
            await ActiveChanged.InvokeAsync(null);
        }
    }

    private async Task ShareAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", Link());
            Snackbar.Add(L["Link copied"], Severity.Success);
        }
        catch (JSException)
        {
            Snackbar.Add(L["The link could not be copied."], Severity.Warning);
        }
    }

    private async Task ResetAsync()
    {
        await ActiveChanged.InvokeAsync(null);
        await OnReset.InvokeAsync();
    }
}

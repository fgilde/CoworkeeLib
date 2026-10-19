using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    /// <summary>Where a row leads: a click on the row (not on its buttons or checkbox) navigates there; the row's open link reaches it by keyboard.</summary>
    [Parameter] public Func<T, string?>? RowHref { get; set; }

    /// <summary>Called for a click on a row (not on its buttons or checkbox) and for its open button; rows with a <see cref="RowHref"/> navigate instead.</summary>
    [Parameter] public EventCallback<T> OnRowClick { get; set; }

    private bool RowOpens => RowHref is not null || OnRowClick.HasDelegate;

    private bool HasRowActions => RowOpens || OnEdit.HasDelegate || OnDelete is not null || History;

    private string? RowClass(T item, int index) => OnRowClick.HasDelegate || RowHref?.Invoke(item) is not null ? "cw-clickable" : null;

    private Task RowClickedAsync(DataGridRowClickEventArgs<T> args) => OpenRowAsync(args.Item);

    private Task OpenRowAsync(T item)
    {
        if (RowHref?.Invoke(item) is { } href)
        {
            Nav.NavigateTo(href);
            return Task.CompletedTask;
        }

        return OnRowClick.InvokeAsync(item);
    }
}

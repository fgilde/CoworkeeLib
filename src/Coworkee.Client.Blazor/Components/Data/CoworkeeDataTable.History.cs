using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

public partial class CoworkeeDataTable<T>
{
    /// <summary>A history action per row and the deleted records in the toolbar, for users with Audit.View; the server opts the set in with AddEntityHistory.</summary>
    [Parameter] public bool History { get; set; }

    /// <summary>The record id the history asks for; defaults to the row's Id property.</summary>
    [Parameter] public Func<T, Guid>? ItemId { get; set; }

    private Guid IdOf(T item) => ItemId?.Invoke(item) ?? (Guid)typeof(T).GetProperty("Id")!.GetValue(item)!;

    private Task ShowHistoryAsync(T item) =>
        EntityHistoryDialog.ShowAsync(Dialogs, DescribeItem?.Invoke(item) ?? L["History"], EntitySet, IdOf(item), EventCallback.Factory.Create(this, ReloadAsync));

    private Task ShowDeletedAsync() =>
        EntityHistoryDialog.ShowAsync(Dialogs, L["Deleted records"], EntitySet, null, EventCallback.Factory.Create(this, ReloadAsync));
}

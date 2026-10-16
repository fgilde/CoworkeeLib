using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>
/// The workflow state of the rows in a <see cref="CoworkeeDataTable{T}"/>: the state chip with the actions menu, sorting and grouping on the server's
/// virtual property <c>WorkflowState</c> and a column filter with the states of the workflow (its facet), counts and "Awaiting my approval" included.
/// Rows from a local store (client entities) do not know <c>WorkflowState</c>: mirror the state into a property (<c>MirrorTo</c>) and name it in
/// <see cref="MirrorProperty"/>; the column then filters, sorts and groups on that property and its facet.
/// </summary>
public sealed class CoworkeeWorkflowColumn<T> : Column<T>, IPropertyLikeColumn
{
    public const string StateProperty = "WorkflowState";

    private static readonly System.Reflection.PropertyInfo? IdProperty = typeof(T).GetProperty("Id") is { PropertyType: var type } id && type == typeof(Guid) ? id : null;

    private readonly Dictionary<Guid, string> _states = [];
    private bool _regroup;

    // the grid reads GroupBy once, before the first OnParametersSet
    public CoworkeeWorkflowColumn() => GroupBy = row => StateOf(row) ?? string.Empty;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    /// <summary>The workflow's entity type, e.g. <c>Products</c>.</summary>
    [Parameter, EditorRequired] public string EntityType { get; set; } = string.Empty;

    /// <summary>The id of a row; defaults to its <c>Id</c> property.</summary>
    [Parameter] public Func<T, Guid>? EntityId { get; set; }

    /// <summary>Reloads the table after a transition or decision, so filters, sorting and facet counts follow; on by default.</summary>
    [Parameter] public bool ReloadOnChange { get; set; } = true;

    [Parameter] public EventCallback<WorkflowStatusDto> OnChanged { get; set; }

    /// <summary>The string property of the row the workflow mirrors its state into (<c>MirrorTo</c>), e.g. <c>ApprovalState</c>; its facet is the column filter.</summary>
    [Parameter] public string? MirrorProperty { get; set; }

    public override string? PropertyName => MirrorProperty ?? StateProperty;

    Type IPropertyLikeColumn.ValueType => typeof(string);

    protected override Type? PropertyType => typeof(string);

    public override RenderFragment<CellContext<T>>? GetCellTemplate() => CellTemplate ?? Cell;

    protected override void OnParametersSet()
    {
#pragma warning disable BL0005 // defaults only where the page left the parameter unset
        Title ??= L["Workflow state"];
#pragma warning restore BL0005
        base.OnParametersSet();
    }

    protected override object? CellContent(T item) => StateOf(item);

    protected override object? PropertyFunc(T item) => StateOf(item);

    protected override void SetProperty(object? item, object? value)
    {
    }

    private Guid IdOf(T row) => EntityId?.Invoke(row)
        ?? (Guid)(IdProperty ?? throw new InvalidOperationException($"{typeof(T).Name} has no Guid Id; set EntityId.")).GetValue(row)!;

    private string? StateOf(T row) => row is null ? null
        : _states.GetValueOrDefault(IdOf(row)) ?? (MirrorProperty is null ? null : typeof(T).GetProperty(MirrorProperty)?.GetValue(row) as string);

    private RenderFragment<CellContext<T>> Cell => context => builder =>
    {
        builder.OpenComponent<CoworkeeWorkflowActions>(0);
        builder.AddComponentParameter(1, nameof(CoworkeeWorkflowActions.EntityType), EntityType);
        builder.AddComponentParameter(2, nameof(CoworkeeWorkflowActions.EntityId), IdOf(context.Item));
        builder.AddComponentParameter(3, nameof(CoworkeeWorkflowActions.StatusRead), (Action<WorkflowStatusDto>)Remember);
        builder.AddComponentParameter(4, nameof(CoworkeeWorkflowActions.OnChanged), EventCallback.Factory.Create<WorkflowStatusDto>(this, ChangedAsync));
        builder.CloseComponent();
    };

    // the states arrive after the rows: groups are built again once, after the cells of a page reported theirs
    private void Remember(WorkflowStatusDto status)
    {
        if (_states.GetValueOrDefault(status.EntityId) == status.State)
        {
            return;
        }

        _states[status.EntityId] = status.State;
        if (DataGrid?.IsGrouped == true && !_regroup)
        {
            _regroup = true;
            _ = InvokeAsync(() =>
            {
                _regroup = false;
                DataGrid.GroupItems();
            });
        }
    }

    private async Task ChangedAsync(WorkflowStatusDto status)
    {
        await OnChanged.InvokeAsync(status);
        if (ReloadOnChange && DataGrid is not null)
        {
            await DataGrid.ReloadServerData();
        }
    }
}

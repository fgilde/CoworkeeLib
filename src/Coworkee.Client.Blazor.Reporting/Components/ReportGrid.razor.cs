using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Widgets on a GridStack grid: Blazor renders the items, GridStack moves and resizes them and reports the layout back.</summary>
public partial class ReportGrid : IAsyncDisposable
{
    public const string Module = "./_content/Coworkee.Client.Blazor.Reporting/reporting.js";
    public const int CellHeight = 72;

    private ElementReference _element;
    private IJSObjectReference? _module;
    private DotNetObjectReference<ReportGrid>? _self;
    private bool _editable;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired] public List<ReportWidget> Widgets { get; set; } = [];

    [Parameter] public bool Editable { get; set; }

    [Parameter, EditorRequired] public RenderFragment<ReportWidget> ItemTemplate { get; set; } = null!;

    /// <summary>After widgets were moved or resized; their positions are already updated.</summary>
    [Parameter] public EventCallback LayoutChanged { get; set; }

    public ElementReference Element => _element;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>("import", Module);
            await _module.InvokeVoidAsync("init", _element, _self, Editable, ReportDefinition.GridColumns, CellHeight);
            _editable = Editable;
            return;
        }

        if (_module is null)
        {
            return;
        }

        await _module.InvokeVoidAsync("sync", _element);
        if (_editable != Editable)
        {
            _editable = Editable;
            await _module.InvokeVoidAsync("setEditable", _element, Editable);
        }
    }

    [JSInvokable]
    public async Task OnLayoutChanged(GridItem[] items)
    {
        var changed = false;
        foreach (var item in items)
        {
            if (Widgets.FirstOrDefault(w => w.Id == item.Id) is { } widget && (widget.X, widget.Y, widget.W, widget.H) != (item.X, item.Y, item.W, item.H))
            {
                (widget.X, widget.Y, widget.W, widget.H) = (item.X, item.Y, item.W, item.H);
                changed = true;
            }
        }

        if (changed)
        {
            await LayoutChanged.InvokeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose", _element);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _self?.Dispose();
        GC.SuppressFinalize(this);
    }

    public sealed record GridItem(string Id, int X, int Y, int W, int H);
}

using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Theming;

/// <summary>
/// Edits a stylesheet as a list of rules (selector plus a style editor) or as raw CSS in Monaco. Whatever is not a plain rule
/// (at-rules, comments, nested CSS) stays a raw block. The chosen mode is remembered per browser.
/// </summary>
public partial class CssRulesEditor
{
    private const string ModeKey = "coworkee.cssEditorMode";

    private List<CssBlock> _blocks = [];
    private string? _value;
    private bool _raw;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    protected override void OnInitialized() => _blocks = [.. CssStylesheet.Parse(Value)];

    protected override void OnParametersSet()
    {
        if (Value != _value)
        {
            _value = Value;
            _blocks = [.. CssStylesheet.Parse(Value)];
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            if (await JS.InvokeAsync<string?>("localStorage.getItem", ModeKey) == "raw")
            {
                _raw = true;
                StateHasChanged();
            }
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or InvalidOperationException)
        {
        }
    }

    private async Task SetModeAsync(bool raw)
    {
        _raw = raw;
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", ModeKey, raw ? "raw" : "rules");
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or InvalidOperationException)
        {
        }
    }

    private Task RawChangedAsync(string? css)
    {
        _blocks = [.. CssStylesheet.Parse(css)];
        return EmitAsync(css);
    }

    private Task AddAsync()
    {
        _blocks.Add(new CssRule(string.Empty, string.Empty));
        return Task.CompletedTask;
    }

    private Task SetBlockAsync(int index, CssBlock block)
    {
        if (_blocks[index] == block)
        {
            return Task.CompletedTask;
        }

        _blocks[index] = block;
        return EmitBlocksAsync();
    }

    private Task DeleteAsync(int index)
    {
        _blocks.RemoveAt(index);
        return EmitBlocksAsync();
    }

    private Task MoveAsync(int index, int by)
    {
        (_blocks[index], _blocks[index + by]) = (_blocks[index + by], _blocks[index]);
        return EmitBlocksAsync();
    }

    // a rule without a selector (just added, still being typed) is not written yet
    private Task EmitBlocksAsync() => EmitAsync(CssStylesheet.Serialize(_blocks.Where(b => b switch
    {
        CssRule r => !string.IsNullOrWhiteSpace(r.Selector),
        CssRawBlock raw => !string.IsNullOrWhiteSpace(raw.Text),
        _ => false,
    })));

    private Task EmitAsync(string? css)
    {
        Value = _value = css;
        return ValueChanged.InvokeAsync(css);
    }
}

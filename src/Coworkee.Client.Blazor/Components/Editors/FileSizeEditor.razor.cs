using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>A size in bytes entered in KB, MB or GB; empty or 0 means no limit.</summary>
public partial class FileSizeEditor
{
    private static readonly (string Name, long Factor)[] Units = [("KB", 1L << 10), ("MB", 1L << 20), ("GB", 1L << 30)];

    private decimal? _amount;
    private long _unit = 1L << 20;
    private long? _shown;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public long? Value { get; set; }

    [Parameter] public EventCallback<long?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>The variant of both fields, the one the surrounding form uses (MudBlazor's default, like MudEx's fields).</summary>
    [Parameter] public Variant Variant { get; set; } = Variant.Text;

    /// <summary>A size in the largest unit that still shows at least 1, e.g. 10 MB, 512 KB.</summary>
    public static string Format(long bytes)
    {
        var (name, factor) = Units.LastOrDefault(u => bytes >= u.Factor, Units[0]);
        return $"{Math.Round((decimal)bytes / factor, 2).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)} {name}";
    }

    protected override void OnParametersSet()
    {
        if (Value == _shown)
        {
            return;
        }

        _shown = Value;
        if (Value is not > 0)
        {
            _amount = null;
            return;
        }

        // the largest unit that still shows at least 1, 5 MB rather than 5120 KB
        _unit = Units.Select(u => u.Factor).LastOrDefault(f => Value >= f, Units[0].Factor);
        _amount = Math.Round((decimal)Value.Value / _unit, 2);
    }

    private Task SetAmountAsync(decimal? amount)
    {
        _amount = amount;
        return EmitAsync();
    }

    private Task SetUnitAsync(long unit)
    {
        _unit = unit;
        return EmitAsync();
    }

    private async Task EmitAsync()
    {
        _shown = _amount is > 0 ? (long)Math.Round(_amount.Value * _unit) : null;
        Value = _shown;
        await ValueChanged.InvokeAsync(_shown);
    }
}

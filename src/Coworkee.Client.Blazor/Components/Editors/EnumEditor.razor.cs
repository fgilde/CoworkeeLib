using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// Picks an enum member by its localized label (see <see cref="EnumLabels"/>); a [Flags] enum takes several of its single-bit members, none meaning 0
/// (combined members such as <c>All = A | B</c> are not offered: choosing all their bits is the same).
/// </summary>
public partial class EnumEditor<TEnum> where TEnum : struct, Enum
{
    internal static readonly bool Flagged = typeof(TEnum).IsDefined(typeof(FlagsAttribute), false);

    internal static readonly TEnum[] Items = [.. Enum.GetValues<TEnum>().Where(v => !Flagged || (Bits(v) != 0 && (Bits(v) & (Bits(v) - 1)) == 0))];

    private readonly bool _flags = Flagged;
    private readonly TEnum[] _items = Items;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public TEnum Value { get; set; }

    [Parameter] public EventCallback<TEnum> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Disabled { get; set; }

    private IReadOnlyCollection<TEnum> Selected => [.. _items.Where(v => Value.HasFlag(v))];

    private static long Bits(TEnum value) => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    private string Text(TEnum value) => L[EnumLabels.Of(value)];

    private string FlagsText(IReadOnlyList<string> texts) => string.Join(", ", texts);

    private Task SetAsync(TEnum value)
    {
        Value = value;
        return ValueChanged.InvokeAsync(value);
    }

    private Task SetFlagsAsync(IEnumerable<TEnum>? values) =>
        SetAsync((TEnum)Enum.ToObject(typeof(TEnum), (values ?? []).Aggregate(0L, (all, v) => all | Bits(v))));
}

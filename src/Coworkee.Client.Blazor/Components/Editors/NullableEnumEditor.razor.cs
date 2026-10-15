using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>Picks a member of a nullable (not [Flags]) enum by its localized label; clearing it sets null.</summary>
public partial class NullableEnumEditor<TEnum> where TEnum : struct, Enum
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public TEnum? Value { get; set; }

    [Parameter] public EventCallback<TEnum?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public string? HelperText { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Disabled { get; set; }

    private string Text(TEnum? value) => value is { } member ? L[EnumLabels.Of(member)] : string.Empty;

    private Task SetAsync(TEnum? value)
    {
        Value = value;
        return ValueChanged.InvokeAsync(value);
    }
}

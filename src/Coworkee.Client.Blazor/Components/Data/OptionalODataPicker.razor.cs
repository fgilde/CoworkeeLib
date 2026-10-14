using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>An <see cref="ODataPicker"/> for optional foreign keys: clearing it gives null.</summary>
public partial class OptionalODataPicker
{
    [Parameter, EditorRequired] public string EntitySet { get; set; } = string.Empty;

    [Parameter] public string TextProperty { get; set; } = "Name";

    [Parameter] public Guid? Value { get; set; }

    [Parameter] public EventCallback<Guid?> ValueChanged { get; set; }

    [Parameter] public string? Label { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public Variant Variant { get; set; } = Variant.Text;

    [Parameter] public Margin? Margin { get; set; }

    private Task ChangeAsync(Guid id) => ValueChanged.InvokeAsync(id == Guid.Empty ? null : id);
}

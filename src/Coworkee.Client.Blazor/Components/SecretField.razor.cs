using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

/// <summary>A secret of a form: masked while a stored one applies, with replace and remove; never read back from the server.</summary>
public partial class SecretField
{
    private const string Mask = "••••••••••••";

    private Dictionary<string, object> _attributes = [];
    private bool _replacing;
    private bool _visible;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public string Label { get; set; } = null!;

    /// <summary>A stored secret applies: nothing typed and not removed.</summary>
    [Parameter] public bool Stored { get; set; }

    /// <summary>The secret typed to replace the stored one.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public EventCallback Removed { get; set; }

    [Parameter] public bool Required { get; set; }

    [Parameter] public string? Help { get; set; }

    /// <summary>Prefix of the data-testid of the input ("{TestId}") and of the stored view ("{TestId}-stored", "-replace", "-remove").</summary>
    [Parameter, EditorRequired] public string TestId { get; set; } = null!;

    protected override void OnParametersSet() =>
        _attributes = new() { ["autocomplete"] = "new-password", ["data-testid"] = TestId };

    private Task RemoveAsync() => Removed.InvokeAsync();
}

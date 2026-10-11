using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Query;

/// <summary>Moves a rule or group one place up or down among its siblings.</summary>
public partial class QueryMoveButtons
{
    [Parameter] public (int Index, int Count) Position { get; set; }

    [Parameter] public EventCallback<int> OnMove { get; set; }

    [Parameter] public Func<string, string>? Localize { get; set; }

    private string T(string text) => Localize?.Invoke(text) ?? text;
}

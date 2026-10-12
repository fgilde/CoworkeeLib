using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Ai;

/// <summary>The key of a connection: masked while stored, with replace and remove; never read back from the server.</summary>
public partial class AiKeyField
{
    private const string Mask = "••••••••••••";

    private static readonly Dictionary<string, object> KeyAttributes = new() { ["autocomplete"] = "new-password", ["data-testid"] = "ai-key" };

    private bool _replacing;
    private bool _visible;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public AiConnectionForm Form { get; set; } = null!;

    private string Help => Form switch
    {
        { RemoveKey: true } => L["The stored key is removed on save."],
        { DropsStoredKey: true } => L["The stored key belongs to the previous provider or address and is removed on save; enter the key for this one."],
        { Info.Key: Contracts.Ai.AiField.Optional } => L["Optional. Stored encrypted."],
        _ => L["Stored encrypted; it is never shown again."],
    };
}

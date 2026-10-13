using Coworkee.Client.Blazor.Ai;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Ai;

/// <summary>The key of an AI connection: none for local providers, else a <see cref="SecretField"/> that drops the key of another provider or address.</summary>
public partial class AiKeyField
{
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

using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components;

public partial class AuditChanges
{
    [Parameter, EditorRequired] public IReadOnlyList<AuditChangeDto> Changes { get; set; } = [];

    private static string Display(string? value)
    {
        if (value is null)
        {
            return "–";
        }

        try
        {
            var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value);
            return element.ValueKind == System.Text.Json.JsonValueKind.String ? element.GetString()! : element.GetRawText();
        }
        catch (System.Text.Json.JsonException)
        {
            return value;
        }
    }
}

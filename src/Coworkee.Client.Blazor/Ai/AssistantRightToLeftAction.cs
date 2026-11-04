using System.Text.Json;
using Coworkee.Client.Blazor.Layout;
using Coworkee.Client.Blazor.Localization;

namespace Coworkee.Client.Blazor.Ai;

internal sealed class AssistantRightToLeftAction(LayoutPreferences layout, CoworkeeLocalizer l) : IAssistantClientAction
{
    public string Name => "set_right_to_left";

    public string Description => "Turns the right to left layout (for Arabic, Hebrew...) on or off in this browser.";

    public JsonElement Parameters { get; } = AssistantArguments.Schema("""{"type":"object","properties":{"enabled":{"type":"boolean"}},"required":["enabled"]}""");

    public async Task<AssistantActionResult> RunAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("enabled", out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return new AssistantActionResult(false, l["Invalid arguments for {0}", Name]);
        }

        await layout.SetRightToLeftAsync(value.GetBoolean());
        return new AssistantActionResult(true, l[value.GetBoolean() ? "Switched to right to left" : "Switched to left to right"]);
    }
}

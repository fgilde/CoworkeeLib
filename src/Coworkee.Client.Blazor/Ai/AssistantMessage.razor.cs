using System.Text.Json;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Ai;

/// <summary>One message: the user's as plain text, the assistant's as sanitized Markdown with the tools it ran and those waiting for the user.</summary>
public partial class AssistantMessage
{
    [Parameter, EditorRequired] public AiMessageDto Message { get; set; } = null!;

    [Parameter] public bool Streaming { get; set; }

    /// <summary>The newest message while nothing runs: only then its tools can be confirmed.</summary>
    [Parameter] public bool Waiting { get; set; }

    [Parameter] public EventCallback<bool> OnDecide { get; set; }

    [Inject] private CoworkeeClientOptions Options { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private static string Humanize(string tool) => tool.Replace('_', ' ');

    /// <summary>The page of what a tool created or changed, from <see cref="CoworkeeClientOptions.AssistantLinks"/> and the id in its result.</summary>
    private string? LinkOf(AiToolCallDto call)
    {
        if (!call.Succeeded || !Options.AssistantLinks.TryGetValue(call.Tool, out var template) || IdOf(call.Output) is not { } id)
        {
            return null;
        }

        return template.Replace("{id}", Uri.EscapeDataString(id), StringComparison.Ordinal);
    }

    private static string? IdOf(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            var id = root.ValueKind == JsonValueKind.Object && (root.TryGetProperty("id", out var value) || root.TryGetProperty("Id", out value)) ? value : root;
            return id.ValueKind is JsonValueKind.String or JsonValueKind.Number && id.ToString() is { Length: > 0 and <= 64 } text ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

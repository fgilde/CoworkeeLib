namespace Coworkee.Contracts.Ai;

/// <summary>
/// One line of the streamed answer (application/x-ndjson): text as it arrives, each tool call, each client action for the client to run,
/// then the saved message or an error.
/// </summary>
public sealed record AiStreamEvent(string Type, string? Text = null, AiToolCallDto? ToolCall = null, AiMessageDto? Message = null, AiClientActionCall? Action = null)
{
    public const string Delta = "delta";

    public const string Tool = "tool";

    public const string ClientAction = "client_action";

    public const string Done = "done";

    public const string Error = "error";
}

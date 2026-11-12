namespace Coworkee.Contracts.Ai;

/// <summary>
/// One line of the streamed answer (application/x-ndjson): text as it arrives, each tool call, each client action for the client to run,
/// each read the client may answer from its local copies (<see cref="ClientQuery"/>), then the saved message or an error.
/// </summary>
public sealed record AiStreamEvent(string Type, string? Text = null, AiToolCallDto? ToolCall = null, AiMessageDto? Message = null, AiClientActionCall? Action = null)
{
    public const string Delta = "delta";

    public const string Tool = "tool";

    public const string ClientAction = "client_action";

    /// <summary>
    /// A read tool call (<see cref="Action"/>) on a set the client said it holds locally (<see cref="AiUiContext.LocalSets"/>): the client answers it with
    /// POST /api/v1/ai/sessions/{id}/client-results (<see cref="AiClientResult"/>); without an answer in time the server runs the tool itself.
    /// </summary>
    public const string ClientQuery = "client_query";

    public const string Done = "done";

    public const string Error = "error";
}

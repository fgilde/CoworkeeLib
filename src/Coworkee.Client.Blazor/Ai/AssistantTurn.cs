using Coworkee.Contracts.Ai;

namespace Coworkee.Client.Blazor.Ai;

public sealed record AssistantTurn(ChatMessageDto Message, IReadOnlyList<AiToolCallDto> ToolCalls);

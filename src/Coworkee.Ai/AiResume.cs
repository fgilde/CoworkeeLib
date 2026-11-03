using Coworkee.Contracts.Ai;
using Microsoft.Extensions.AI;

namespace Coworkee.Ai;

/// <summary>The decided round as messages for the model, and the tool calls that ran for it.</summary>
internal sealed record AiResume(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<AiToolCallDto> Calls);

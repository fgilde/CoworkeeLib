using Coworkee.Ai.Sessions;
using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

/// <summary>The answer of one turn: all text, the tool calls, and the round waiting for the user when a tool needs confirmation.</summary>
internal sealed record AiTurn(string Text, string StopReason, IReadOnlyList<AiToolCallDto> Calls, IReadOnlyList<AiPendingCall>? Pending);

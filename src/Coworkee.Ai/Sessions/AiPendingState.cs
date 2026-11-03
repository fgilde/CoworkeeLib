using Coworkee.Contracts.Ai;

namespace Coworkee.Ai.Sessions;

/// <summary>What a waiting message shows (redacted inputs) and its calls with their full arguments, encrypted with Data Protection.</summary>
internal sealed record AiPendingState(IReadOnlyList<AiPendingToolDto> Tools, string Calls);

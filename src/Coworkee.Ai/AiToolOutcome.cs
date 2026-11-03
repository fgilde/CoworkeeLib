using Coworkee.Contracts.Ai;

namespace Coworkee.Ai;

public sealed record AiToolOutcome(string Output, bool Succeeded, AiToolCallDto Call);

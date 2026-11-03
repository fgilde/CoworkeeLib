namespace Coworkee.Contracts.Workflows;

public sealed record DecisionRequest(bool Approve, string? Comment = null);

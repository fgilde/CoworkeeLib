namespace Coworkee.Contracts.Ai;

public sealed record AiConfirmRequest(bool Approve, AiUiContext? Ui = null);

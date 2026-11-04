namespace Coworkee.Contracts.Ai;

public sealed record AiSendRequest(string Text, AiUiContext? Ui = null);

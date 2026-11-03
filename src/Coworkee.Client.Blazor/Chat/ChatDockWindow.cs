namespace Coworkee.Client.Blazor.Chat;

/// <summary>An open mini chat: a window of the dock, or only its header bar while minimized.</summary>
public sealed record ChatDockWindow(Guid ConversationId, bool Minimized = false);

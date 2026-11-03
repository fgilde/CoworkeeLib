using Coworkee.Contracts.Chat;

namespace Coworkee.Chat;

/// <summary>The editor HTML of a message after the sanitizer, with its mentions in one canonical form.</summary>
internal sealed record ChatContent(string Html, string Text, IReadOnlyList<ChatMentionItem> Mentions);

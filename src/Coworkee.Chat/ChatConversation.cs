using Coworkee.Domain;

namespace Coworkee.Chat;

/// <summary>A direct chat of two people (<see cref="DirectKey"/> set) or a named group.</summary>
public sealed class ChatConversation : Entity, IMultiTenant
{
    public Guid TenantId { get; set; }

    public bool IsGroup { get; set; }

    public string? Name { get; set; }

    /// <summary>"{smaller id}:{larger id}" of a direct chat, so there is one per pair.</summary>
    public string? DirectKey { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastMessageAt { get; set; }

    public static string KeyOf(Guid a, Guid b) => a.CompareTo(b) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}

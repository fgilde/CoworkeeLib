using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Chat;

// texts, names and read marks stay out of the audit trail, so erasing a user's messages leaves no copy behind and reading writes no entries
internal sealed class ChatModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatConversation>(conversation =>
        {
            conversation.ToTable("ChatConversations", "cw");
            conversation.Property(c => c.Name).HasMaxLength(ChatLimits.NameLength).IsNotAudited();
            conversation.Property(c => c.DirectKey).HasMaxLength(80);
            conversation.Property(c => c.LastMessageAt).IsNotAudited();
            conversation.HasIndex(c => new { c.TenantId, c.DirectKey }).IsUnique();
        });
        modelBuilder.Entity<ChatMember>(member =>
        {
            member.ToTable("ChatMembers", "cw");
            member.Property(m => m.LastReadAt).IsNotAudited();
            member.HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();
            member.HasIndex(m => new { m.TenantId, m.UserId });
        });
        modelBuilder.Entity<ChatMessage>(message =>
        {
            message.ToTable("ChatMessages", "cw");
            message.Property(m => m.Text).HasMaxLength(ChatLimits.TextLength).IsNotAudited();
            message.Property(m => m.Html).HasMaxLength(ChatLimits.HtmlLength).IsNotAudited();
            message.Property(m => m.ReadAt).IsNotAudited();
            message.HasIndex(m => new { m.ConversationId, m.SentAt });
            message.HasIndex(m => new { m.TenantId, m.FromUserId, m.ToUserId, m.SentAt });
        });
        modelBuilder.Entity<ChatReaction>(reaction =>
        {
            reaction.ToTable("ChatReactions", "cw");
            reaction.Metadata.IsNotAudited();
            reaction.Property(r => r.Emoji).HasMaxLength(ChatLimits.EmojiLength);
            reaction.HasIndex(r => new { r.MessageId, r.UserId, r.Emoji }).IsUnique();
        });
        modelBuilder.Entity<ChatAttachment>(attachment =>
        {
            attachment.ToTable("ChatAttachments", "cw");
            attachment.Property(a => a.Name).HasMaxLength(ChatLimits.NameLength).IsNotAudited();
            attachment.Property(a => a.ContentType).HasMaxLength(200);
            attachment.Property(a => a.BlobKey).HasMaxLength(512);
            attachment.Property(a => a.ThumbnailKey).HasMaxLength(520);
            attachment.HasIndex(a => a.MessageId);
            attachment.HasIndex(a => new { a.ConversationId, a.UploadedBy });
        });
    }
}

internal static class ChatLimits
{
    public const int TextLength = 4000;
    public const int HtmlLength = 16000;
    public const int NameLength = 200;
    public const int EmojiLength = 16;
    public const int MaxAttachments = 10;
    public const int MaxMembers = 100;
    public const int DefaultAttachmentMb = 25;
}

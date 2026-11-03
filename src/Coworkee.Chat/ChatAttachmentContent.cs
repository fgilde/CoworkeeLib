namespace Coworkee.Chat;

public sealed record ChatAttachmentContent(Stream Content, string ContentType, string Name);

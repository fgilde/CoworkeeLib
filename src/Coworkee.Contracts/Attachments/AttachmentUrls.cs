namespace Coworkee.Contracts.Attachments;

public static class AttachmentUrls
{
    public const string Root = "api/v1/attachments";

    public static string Slot(string entityType, Guid entityId, string slot) =>
        $"{Root}/{Uri.EscapeDataString(entityType)}/{entityId}/{Uri.EscapeDataString(slot)}";

    /// <summary>The content, inline; <paramref name="download"/> asks the browser to save it.</summary>
    public static string Content(RecordAttachmentDto attachment, bool download = false) =>
        $"{Slot(attachment.EntityType, attachment.EntityId, attachment.Slot)}/{attachment.Id}" + (download ? "?download=true" : string.Empty);

    /// <summary>The anonymous address of an attachment behind a short-lived token (see <c>FileTokenDto</c>).</summary>
    public static string Token(string token, string name, bool download = false) =>
        $"{Root}/token/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(name)}" + (download ? "?download=true" : string.Empty);
}

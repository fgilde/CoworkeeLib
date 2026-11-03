namespace Coworkee.Contracts.Attachments;

public static class AttachmentTopics
{
    /// <summary>Realtime topic that signals every added, replaced or deleted attachment of one record.</summary>
    public static string Record(string entityType, Guid entityId) => $"attachments:{entityType}:{entityId}";
}

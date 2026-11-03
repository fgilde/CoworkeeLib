namespace Coworkee.Files.Attachments;

/// <summary>The record types that take attachments, by the name used in the address (usually the OData set name).</summary>
public sealed class RecordAttachmentOptions
{
    internal Dictionary<string, RecordAttachmentType> Types { get; } = new(StringComparer.Ordinal);

    public RecordAttachmentType? Find(string entityType) => Types.GetValueOrDefault(entityType);
}

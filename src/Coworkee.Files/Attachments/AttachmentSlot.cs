namespace Coworkee.Files.Attachments;

/// <param name="ContentTypes">Accepted types such as <c>application/pdf</c> or <c>image/*</c>; empty takes any.</param>
/// <param name="Multiple">False: an upload replaces the file of the slot.</param>
public sealed record AttachmentSlot(string Name, long MaxBytes, IReadOnlyList<string> ContentTypes, bool Multiple)
{
    public bool Accepts(string contentType) =>
        ContentTypes.Count == 0 || ContentTypes.Any(t => t.EndsWith("/*", StringComparison.Ordinal)
            ? contentType.StartsWith(t[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(t, contentType, StringComparison.OrdinalIgnoreCase));
}

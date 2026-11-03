namespace Coworkee.Files;

public sealed class FilesOptions
{
    public const string Section = "Coworkee:Files";

    /// <summary>Largest upload in bytes; also the request size limit of the upload endpoint.</summary>
    public long MaxFileSize { get; set; } = 2L * 1024 * 1024 * 1024;
}

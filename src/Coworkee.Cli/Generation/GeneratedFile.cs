namespace Coworkee.Cli.Generation;

/// <summary>A file to write; its content has LF line ends, whatever the line ends of the generator's source are.</summary>
internal sealed record GeneratedFile(string Path, string Content)
{
    public string Content { get; } = Content.ReplaceLineEndings("\n");
}

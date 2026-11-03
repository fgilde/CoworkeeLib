namespace Coworkee.Contracts.Configuration;

public sealed class BffPwaOptions
{
    /// <summary>Name of the installed app (default: the host name).</summary>
    public string? Name { get; set; }

    /// <summary>Icon path of the web app for themes without a logo, e.g. <c>/coworkee-icon.svg</c>.</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Icons of the web app with their sizes for themes without a logo, e.g. a 192x192 and a 512x512 PNG; the manifest lists them instead of
    /// <see cref="Icon"/>. Paths are local (<c>/icons/192.png</c>).
    /// </summary>
    public List<BffPwaIcon> Icons { get; set; } = [];
}

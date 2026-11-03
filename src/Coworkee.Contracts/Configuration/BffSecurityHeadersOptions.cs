namespace Coworkee.Contracts.Configuration;

public sealed class BffSecurityHeadersOptions
{
    /// <summary>Extra sources per directive, e.g. <c>script-src</c>: <c>https://apis.google.com</c> for the Google Drive picker.</summary>
    public Dictionary<string, List<string>> Sources { get; set; } = [];

    /// <summary>Replaces the whole policy; frame-ancestors of <see cref="BffOptions.FrameAncestors"/> is not added then.</summary>
    public string? ContentSecurityPolicy { get; set; }

    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// CDNs the viewers of MudExFileDisplay load their scripts and styles from the first time they show a type (Word, Excel, maps, 3D, HEIC, PSD, ...);
    /// allowed in script-src, style-src and connect-src. Clear it for a policy without third-party scripts; those file types then show no preview.
    /// </summary>
    public List<string> FileViewerCdns { get; set; } = ["https://cdn.jsdelivr.net", "https://esm.sh", "https://unpkg.com"];
}

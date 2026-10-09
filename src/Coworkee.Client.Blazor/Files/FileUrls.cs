using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Files;

public static class FileUrls
{
    public static string Content(Guid id, bool download = false) => $"api/v1/files/{id}/content" + (download ? "?download=true" : string.Empty);

    /// <summary>MudExFileDisplay loads with its own HttpClient, which needs an absolute address.</summary>
    public static string FileUrl(this NavigationManager navigation, Guid id, bool download = false) => navigation.ToAbsoluteUri(Content(id, download)).ToString();
}

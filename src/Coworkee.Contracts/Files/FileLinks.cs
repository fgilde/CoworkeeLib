namespace Coworkee.Contracts.Files;

public static class FileLinks
{
    /// <summary>The anonymous address of a public file, relative to the API (or the web app that forwards /api); ends with the file name so its type shows.</summary>
    public static string Public(Guid id, string name) => $"api/v1/files/public/{id}/{Uri.EscapeDataString(name)}";

    /// <summary>The anonymous address of a file behind a short-lived token, relative to the API; <paramref name="download"/> asks the browser to save it.</summary>
    public static string Token(string token, string name, bool download = false) =>
        $"api/v1/files/token/{Uri.EscapeDataString(token)}/{Uri.EscapeDataString(name)}" + (download ? "?download=true" : string.Empty);
}

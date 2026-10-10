using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Coworkee.Studio.Model;

public static class Images
{
    private static readonly string[] Raster = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico"];

    public static bool IsRaster(string? path) => path is not null && Raster.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsVideo(string? path) => path is not null && new[] { ".mp4", ".webm", ".ogg" }.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>A preview of a local image, or null for other files and when there is no renderer (unit tests).</summary>
    public static Bitmap? TryLoad(string? path)
    {
        if (!IsRaster(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Bitmap? Asset(string name)
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://Coworkee.Studio/Assets/{name}")));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

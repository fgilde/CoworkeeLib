using SkiaSharp;

namespace Coworkee.Chat;

/// <summary>Small WebP pictures of image attachments for the message list; the full file opens in the viewer.</summary>
internal static class ChatThumbnails
{
    public const string ContentType = "image/webp";

    public const int Edge = 480;

    // ponytail: decoded in the upload request; pictures that would need more pixels than this get none (about 100 MB of memory)
    private const long MaxPixels = 25_000_000;

    public static string KeyOf(string blobKey) => blobKey + ".thumb";

    /// <summary>The WebP of a picture Skia reads, turned upright; null for anything else.</summary>
    public static byte[]? Create(Stream image)
    {
        using var codec = SKCodec.Create(new SKManagedStream(image, false));
        if (codec is null)
        {
            return null;
        }

        var scale = Math.Min(1f, (float)Edge / Math.Max(codec.Info.Width, codec.Info.Height));
        var decoded = codec.GetScaledDimensions(scale);
        if (scale < 1 && Math.Max(decoded.Width, decoded.Height) < Edge)
        {
            // the format decodes only some scales (eighths for JPEG) and the nearest one is too small
            decoded = codec.Info.Size;
        }

        if ((long)decoded.Width * decoded.Height > MaxPixels)
        {
            return null;
        }

        var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        var factor = Math.Min(1f, (float)Edge / Math.Max(decoded.Width, decoded.Height));
        var (width, height) = (Math.Max(1, (int)(decoded.Width * factor)), Math.Max(1, (int)(decoded.Height * factor)));
        var turned = codec.EncodedOrigin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom;
        using var surface = SKSurface.Create(new SKImageInfo(turned ? height : width, turned ? width : height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        switch (codec.EncodedOrigin)
        {
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, width / 2f, height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(height, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, width);
                canvas.RotateDegrees(270);
                break;
        }

        using var source = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(source, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var snapshot = surface.Snapshot();
        using var webp = snapshot.Encode(SKEncodedImageFormat.Webp, 80);
        return webp?.ToArray();
    }
}

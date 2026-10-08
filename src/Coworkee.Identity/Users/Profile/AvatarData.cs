using System.Text.RegularExpressions;

namespace Coworkee.Identity.Users.Profile;

internal static partial class AvatarData
{
    public static bool TryParse(string dataUrl, out string contentType, out byte[] content)
    {
        (contentType, content) = (string.Empty, []);
        if (Format().Match(dataUrl) is not { Success: true } match)
        {
            return false;
        }

        try
        {
            content = Convert.FromBase64String(match.Groups[2].Value);
            contentType = match.Groups[1].Value;
            return content.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex("^data:(image/(?:png|jpeg|gif|webp));base64,([A-Za-z0-9+/=]+)$")]
    private static partial Regex Format();
}

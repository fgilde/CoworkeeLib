using System.Text.RegularExpressions;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;

namespace Coworkee.AspNetCore.Http;

/// <summary>Reads the version from literal routes like /api/v2/…, so the paths and the OpenAPI documents need no {version} parameter.</summary>
internal sealed partial class PathApiVersionReader : IApiVersionReader
{
    public static int? VersionOf(string path) => Segment().Match(path) is { Success: true } match ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;

    public IReadOnlyList<string> Read(HttpRequest request) => VersionOf(request.Path.Value ?? string.Empty) is { } version ? [version.ToString(System.Globalization.CultureInfo.InvariantCulture)] : [];

    public void AddParameters(IApiVersionParameterDescriptionContext context)
    {
    }

    [GeneratedRegex(@"^/?api/v(\d{1,4})(?:/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Segment();
}

using System.Reflection;

namespace Coworkee.Client.Blazor.Components.About;

public static class AboutVersion
{
    /// <summary>
    /// The highest version the assembly states (informational without the '+' metadata, file or assembly version), the informational one on ties
    /// so prerelease tags stay; for an assembly that states none (Nextended), the package version this library was built with; else null.
    /// </summary>
    public static string? Of(Assembly assembly) => Stated(assembly) ?? typeof(AboutVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "PackageVersion:" + assembly.GetName().Name)?.Value;

    private static string? Stated(Assembly assembly) =>
        new[]
            {
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0],
                assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
                assembly.GetName().Version?.ToString(),
            }
            .Select(text => (Text: text, Number: Number(text)))
            .Where(version => version.Number is not null)
            .OrderByDescending(version => version.Number)
            .Select(version => version.Text)
            .FirstOrDefault();

    private static Version? Number(string? text) =>
        Version.TryParse(text?.Split('-')[0], out var parsed) && new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0)) is var number
        && number > new Version(0, 0, 0, 0) ? number : null;
}

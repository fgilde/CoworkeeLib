namespace Coworkee.Cli.Apps;

/// <summary>A package or MSBuild SDK raised to <see cref="Version"/> where the app has an older one.</summary>
internal sealed record PackageRule(string Id, string Version);

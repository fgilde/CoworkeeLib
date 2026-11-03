namespace Coworkee.Cli.Apps;

/// <summary>How <c>coworkee add</c>/<c>remove</c> runs: the template from <see cref="TemplateSource"/> or nuget.org, or one already installed in <see cref="Hive"/>.</summary>
internal sealed record ApplyOptions(bool DryRun, bool RequireClean, string? TemplateSource = null, string? Hive = null, bool Migration = true);

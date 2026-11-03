using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal sealed record NewSettings(AppSpec Spec, string BaseDirectory, string Output, string? TemplateSource, IReadOnlyCollection<string>? TrustedFiles = null);

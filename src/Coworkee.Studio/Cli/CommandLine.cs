using Coworkee.Cli.Spec;

namespace Coworkee.Studio.Cli;

public static class CommandLine
{
    public static string[] Validate(string specFile) => ["spec", "validate", specFile];

    /// <summary>
    /// The spec file for <c>coworkee new</c> and its arguments: output folder, logo and sign-in media chosen anywhere on disk go on the
    /// command line (the CLI trusts only typed paths outside the spec folder), the rest stays in the spec.
    /// </summary>
    public static (AppSpec Spec, string[] Args) New(AppSpec spec, string specFile, string? templateSource)
    {
        var copy = SpecJson.Read(SpecJson.Write(spec));
        var args = new List<string> { "new", "--spec", specFile };
        if (copy.Output is { } output)
        {
            args.AddRange(["-o", output]);
            copy.Output = null;
        }

        if (copy.Branding is { } branding)
        {
            if (branding.Logo is { } logo)
            {
                args.AddRange(["--logo", logo]);
                branding.Logo = null;
            }

            if (branding.SignInMedia is { } media && !SpecValidator.IsWebAddress(media))
            {
                args.AddRange(["--sign-in-media", media]);
                branding.SignInMedia = null;
            }

            if (branding is { Title: null, Theme: null, PrimaryColor: null, SecondaryColor: null, Logo: null, SignInLayout: null, SignInMedia: null, SignInMediaSide: null })
            {
                copy.Branding = null;
            }
        }

        if (!string.IsNullOrWhiteSpace(templateSource))
        {
            args.AddRange(["--template-source", templateSource]);
        }

        return (copy, [.. args]);
    }

    /// <summary><c>coworkee add --spec</c> for an existing app, run in its folder.</summary>
    public static string[] Add(string specFile, bool dryRun, string? templateSource) =>
        ["add", "--spec", specFile, .. dryRun ? new[] { "--dry-run" } : [], .. string.IsNullOrWhiteSpace(templateSource) ? [] : new[] { "--template-source", templateSource }];

    public static string[] InstallCli(string? feed) =>
        ["tool", "install", "-g", "Coworkee.Cli", .. string.IsNullOrWhiteSpace(feed) ? Array.Empty<string>() : ["--add-source", feed, "--prerelease"]];

    /// <summary><c>coworkee new</c> with a switch per option that differs from its default; modules and entities need the spec.</summary>
    public static string[] Flags(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        var args = new List<string> { "coworkee", "new", spec.Name };
        if (!string.IsNullOrEmpty(spec.Output))
        {
            args.AddRange(["-o", spec.Output]);
        }

        foreach (var option in OptionCatalog.All)
        {
            if (values[option.Id] is { } value && value != option.Default && OptionCatalog.IsActive(option, values))
            {
                args.AddRange([option.Flag, value]);
            }
        }

        // roles of the spec need the spec, so only the built-in ones go along
        var builtIn = PermissionCatalog.BuiltInRoles(spec);
        foreach (var user in spec.Users)
        {
            var roles = user.Roles.Where(r => builtIn.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
            args.AddRange(["--user", string.Join(':', [user.Email, .. roles.Count > 0 ? [string.Join(',', roles)] : Array.Empty<string>(), .. user.Admin ? ["admin"] : Array.Empty<string>()])]);
        }

        return [.. args];
    }

    public static string Format(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    public static string Quote(string arg) =>
        arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || "\"'&|;<>()$`".Contains(c)) ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";
}

namespace Coworkee.Cli.Spec;

public static partial class SpecValidator
{
    /// <summary>What the spec sets without effect: options whose <see cref="CliOption.DependsOn"/> option is off or has another value. They are left out of the app.</summary>
    public static IReadOnlyList<string> Warnings(AppSpec spec)
    {
        var values = SpecOptions.Resolve(spec);
        return
        [
            .. OptionCatalog.All
                .Where(o => SpecOptions.IsSet(spec, o.Id) && values[o.Id] != o.Default)
                .Select(o => (Option: o, Unmet: OptionCatalog.Unmet(o, id => values[id])))
                .Where(x => x.Unmet is not null)
                .Select(x => $"{x.Option.Id}: ignored, it only matters with {x.Unmet} (coworkee options).")
        ];
    }
}

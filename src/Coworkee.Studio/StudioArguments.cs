namespace Coworkee.Studio;

/// <summary>coworkee-ui [--spec app.json] [--output folder] [--theme light|dark] [--lang en|de]; coworkee ui passes its other arguments here.</summary>
public sealed record StudioArguments(string? Spec, string? Output, string? Theme, string? Language)
{
    public static StudioArguments Parse(IReadOnlyList<string> args)
    {
        string? Value(string name)
        {
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Count ? args[i + 1] : null;
                }

                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i][(name.Length + 1)..];
                }
            }

            return null;
        }

        // a lone .json argument opens it as well, so the Studio can be the program for spec files
        var spec = Value("--spec") ?? args.FirstOrDefault(a => a.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !a.StartsWith('-'));
        return new(spec, Value("--output") ?? Value("-o"), Value("--theme"), Value("--lang"));
    }
}

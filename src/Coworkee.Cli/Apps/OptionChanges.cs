using System.Text.Json.Nodes;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Apps;

/// <summary>
/// The words of <c>coworkee add</c> and <c>coworkee remove</c>: option ids, each optionally followed by its value
/// (<c>eventBus rabbitMq</c> or <c>eventBus=rabbitMq</c>). A bool option followed by a choice of an option that depends on it sets both (<c>ai openAI</c>).
/// </summary>
internal static class OptionChanges
{
    /// <summary>The spec with the changes applied; throws <see cref="CliException"/> for unknown options or values.</summary>
    public static AppSpec Apply(AppSpec current, IReadOnlyList<string> words, bool remove)
    {
        var target = SpecJson.Read(SpecJson.Write(AppState.Stored(current)));
        var values = SpecOptions.Resolve(current);
        foreach (var (option, value) in Parse(words))
        {
            foreach (var (id, text) in remove ? Removed(option, value, values) : Added(option, value, values))
            {
                var changed = OptionCatalog.Find(id)!;
                target.Options[id] = text is null ? null : changed.Kind == OptionKind.Bool ? JsonValue.Create(text == "true") : JsonValue.Create(text);
                values[id] = text;
            }
        }

        return AppState.Stored(target);
    }

    /// <summary>A name for the migration of the changes, e.g. AddReports or AddEventBusRabbitMq.</summary>
    public static string MigrationName(IReadOnlyList<string> words, bool remove) =>
        (remove ? "Remove" : "Add") + string.Concat(Parse(words).Select(c => Pascal(c.Option.Id) + (c.Option.Kind == OptionKind.Choice && c.Value is { } v ? Pascal(v) : string.Empty)));

    private static List<(CliOption Option, string? Value)> Parse(IReadOnlyList<string> words)
    {
        var changes = new List<(CliOption Option, string? Value)>();
        foreach (var word in words)
        {
            var parts = word.Split('=', 2);
            if (OptionCatalog.Find(parts[0]) is { } option)
            {
                changes.Add((option, parts.Length == 2 ? parts[1] : null));
            }
            else if (parts.Length == 1 && changes.Count > 0 && changes[^1].Value is null)
            {
                changes[^1] = (changes[^1].Option, word);
            }
            else
            {
                throw new CliException($"Unknown option '{parts[0]}'. coworkee options lists them.");
            }
        }

        return changes.Count > 0 ? changes : throw new CliException("Name at least one option, e.g. coworkee add reports.");
    }

    private static IEnumerable<(string Id, string? Value)> Added(CliOption option, string? value, IReadOnlyDictionary<string, string?> values)
    {
        switch (option.Kind)
        {
            case OptionKind.Bool when value is null or "true" or "false":
                yield return (option.Id, value ?? "true");
                break;
            case OptionKind.Bool:
                var dependent = OptionCatalog.All.FirstOrDefault(o => o.DependsOn?.Option == option.Id && o.Choices?.Any(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase)) == true)
                    ?? throw new CliException($"{option.Id} is on or off; '{value}' is no choice of an option that belongs to it.");
                yield return (option.Id, "true");
                yield return (dependent.Id, Choice(dependent, value!));
                break;
            case OptionKind.Multi:
                var items = SpecOptions.List(Required(option, value));
                foreach (var item in items.Where(i => !option.Choices!.Any(c => c.Value.Equals(i, StringComparison.OrdinalIgnoreCase))))
                {
                    throw new CliException($"{option.Id}: '{item}' is not one of {string.Join(", ", option.Choices!.Select(c => c.Value))}.");
                }

                var current = values[option.Id] is { } list ? SpecOptions.List(list) : option.Id == "languages" ? OptionCatalog.DefaultLanguages : [];
                yield return (option.Id, string.Join(",", current.Union(items, StringComparer.OrdinalIgnoreCase)));
                break;
            case OptionKind.Choice:
                yield return (option.Id, Choice(option, Required(option, value)));
                break;
            default:
                yield return (option.Id, Required(option, value));
                break;
        }
    }

    private static IEnumerable<(string Id, string? Value)> Removed(CliOption option, string? value, IReadOnlyDictionary<string, string?> values)
    {
        if (option is { Kind: OptionKind.Multi } && value is not null)
        {
            var items = SpecOptions.List(value);
            var current = values[option.Id] is { } list ? SpecOptions.List(list) : option.Id == "languages" ? OptionCatalog.DefaultLanguages : [];
            yield return (option.Id, string.Join(",", current.Where(c => !items.Contains(c, StringComparer.OrdinalIgnoreCase))));
        }
        else
        {
            yield return (option.Id, option.Kind == OptionKind.Bool ? "false" : option.Default);
        }
    }

    private static string Required(CliOption option, string? value) =>
        value ?? throw new CliException($"{option.Id} needs a value{(option.Choices is { } choices ? ": " + string.Join(", ", choices.Select(c => c.Value)) : string.Empty)}.");

    private static string Choice(CliOption option, string value) =>
        option.Choices?.FirstOrDefault(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase))?.Value
        ?? throw new CliException($"{option.Id}: '{value}' is not one of {string.Join(", ", option.Choices?.Select(c => c.Value) ?? [])}.");

    private static string Pascal(string text) => string.Concat(text.Split('-', ',', ' ').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
}

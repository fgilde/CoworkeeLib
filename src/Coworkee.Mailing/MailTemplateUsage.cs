using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Syntax;

namespace Coworkee.Mailing;

/// <summary>
/// What a template reads from its model: the paths of its expressions (<c>shipment.number</c>, <c>shipment.containers[].number</c> for the members
/// of a loop variable), and the sample model cut down to them, so a send asks only for the values the mail shows.
/// </summary>
public static class MailTemplateUsage
{
    /// <summary>Keys the send fills for each recipient (<c>user</c>, <c>unsubscribe_url</c>): never asked for.</summary>
    public static readonly IReadOnlySet<string> PerRecipient = new HashSet<string>(StringComparer.Ordinal) { "user", "unsubscribe_url" };

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The paths <paramref name="texts"/> (subjects and bodies, any language) read; a text that does not parse adds nothing.</summary>
    public static Usage Read(IEnumerable<string?> texts)
    {
        var collector = new PathCollector();
        foreach (var text in texts)
        {
            if (!string.IsNullOrWhiteSpace(text) && Template.Parse(text) is { HasErrors: false, Page: { } page })
            {
                page.Accept(collector);
            }
        }

        return new Usage(collector.Whole, collector.Iterated);
    }

    /// <summary>
    /// <paramref name="sampleJson"/> with only the members <paramref name="usage"/> reads, list items cut the same way, without <see cref="PerRecipient"/>;
    /// an indented JSON object, <c>{}</c> when the template reads nothing of it.
    /// </summary>
    public static string SendModel(string sampleJson, Usage usage)
    {
        var sample = JsonNode.Parse(sampleJson) as JsonObject ?? [];
        var result = Prune(sample, null, usage);
        foreach (var key in PerRecipient)
        {
            result.Remove(key);
        }

        return result.ToJsonString(Indented);
    }

    private static JsonObject Prune(JsonObject source, string? prefix, Usage usage)
    {
        var result = new JsonObject();
        foreach (var (key, value) in source)
        {
            var path = prefix is null ? key : $"{prefix}.{key}";
            if (usage.ReadsWhole(path))
            {
                result[key] = value?.DeepClone();
            }
            else if (usage.ReadsInside(path))
            {
                result[key] = value switch
                {
                    JsonObject child => Prune(child, path, usage),
                    JsonArray items => new JsonArray([.. items.Select(item => item is JsonObject element ? Prune(element, path + "[]", usage) : item?.DeepClone())]),
                    _ => value?.DeepClone(),
                };
            }
        }

        return result;
    }

    /// <summary>The paths a template reads whole (<see cref="Whole"/>) and the lists it loops over (<see cref="Iterated"/>).</summary>
    public sealed record Usage(IReadOnlySet<string> Whole, IReadOnlySet<string> Iterated)
    {
        /// <summary>The path or one of its parents is read as a value: <c>{{ shipment }}</c> reads all of <c>shipment.number</c>.</summary>
        public bool ReadsWhole(string path) => Whole.Contains(path) || Whole.Any(p => path.StartsWith(p + ".", StringComparison.Ordinal));

        /// <summary>Something below the path is read, or the template loops over it.</summary>
        public bool ReadsInside(string path) =>
            Iterated.Contains(path) || Whole.Concat(Iterated).Any(p => p.StartsWith(path + ".", StringComparison.Ordinal) || p.StartsWith(path + "[]", StringComparison.Ordinal));
    }

    // a path is the chain of a global variable and its members; a loop variable stands for "<list>[]" inside its loop
    private sealed class PathCollector : ScriptVisitor
    {
        private readonly Dictionary<string, string> _loops = new(StringComparer.Ordinal);

        public HashSet<string> Whole { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Iterated { get; } = new(StringComparer.Ordinal);

        public override void Visit(ScriptVariableGlobal node) => Add(node);

        public override void Visit(ScriptMemberExpression node) => Add(node);

        public override void Visit(ScriptIndexerExpression node) => Add(node);

        public override void Visit(ScriptForStatement node) => Loop(node.Variable, node.Iterator, () =>
        {
            node.Body?.Accept(this);
            node.Else?.Accept(this);
        });

        public override void Visit(ScriptTableRowStatement node) => Loop(node.Variable, node.Iterator, () => node.Body?.Accept(this));

        private void Loop(ScriptExpression? variable, ScriptExpression? iterator, Action body)
        {
            var list = Path(iterator);
            if (list is null)
            {
                iterator?.Accept(this);
            }
            else
            {
                Iterated.Add(list);
                VisitIndexes(iterator);
            }

            if (list is null || variable is not ScriptVariable { Name: { } name })
            {
                body();
                return;
            }

            var shadowed = _loops.TryGetValue(name, out var outer);
            _loops[name] = list + "[]";
            body();
            if (shadowed)
            {
                _loops[name] = outer!;
            }
            else
            {
                _loops.Remove(name);
            }
        }

        private void Add(ScriptExpression node)
        {
            if (Path(node) is { } path)
            {
                Whole.Add(path);
            }

            VisitIndexes(node);
        }

        // a[b.c].d reads b.c as well
        private void VisitIndexes(ScriptExpression? node)
        {
            switch (node)
            {
                case ScriptMemberExpression member:
                    VisitIndexes(member.Target);
                    break;
                case ScriptIndexerExpression indexer:
                    VisitIndexes(indexer.Target);
                    indexer.Index?.Accept(this);
                    break;
            }
        }

        private string? Path(ScriptExpression? node) => node switch
        {
            ScriptVariableGlobal variable => _loops.GetValueOrDefault(variable.Name) ?? variable.Name,
            ScriptMemberExpression { Member.Name: { } member } expression => Path(expression.Target) is { } target ? $"{target}.{member}" : null,
            ScriptIndexerExpression indexer => Path(indexer.Target) is { } target ? target + "[]" : null,
            _ => null,
        };
    }
}

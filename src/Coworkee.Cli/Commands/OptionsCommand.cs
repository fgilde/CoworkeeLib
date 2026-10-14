using System.CommandLine;
using System.Text.Json;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal static class OptionsCommand
{
    private static readonly Option<bool> Json = new("--json") { Description = "Print the catalog as JSON: id, flag, group, title, description, kind, default, choices, pros, cons, when to use." };

    public static Command Create() => new Command("options", "List every switch of coworkee new and the spec, with default, choices and when to use it.") { Json }
        .Handle((result, _) =>
        {
            Console.WriteLine(result.GetValue(Json) ? CatalogJson() : Text());
            return Task.FromResult(0);
        });

    public static string CatalogJson(string? group = null) => JsonSerializer.Serialize(OptionCatalog.All.Where(o => group is null || o.Group.Equals(group, StringComparison.OrdinalIgnoreCase)).Select(o => new { o.Id, o.Flag, o.Group, o.Title, o.Description, o.Kind, o.Default, o.Choices, o.WhenToUse, o.Pros, o.Cons, o.Package, o.TemplateSymbol, o.InfoUrl, DependsOn = o.DependsOn is { } need ? new { need.Option, need.Values } : null }), SpecJson.Options);

    public static string Text()
    {
        var lines = new List<string>();
        foreach (var group in OptionCatalog.All.GroupBy(o => o.Group))
        {
            lines.Add(group.Key);
            foreach (var option in group)
            {
                var values = option.Choices is { } choices ? string.Join("|", choices.Select(c => c.Value)) : option.Kind.ToString().ToLowerInvariant();
                lines.Add($"  {option.Flag,-24} {values,-34} default {option.Default ?? "-"}");
                lines.Add($"  {string.Empty,-24} {option.Description}");
                lines.Add($"  {string.Empty,-24} When: {option.WhenToUse}");
                if (option.DependsOn is { } need)
                {
                    lines.Add($"  {string.Empty,-24} Needs: {need} (otherwise ignored)");
                }

                if (option.InfoUrl is { } info)
                {
                    lines.Add($"  {string.Empty,-24} More: {info}");
                }

                foreach (var choice in option.Choices?.Where(c => c.InfoUrl is not null && option.Kind == OptionKind.Choice) ?? [])
                {
                    lines.Add($"  {string.Empty,-24} More ({choice.Value}): {choice.InfoUrl}");
                }
            }

            lines.Add(string.Empty);
        }

        lines.Add("In a spec, use the id (the flag in camel case) under \"options\": coworkee spec example.");
        return string.Join(Environment.NewLine, lines);
    }
}

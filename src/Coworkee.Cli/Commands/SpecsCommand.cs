using System.CommandLine;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal static class SpecsCommand
{
    private static readonly Argument<string> Id = new("id") { Description = "Id of the starter spec, see coworkee specs list." };

    public static Command Create() => new("specs", "Starter specs that come with the CLI: a CRM, a helpdesk, a shop and more. coworkee new MyApp --template-spec <id> creates one.")
    {
        new Command("list", "List the starter specs with id, tags and what they contain.").Handle((_, _) =>
        {
            Console.WriteLine(List());
            return Task.FromResult(0);
        }),
        new Command("show", "Print a starter spec as JSON, e.g. to save it and change it: coworkee specs show crm > crm.json") { Id }.Handle((result, _) =>
        {
            Console.WriteLine(Json(result.GetValue(Id)!));
            return Task.FromResult(0);
        }),
    };

    public static string List() => string.Join(Environment.NewLine, SpecGallery.All.SelectMany(e => new[]
    {
        $"{e.Id,-14} {e.Title} [{string.Join(", ", e.Tags)}]",
        $"{string.Empty,-14} {e.Description}",
    }).Append(string.Empty).Append("coworkee new MyApp --template-spec <id>, or coworkee specs show <id> > app.json to change it first."));

    public static AppSpec Read(string id) => SpecJson.Read(Json(id));

    private static string Json(string id)
    {
        try
        {
            return SpecGallery.Json(id);
        }
        catch (KeyNotFoundException exception)
        {
            throw new CliException(exception.Message);
        }
    }
}

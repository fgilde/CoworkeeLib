using System.CommandLine;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Commands;

internal static class SpecCommand
{
    private static readonly Argument<FileInfo> File = new("file") { Description = "The spec to check." };

    public static Command Create() => new("spec", "App specs for coworkee new --spec: JSON with name, options, branding and modules with entities.")
    {
        Print("schema", "Print the JSON Schema of a spec.", () => SpecJson.Schema().ToJsonString(SpecJson.Options)),
        Print("example", "Print an example spec.", () => SpecJson.Write(SpecJson.Example())),
        new Command("validate", "Check a spec; prints the problems and exits with 1 when there are any.") { File }.Handle((result, _) =>
        {
            var file = result.GetValue(File)!;
            var spec = NewCommand.ReadSpec(file.FullName);
            var errors = SpecValidator.Validate(spec, file.DirectoryName);
            Console.WriteLine(errors.Count == 0 ? "ok" : string.Join(Environment.NewLine, errors));
            foreach (var warning in SpecValidator.Warnings(spec))
            {
                Console.WriteLine($"warn  {warning}");
            }

            return Task.FromResult(errors.Count == 0 ? 0 : 1);
        }),
        new Command("show", "Print what a spec creates: the library modules on and off, every other option with its value and your own modules. A spec lists only what differs from the defaults.") { File }.Handle((result, _) =>
        {
            Console.WriteLine(string.Join(Environment.NewLine, SpecSummary.Lines(NewCommand.ReadSpec(result.GetValue(File)!.FullName))));
            return Task.FromResult(0);
        }),
    };

    private static Command Print(string name, string description, Func<string> text) => new Command(name, description).Handle((_, _) =>
    {
        Console.WriteLine(text());
        return Task.FromResult(0);
    });
}

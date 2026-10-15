using System.Runtime.CompilerServices;
using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class GalleryTests
{
    private static readonly string Repository = Path.GetFullPath(Path.Combine(Here(), "..", "..", ".."));

    [Fact]
    public void Every_starter_spec_is_valid_without_warnings_and_described_in_the_gallery()
    {
        var files = Directory.GetFiles(Path.Combine(Repository, "templates", "specs"), "*.json").Select(Path.GetFileNameWithoutExtension).Where(n => n != "gallery");

        SpecGallery.All.Select(e => e.Id).ShouldBe(files, ignoreOrder: true);
        SpecGallery.All.Count.ShouldBeInRange(5, 8);
        foreach (var entry in SpecGallery.All)
        {
            var spec = SpecGallery.Read(entry.Id);
            SpecValidator.Validate(spec).ShouldBeEmpty(entry.Id);
            SpecValidator.Warnings(spec).ShouldBeEmpty(entry.Id);
            new[] { entry.Title, entry.Description, entry.Icon, entry.De?.Title, entry.De?.Description }.ShouldAllBe(t => !string.IsNullOrWhiteSpace(t), entry.Id);
            entry.Tags.ShouldNotBeEmpty(entry.Id);
        }
    }

    [Fact]
    public void The_published_schema_is_the_one_of_the_cli()
    {
        var published = File.ReadAllText(Path.Combine(Repository, "website", "docs", "assets", "spec.schema.json"));

        published.ReplaceLineEndings().Trim().ShouldBe(SpecJson.Schema().ToJsonString(SpecJson.Options).ReplaceLineEndings().Trim(), "run: coworkee spec schema > website/docs/assets/spec.schema.json");
    }

    [Fact]
    public void New_starts_from_a_starter_spec_and_switches_win()
    {
        var settings = NewCommand.Read(CliApp.Build().Parse("new Sales --template-spec crm --database sqlite"));

        settings.Spec.Name.ShouldBe("Sales");
        settings.Spec.Modules.Single().Name.ShouldBe("Sales");
        settings.Spec.Options["database"]!.GetValue<string>().ShouldBe("sqlite");
        settings.Output.ShouldBe(Path.GetFullPath("Sales"));
        NewCommand.Read(CliApp.Build().Parse("new --template-spec Helpdesk")).Spec.Name.ShouldBe("Helpdesk");
    }

    [Fact]
    public void An_unknown_starter_or_two_specs_are_refused()
    {
        Should.Throw<CliException>(() => NewCommand.Read(CliApp.Build().Parse("new Shop --template-spec nope"))).Message.ShouldContain("crm");
        Should.Throw<CliException>(() => NewCommand.Read(CliApp.Build().Parse("new Shop --template-spec crm --spec app.json")));
    }

    [Fact]
    public void Specs_lists_and_shows_the_starters()
    {
        SpecGallery.All.ShouldAllBe(e => SpecsCommand.List().Contains(e.Id));
        SpecsCommand.Read("shop").Name.ShouldBe("Shop");
        CliApp.Build().Parse("specs list").Invoke().ShouldBe(0);
        CliApp.Build().Parse("specs show shop").Invoke().ShouldBe(0);
        CliApp.Build().Parse("specs show nope").Invoke().ShouldBe(1);
    }

    private static string Here([CallerFilePath] string path = "") => path;
}

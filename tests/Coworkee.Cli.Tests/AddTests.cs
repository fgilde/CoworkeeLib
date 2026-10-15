using System.Text.Json.Nodes;
using Coworkee.Cli.Apps;
using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Tests;

public sealed class AddTests
{
    private static AppSpec Shop(params (string Id, JsonNode Value)[] options)
    {
        var spec = new AppSpec { Name = "Shop" };
        foreach (var (id, value) in options)
        {
            spec.Options[id] = value;
        }

        return spec;
    }

    [Fact]
    public void Add_turns_bools_on_sets_choices_and_extends_lists()
    {
        var current = Shop(("reports", false), ("languages", new JsonArray("en", "de")));

        var target = SpecOptions.Resolve(OptionChanges.Apply(current, ["reports", "eventBus", "rabbitMq", "languages=fr,it", "storage=AzureBlob"], remove: false));

        target["reports"].ShouldBe("true");
        target["eventBus"].ShouldBe("rabbitMq");
        target["languages"].ShouldBe("en,de,fr,it");
        target["storage"].ShouldBe("azureBlob");
        OptionChanges.MigrationName(["reports", "eventBus", "rabbitMq"], remove: false).ShouldBe("AddReportsEventBusRabbitMq");
    }

    [Fact]
    public void A_bool_followed_by_a_choice_of_its_dependent_option_sets_both()
    {
        var target = SpecOptions.Resolve(OptionChanges.Apply(Shop(("ai", false)), ["ai", "openAI"], remove: false));

        target["ai"].ShouldBe("true");
        target["aiProvider"].ShouldBe("openAI");
    }

    [Fact]
    public void Remove_switches_off_resets_choices_and_shrinks_lists()
    {
        var current = Shop(("eventBus", "rabbitMq"), ("languages", new JsonArray("en", "de", "fr")));

        var target = OptionChanges.Apply(current, ["chat", "eventBus", "languages=fr"], remove: true);

        var values = SpecOptions.Resolve(target);
        values["chat"].ShouldBe("false");
        values["eventBus"].ShouldBe("none");
        values["languages"].ShouldBe("en,de");
        target.Options.ShouldNotContainKey("eventBus");
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("eventBus=kafka")]
    [InlineData("eventBus")]
    [InlineData("languages=xx")]
    public void Unknown_options_and_values_are_refused(string word) =>
        Should.Throw<CliException>(() => OptionChanges.Apply(Shop(), [word], remove: false));

    [Fact]
    public void The_stored_spec_folds_branding_into_options_and_keeps_no_passwords()
    {
        var spec = SpecJson.Example();
        spec.Users.Add(new UserSpec { Email = "ada@shop.local", Password = "Secret1!x" });
        spec.Branding!.Logo = "assets/logo.svg";

        var stored = AppState.Stored(spec);

        stored.Branding.ShouldBeNull();
        stored.Users.ShouldAllBe(u => u.Password == null);
        stored.Options["theme"]!.GetValue<string>().ShouldBe("Forest");
        stored.Options["logo"]!.GetValue<string>().ShouldBe("logo.svg");
        stored.Options["chat"]!.GetValue<bool>().ShouldBeFalse();
        stored.Options.ShouldNotContainKey("reports");
        var original = SpecOptions.Resolve(spec);
        SpecOptions.Resolve(stored).Where(v => v.Key != "logo").ShouldAllBe(v => original[v.Key] == v.Value);
    }

    [Fact]
    public void Merge_adds_updates_merges_deletes_and_reports_conflicts_without_touching_changed_files()
    {
        using var folder = new TempFolder();
        string Root(string name) => Path.Combine(folder.Path, name);
        void Put(string root, string path, string text)
        {
            var file = Path.Combine(Root(root), path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        const string Program = "a\nb\nc\nd\ne\nf\n";
        Put("before", "Program.cs", Program);
        Put("after", "Program.cs", "a\nb\nc\nd\ne\nf\nreports\n");
        Put("app", "Program.cs", "mine\r\na\r\nb\r\nc\r\nd\r\ne\r\nf\r\n");
        Put("before", "Same.cs", "x\n");
        Put("after", "Same.cs", "y\n");
        Put("app", "Same.cs", "x\r\n");
        Put("before", "Clash.cs", "1\n2\n3\n");
        Put("after", "Clash.cs", "1\ntemplate\n3\n");
        Put("app", "Clash.cs", "1\nmine\n3\n");
        Put("after", "New.cs", "new\n");
        Put("before", "Old.cs", "old\n");
        Put("app", "Old.cs", "old\n");
        Put("before", "Kept.cs", "old\n");
        Put("app", "Kept.cs", "changed\n");
        Put("before", "Migrations/1.cs", "m\n");
        Put("after", "Migrations/1.cs", "n\n");
        Put("app", "Migrations/1.cs", "m\n");

        var plan = TemplateMerge.Plan(Root("before"), Root("after"), Root("app"), p => p.StartsWith("Migrations/", StringComparison.Ordinal));
        var rejects = TemplateMerge.Apply(Root("app"), plan);

        plan.Select(s => (s.Path, s.Action)).ShouldBe(
        [
            ("Clash.cs", MergeAction.Conflict), ("Kept.cs", MergeAction.Conflict), ("New.cs", MergeAction.Add),
            ("Old.cs", MergeAction.Delete), ("Program.cs", MergeAction.Merge), ("Same.cs", MergeAction.Update),
        ]);
        File.ReadAllText(Path.Combine(Root("app"), "Program.cs")).ShouldBe("mine\r\na\r\nb\r\nc\r\nd\r\ne\r\nf\r\nreports\r\n");
        File.ReadAllText(Path.Combine(Root("app"), "Same.cs")).ShouldBe("y\r\n");
        File.ReadAllText(Path.Combine(Root("app"), "Clash.cs")).ShouldBe("1\nmine\n3\n");
        File.ReadAllText(Path.Combine(Root("app"), "Clash.cs.rej")).ShouldContain("<<<<<<< app");
        File.Exists(Path.Combine(Root("app"), "Old.cs")).ShouldBeFalse();
        File.ReadAllText(Path.Combine(Root("app"), "Kept.cs")).ShouldBe("changed\n");
        File.ReadAllText(Path.Combine(Root("app"), "Migrations/1.cs")).ShouldBe("m\n");
        rejects.ShouldBe(["Clash.cs.rej"]);
    }
}

using System.Collections;
using Coworkee.Contracts.Mailing;
using Scriban.Runtime;

namespace Coworkee.Mailing.Tests;

public sealed class MailTemplateVariablesTests
{
    [Fact]
    public void Nested_objects_become_dotted_paths()
    {
        var variables = MailTemplateVariables.From(new { user = new { first_name = "Ada" }, confirm_url = "https://x" });

        variables.Select(v => v.Name).ShouldBe(["user.first_name", "confirm_url"]);
        variables[0].Sample.ShouldBe("Ada");
        variables[0].Type.ShouldBe("text");
    }

    [Fact]
    public void Pascal_case_members_are_snake_cased()
    {
        MailTemplateVariables.From(new { User = new { FirstName = "A" } }).Select(v => v.Name).ShouldBe(["user.first_name"]);
    }

    [Fact]
    public void Lists_expose_their_item_members()
    {
        var variables = MailTemplateVariables.From(new { notifications = new[] { new { title = "T", url = "u" } } });

        variables.Select(v => v.Name).ShouldBe(["notifications", "notifications[].title", "notifications[].url"]);
        variables[0].IsList.ShouldBeTrue();
        variables[0].Type.ShouldBe("list");
    }

    [Fact]
    public void Types_are_derived_from_the_values()
    {
        var variables = MailTemplateVariables.From(new { count = 3, at = DateTimeOffset.UnixEpoch, ok = true });

        variables.Select(v => v.Type).ShouldBe(["number", "date", "bool"]);
    }

    [Fact]
    public void Recipient_sample_has_the_documented_paths()
    {
        var names = MailTemplateVariables.From(MailRecipientModel.Sample).Select(v => v.Name);

        names.ShouldBe(
        [
            "user.first_name", "user.last_name", "user.display_name", "user.email", "user.culture",
            "app.name", "app.url", "organisation.name", "unsubscribe_url", "campaign.name",
        ],
        ignoreOrder: true);
    }

    [Fact]
    public void Indexers_are_not_variables()
    {
        MailTemplateVariables.From(new { map = new Indexed(), name = "x" }).Select(v => v.Name).ShouldBe(["map.label", "name"]);
    }

    [Fact]
    public void Script_objects_list_their_members()
    {
        var sample = new ScriptObject { ["user"] = new ScriptObject { ["first_name"] = "Ada" }, ["count"] = 3, ["items"] = new ScriptArray { new ScriptObject { ["title"] = "T" } } };

        var variables = MailTemplateVariables.From(sample);

        variables.Select(v => v.Name).ShouldBe(["user.first_name", "count", "items", "items[].title"]);
        variables.Single(v => v.Name == "count").Type.ShouldBe("number");
    }

    [Fact]
    public void Nesting_stops_at_the_maximum_depth()
    {
        var variables = MailTemplateVariables.From(new { a = new { b = new { c = new { d = new { e = "deep" } } } } });

        variables.Select(v => (v.Name, v.Type)).ShouldBe([("a.b.c.d", "object")]);
    }

    [Fact]
    public void A_null_campaign_still_lists_its_members()
    {
        var model = MailRecipientModel.For(new MailRecipient(Guid.NewGuid(), "a@x", "Ada", null, null), null, null, null, null, null);

        MailTemplateVariables.From(model).ShouldContain(v => v.Name == "campaign.name" && v.Type == "text" && v.Sample == null);
    }

    private sealed class Indexed
    {
        public string Label => "l";

        public string this[int index] => index.ToString();
    }

    [Fact]
    public void Display_name_falls_back_to_email()
    {
        var model = MailRecipientModel.For(new MailRecipient(Guid.NewGuid(), "a@x", null, null, null), null, null, null, null, null);

        MailTemplateVariables.From(model).Single(v => v.Name == "user.display_name").Sample.ShouldBe("a@x");
    }

    [Fact]
    public void Display_name_joins_first_and_last_name()
    {
        var model = MailRecipientModel.For(new MailRecipient(Guid.NewGuid(), "a@x", "Ada", "Lovelace", "en"), null, null, null, null, "Spring");

        var variables = MailTemplateVariables.From(model);

        variables.Single(v => v.Name == "user.display_name").Sample.ShouldBe("Ada Lovelace");
        variables.Single(v => v.Name == "campaign.name").Sample.ShouldBe("Spring");
    }
}

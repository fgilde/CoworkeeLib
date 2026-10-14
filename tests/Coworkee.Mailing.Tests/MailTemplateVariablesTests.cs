using Coworkee.Contracts.Mailing;

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

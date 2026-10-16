using Coworkee.Client.Blazor.Components.Mail;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailMentionHtmlTests
{
    private static readonly string[] Known = ["user.first_name"];

    [Fact]
    public void Round_trip_keeps_the_body()
    {
        const string body = "<p>Hi {{ user.first_name }}!</p>";

        var mentions = MailMentionHtml.ToMentions(body, Known);

        mentions.ShouldContain("class=\"mention\"");
        mentions.ShouldNotContain("{{");
        MailMentionHtml.ToScriban(mentions, Known).ShouldBe(body);
    }

    [Fact]
    public void Quill_markup_with_nested_spans_becomes_a_placeholder()
    {
        const string html = "<p>Hi <span class=\"mention\" data-denotation-char=\"{\" data-id=\"a1\" data-value=\"user.first_name\">\uFEFF<span contenteditable=\"false\"><span class=\"ql-mention-denotation-char\">{</span>user.first_name</span>\uFEFF</span>!</p>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe("<p>Hi {{ user.first_name }}!</p>");
    }

    [Fact]
    public void TipTap_markup_becomes_a_placeholder()
    {
        var html = $"<p>Hi <span class=\"mention\" data-__type=\"{MailPlaceholder.MentionType}\" data-id=\"a1\" data-value=\"user.first_name\" data-denotation-char=\"{{\" data-__data-json=\"{{&quot;name&quot;:&quot;user.first_name&quot;}}\">{{user.first_name</span>!</p>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe("<p>Hi {{ user.first_name }}!</p>");
    }

    // TipTap reads only span.mention[data-__type="<type>"] back as a mention, Quill keeps the attribute
    [Fact]
    public void Mentions_carry_the_type_TipTap_parses_them_by()
    {
        MailMentionHtml.ToMentions("{{ user.first_name }}", Known)
            .ShouldContain($"data-__type=\"{MailPlaceholder.MentionType}\"");
    }

    [Fact]
    public void Unknown_placeholders_stay_text()
    {
        MailMentionHtml.ToMentions("{{ foo }}", Known).ShouldBe("{{ foo }}");
    }

    [Fact]
    public void Placeholders_inside_tags_are_not_touched()
    {
        const string body = "<a href=\"{{ user.first_name }}\">x</a>";

        MailMentionHtml.ToMentions(body, Known).ShouldBe(body);
    }

    [Fact]
    public void Unknown_mention_values_become_plain_text()
    {
        var result = MailMentionHtml.ToScriban("<span class=\"mention\" data-value=\"x }}{{ evil\">x</span>", Known);

        result.ShouldNotContain("{{");
        result.ShouldContain("x");
    }

    [Fact]
    public void Unknown_mention_text_cannot_inject_placeholders()
    {
        var result = MailMentionHtml.ToScriban("<span class=\"mention\" data-value=\"nope\"><span contenteditable=\"false\">{{ evil }}</span></span>", Known);

        result.ShouldNotContain("{{");
        result.ShouldNotContain("<");
    }

    [Fact]
    public void A_data_class_attribute_is_not_a_mention()
    {
        const string html = "<span data-class=\"mention\" data-value=\"user.first_name\">hi</span>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe(html);
    }

    [Fact]
    public void An_unclosed_mention_span_does_not_swallow_the_rest()
    {
        MailMentionHtml.ToScriban("<p>Hi <span class=\"mention\" data-value=\"user.first_name\">x</p><p>Bye</p>", Known)
            .ShouldBe("<p>Hi {{ user.first_name }}x</p><p>Bye</p>");
        MailMentionHtml.ToScriban("<p>Hi <span class=\"mention\" data-value=\"nope\">x</p><p>Bye</p>", Known)
            .ShouldBe("<p>Hi x</p><p>Bye</p>");
    }

    [Fact]
    public void Other_spans_are_kept()
    {
        const string html = "<p><span style=\"color:red\">hi</span></p>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe(html);
    }

    [Theory]
    [InlineData("<p>{{ user.first_name }}</p>", true)]
    [InlineData("{{ for n in notifications }}…{{ end }}", false)]
    [InlineData("{{- if user }}x{{ end }}", false)]
    [InlineData("{{~ if user }}x{{ end }}", false)]
    [InlineData("<TABLE>", false)]
    public void Visual_mode_is_only_allowed_for_simple_bodies(string body, bool allowed) =>
        MailVisualMode.Allowed(body).ShouldBe(allowed);
}

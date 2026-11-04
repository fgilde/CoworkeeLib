using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.Components.Mail;

namespace Coworkee.Client.Blazor.Tests;

public sealed partial class MailProtectedChipTests
{
    private static readonly string[] Known = ["user.first_name", "user.email", "new_email", "export.name"];

    private const string Button = "<p style=\"margin:24px 0;\"><a href=\"{{ confirm_url }}\" style=\"background:#16140f;color:#f3efe6;padding:12px 20px;text-decoration:none;display:inline-block;\">Confirm email</a></p>";

    public static TheoryData<string> DefaultBodies() =>
        [.. Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Templates"), "*.body.scriban").Select(Path.GetFileName).Order()!];

    [Theory]
    [MemberData(nameof(DefaultBodies))]
    public void Every_default_template_round_trips_byte_exact(string file)
    {
        var body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", file));

        MailMentionHtml.ToScriban(MailMentionHtml.ToMentions(body, Known), Known).ShouldBe(body);
    }

    [Theory]
    [MemberData(nameof(DefaultBodies))]
    public void Every_default_template_but_the_layout_document_opens_visually(string file)
    {
        var body = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Templates", file));

        MailVisualMode.Allowed(body).ShouldBe(!file.StartsWith("Layout.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_default_templates_are_there() => DefaultBodies().Count.ShouldBeGreaterThan(100);

    [Fact]
    public void Each_logic_tag_becomes_its_own_chip()
    {
        var html = MailMentionHtml.ToMentions("<p>{{ if user.email }}a{{ else if x }}b{{~ end -}}</p>", Known);

        Values(html).ShouldBe(["if user.email", "else if x", "end"]);
        Chip().Replace(html, "#").ShouldBe("<p>#a#b#</p>");
    }

    [Fact]
    public void A_styled_button_becomes_a_chip_with_its_text()
    {
        var html = MailMentionHtml.ToMentions(Button, Known, k => $"[{k}]");

        Values(html).ShouldBe(["[Button]: Confirm email"]);
    }

    [Fact]
    public void A_table_becomes_one_chip()
    {
        const string body = "<p>a</p><table><tr><td>{{ for c in r }}{{ c }}{{ end }}</td></tr></table><p>b</p>";

        var html = MailMentionHtml.ToMentions(body, Known);

        Values(html).ShouldBe(["Table"]);
        html.ShouldStartWith("<p>a</p><span class=\"mention\"");
        html.ShouldEndWith("</span><p>b</p>");
    }

    [Fact]
    public void A_list_with_logic_between_its_items_becomes_one_chip()
    {
        var html = MailMentionHtml.ToMentions("<ul>\n{{ for n in notifications }}  <li>{{ n.title }}</li>\n{{ end }}</ul>", Known);

        Values(html).ShouldBe(["List"]);
    }

    [Fact]
    public void Simple_bodies_are_unchanged()
    {
        const string body = "<p>Hi {{ foo }}, <a href=\"{{ url }}\">x</a></p><ul><li>a</li></ul>";

        MailMentionHtml.ToMentions(body, Known).ShouldBe(body);
    }

    [Fact]
    public void Text_edited_around_the_chips_is_kept_and_the_fragments_stay_byte_exact()
    {
        var body = "<p>Hello {{ user.first_name }},</p>\n{{ if pending }}\n<p>Please confirm.</p>\n{{ else }}\n<p>Done.</p>\n{{ end }}\n" + Button + "\n";
        var html = MailMentionHtml.ToMentions(body, Known);

        // what Quill writes back: every top-level chip in a paragraph of its own, line breaks between blocks gone, the text edited
        var quill = Chip().Replace(html.Replace("\n", string.Empty), m => $"<p>{m.Value}</p>").Replace("Please confirm.", "Kindly confirm!");

        MailMentionHtml.ToScriban(quill, Known).ShouldBe(
            "<p>Hello {{ user.first_name }},</p>{{ if pending }}<p>Kindly confirm!</p>{{ else }}<p>Done.</p>{{ end }}" + Button);
    }

    [Fact]
    public void A_chip_next_to_text_keeps_its_paragraph()
    {
        var html = "<p>" + MailMentionHtml.ToMentions("{{ if x }}", Known) + "a</p>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe("<p>{{ if x }}a</p>");
    }

    [Fact]
    public void A_browser_serialised_chip_still_round_trips()
    {
        const string raw = "<td title='a \"b\" &amp; c'>{{ x }}</td>";
        var html = MailMentionHtml.ToMentions("<table><tr>" + raw + "</tr></table>", Known);

        // browsers write &quot; and &amp; in attributes but no numeric entities
        var serialised = html.Replace("&#39;", "'");

        MailMentionHtml.ToScriban(serialised, Known).ShouldBe("<table><tr>" + raw + "</tr></table>");
    }

    [Fact]
    public void A_picked_placeholder_with_its_own_data_stays_a_placeholder()
    {
        var html = $"<span class=\"mention\" data-__type=\"{MailPlaceholder.MentionType}\" data-value=\"user.email\" data-__data-json=\"{{&quot;name&quot;:&quot;user.email&quot;}}\">x</span>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe("{{ user.email }}");
    }

    [Fact]
    public void Malformed_chip_data_falls_back_to_the_mention_value()
    {
        var html = "<span class=\"mention\" data-value=\"user.email\" data-__data-json=\"{&quot;raw\">x</span>";

        MailMentionHtml.ToScriban(html, Known).ShouldBe("{{ user.email }}");
    }

    private static string[] Values(string html) =>
        [.. ChipValue().Matches(html).Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))];

    [GeneratedRegex("""<span class="mention"[^>]*data-__data-json[^>]*><span[^>]*><span[^>]*>[^<]*</span>[^<]*</span></span>""")]
    private static partial Regex Chip();

    [GeneratedRegex("""<span class="mention"[^>]*\bdata-value="([^"]*)"[^>]*data-__data-json""")]
    private static partial Regex ChipValue();
}

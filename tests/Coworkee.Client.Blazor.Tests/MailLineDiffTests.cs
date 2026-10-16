using Coworkee.Client.Blazor.Components.Mail;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailLineDiffTests
{
    [Fact]
    public void Html_on_one_line_is_compared_per_tag()
    {
        var diff = MailLineDiff.Compute("<p>a</p><p>b</p><p>c</p>", "<p>a</p><p>B</p><p>c</p><p>d</p>");

        diff.Select(l => $"{l.Kind}:{l.Text}").ShouldBe(["Same:<p>a</p>", "Removed:<p>b</p>", "Added:<p>B</p>", "Same:<p>c</p>", "Added:<p>d</p>"]);
    }

    [Fact]
    public void Empty_texts_give_only_additions_or_removals()
    {
        MailLineDiff.Compute("", "x\ny").ShouldAllBe(l => l.Kind == MailDiffKind.Added);
        MailLineDiff.Compute("x", "").ShouldHaveSingleItem().Kind.ShouldBe(MailDiffKind.Removed);
    }
}

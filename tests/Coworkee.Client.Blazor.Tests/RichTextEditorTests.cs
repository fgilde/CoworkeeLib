using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using MudExRichTextEditor;
using MudExRichTextEditor.Types;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class RichTextEditorTests : ClientTestBase
{
    [Fact]
    public void Without_tools_the_editor_shows_its_default_toolbar()
    {
        var cut = Render<RichTextEditor>(p => p.Add(e => e.Value, "<p>Hi</p>"));

        cut.FindComponent<MudExTiptapEdit>().Instance.Tools.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    [Fact]
    public void Given_tools_replace_the_default_toolbar()
    {
        var cut = Render<RichTextEditor>(p => p.Add(e => e.Tools, [QuillTools.Bold()]));

        cut.FindComponent<MudExTiptapEdit>().Instance.Tools.ShouldHaveSingleItem().Class.ShouldBe("ql-bold");
    }
}

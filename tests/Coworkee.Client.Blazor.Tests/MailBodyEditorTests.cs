using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Components.Mail;
using Coworkee.Contracts.Mailing;
using MudExRichTextEditor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class MailBodyEditorTests : ClientTestBase
{
    private static readonly MailVariableDto[] Variables =
    [
        new("user.first_name", "string", "Ada", false),
        new("notifications", "list", null, true),
        new("notifications[].title", "string", "Hi", false),
    ];

    private IRenderedComponent<MailBodyEditor> Render(string value, Action<string>? changed = null) =>
        Render<MailBodyEditor>(p => p
            .Add(e => e.Value, value)
            .Add(e => e.Variables, Variables)
            .Add(e => e.ValueChanged, v => changed?.Invoke(v)));

    [Fact]
    public void Source_mode_offers_variables_as_completions()
    {
        var cut = Render("{{ for x in y }}{{ end }}");

        var editor = cut.FindComponent<CodeEditor>().Instance;
        editor.CompletionTrigger.ShouldBe("{{");
        editor.Completions.ShouldNotBeNull().ShouldContain(new CodeCompletion("user.first_name", " user.first_name }}", "string"));
        editor.Completions.ShouldNotContain(c => c.Label == "notifications");
        cut.Find("[data-testid='mail-mode-visual']").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("This template uses layout or logic the visual editor would change");
    }

    [Fact]
    public void Simple_bodies_open_in_visual_mode()
    {
        var cut = Render("<p>Hi</p>");

        cut.FindComponents<MudExRichTextEdit>().Count.ShouldBe(1);
        cut.FindComponents<CodeEditor>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Switching_between_modes_keeps_the_body()
    {
        var cut = Render("<p>Hi {{ user.first_name }}</p>");

        await cut.Find("[data-testid='mail-mode-source']").ClickAsync(new());
        cut.FindComponent<CodeEditor>().Instance.Value.ShouldBe("<p>Hi {{ user.first_name }}</p>");

        await cut.Find("[data-testid='mail-mode-visual']").ClickAsync(new());
        cut.FindComponents<MudExRichTextEdit>().Count.ShouldBe(1);
    }

    [Fact]
    public async Task Picking_a_variable_in_source_mode_inserts_it()
    {
        string? received = null;
        var cut = Render("<p>Hi </p>", v => received = v);
        await cut.Find("[data-testid='mail-mode-source']").ClickAsync(new());

        await cut.Find("[data-testid='mail-variable-user.first_name']").ClickAsync(new());

        received.ShouldNotBeNull().ShouldContain("{{ user.first_name }}");
    }

    [Fact]
    public async Task Picking_a_list_variable_inserts_a_loop_skeleton()
    {
        string? received = null;
        var cut = Render("{{ for x in y }}{{ end }}", v => received = v);

        await cut.Find("[data-testid='mail-variable-notifications']").ClickAsync(new());

        received.ShouldNotBeNull().ShouldContain("{{ for item in notifications }}\n{{ end }}");
    }

    [Fact]
    public async Task Picking_a_list_variable_in_visual_mode_switches_to_the_source()
    {
        string? received = null;
        var cut = Render("<p>Hi</p>", v => received = v);
        cut.FindComponents<MudExRichTextEdit>().Count.ShouldBe(1);

        await cut.Find("[data-testid='mail-variable-notifications']").ClickAsync(new());

        cut.FindComponents<MudExRichTextEdit>().ShouldBeEmpty();
        cut.FindComponent<CodeEditor>().Instance.Value.ShouldBe("<p>Hi</p>{{ for item in notifications }}\n{{ end }}");
        received.ShouldBe("<p>Hi</p>{{ for item in notifications }}\n{{ end }}");
    }

    [Fact]
    public void The_visual_editor_widens_the_characters_the_mention_list_accepts()
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        module.Mode = JSRuntimeMode.Loose;

        var cut = Render("<p>Hi</p>");

        cut.WaitForAssertion(() => module.VerifyInvoke("widenMentionChars"));
    }

    [Fact]
    public async Task Members_of_list_items_are_written_for_the_loop_variable()
    {
        string? received = null;
        var cut = Render("{{ for x in y }}{{ end }}", v => received = v);

        await cut.Find("[data-testid='mail-variable-notifications[].title']").ClickAsync(new());

        received.ShouldNotBeNull().ShouldContain("{{ item.title }}");
        cut.FindComponent<CodeEditor>().Instance.Completions.ShouldNotBeNull().ShouldContain(new CodeCompletion("notifications[].title", " item.title }}", "string"));
    }
}
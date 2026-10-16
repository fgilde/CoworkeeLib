using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Coworkee.Client.Blazor.Components.Mail;
using Coworkee.Contracts.Mailing;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using MudExRichTextEditor;
using MudExRichTextEditor.Extensibility;
using NSubstitute;

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

        cut.FindComponents<RichTextEditor>().Count.ShouldBe(1);
        cut.FindComponents<CodeEditor>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Switching_between_modes_keeps_the_body()
    {
        var cut = Render("<p>Hi {{ user.first_name }}</p>");

        await cut.Find("[data-testid='mail-mode-source']").ClickAsync(new());
        cut.FindComponent<CodeEditor>().Instance.Value.ShouldBe("<p>Hi {{ user.first_name }}</p>");

        await cut.Find("[data-testid='mail-mode-visual']").ClickAsync(new());
        cut.FindComponents<RichTextEditor>().Count.ShouldBe(1);
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
        cut.FindComponents<RichTextEditor>().Count.ShouldBe(1);

        await cut.Find("[data-testid='mail-variable-notifications']").ClickAsync(new());

        cut.FindComponents<RichTextEditor>().ShouldBeEmpty();
        cut.FindComponent<CodeEditor>().Instance.Value.ShouldBe("<p>Hi</p>{{ for item in notifications }}\n{{ end }}");
        received.ShouldBe("<p>Hi</p>{{ for item in notifications }}\n{{ end }}");
    }

    [Fact]
    public void TipTap_is_the_default_engine_with_the_placeholder_mentions()
    {
        var cut = Render("<p>Hi</p>");

        var tiptap = cut.FindComponent<MudExTiptapEdit>().Instance;
        tiptap.Modules.ShouldHaveSingleItem().ShouldBeOfType<MentionModule<MailPlaceholder>>();
        cut.FindComponents<MudExRichTextEdit>().ShouldBeEmpty();
    }

    [Fact]
    public async Task The_setting_switches_to_Quill_with_the_same_mentions()
    {
        await UseRichTextEditorAsync(EditorSettings.Quill);

        var cut = Render("<p>Hi</p>");

        var quill = cut.FindComponent<MudExRichTextEdit>().Instance;
        quill.Modules.ShouldHaveSingleItem().ShouldBeOfType<MentionModule<MailPlaceholder>>();
        cut.FindComponents<MudExTiptapEdit>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Mentions_search_the_placeholders_with_both_engines()
    {
        foreach (var engine in new[] { EditorSettings.TipTap, EditorSettings.Quill })
        {
            await UseRichTextEditorAsync(engine);
            var cut = Render("<p>Hi</p>");
            var module = (MentionModule<MailPlaceholder>)cut.FindComponent<RichTextEditor>().Instance.Modules.Single();

            var found = await module.GetSuggestions('{', "first");

            found.Select(m => m.Value).ShouldBe(["user.first_name"]);
            found.Single().Data.ShouldBe(new MailPlaceholder("user.first_name"));
        }
    }

    [Fact]
    public async Task Picking_a_variable_in_visual_mode_inserts_a_mention_TipTap_parses()
    {
        var cut = Render("<p>Hi</p>");

        await cut.Find("[data-testid='mail-variable-user.first_name']").ClickAsync(new());

        cut.WaitForAssertion(() => JSInterop.Invocations.ShouldContain(i => i.Arguments.OfType<string>().Any(a => a.Contains($"data-__type=\"{MailPlaceholder.MentionType}\""))));
    }

    [Fact]
    public async Task The_visual_editor_widens_the_characters_the_Quill_mention_list_accepts()
    {
        await UseRichTextEditorAsync(EditorSettings.Quill);
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

    private Task UseRichTextEditorAsync(string engine)
    {
        Api.GetClientSettingsAsync(default).ReturnsForAnyArgs(new Dictionary<string, string?> { [EditorSettings.RichTextEditor] = engine });
        return Services.GetRequiredService<Theming.ThemeService>().LoadAsync();
    }
}

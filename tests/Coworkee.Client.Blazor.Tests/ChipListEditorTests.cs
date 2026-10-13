using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChipListEditorTests : ClientTestBase
{
    private static readonly string[] Standard = ["openid", "profile", "email"];

    private IReadOnlyList<string> _value = [];
    private int _changes;

    private IRenderedComponent<ChipListEditor> Open(
        IReadOnlyList<string>? value = null,
        Func<string, string?>? validate = null,
        Func<string, CancellationToken, Task<IEnumerable<string>>>? suggestions = null,
        IReadOnlyCollection<string>? fixedValues = null)
    {
        _value = value ?? [];
        Render<MudPopoverProvider>();
        return Render<ChipListEditor>(p => p
            .Add(e => e.Value, _value)
            .Add(e => e.ValueChanged, v => { _value = v; _changes++; })
            .Add(e => e.Validate, validate)
            .Add(e => e.Suggestions, suggestions)
            .Add(e => e.Fixed, fixedValues));
    }

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<ChipListEditor> editor) => editor.Find("input");

    private static Task EnterAsync(IRenderedComponent<ChipListEditor> editor) => Input(editor).KeyUpAsync(new KeyboardEventArgs { Key = "Enter" });

    [Fact]
    public async Task Enter_adds_a_chip()
    {
        var editor = Open();

        Input(editor).Input("profile");
        await EnterAsync(editor);

        _value.ShouldBe(["profile"]);
        editor.Find("[data-chip='profile']");
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Pasted_list_becomes_several_chips()
    {
        var editor = Open();

        Input(editor).Input("openid profile\nemail,phone");

        _value.ShouldBe(["openid", "profile", "email", "phone"]);
        editor.FindAll("[data-chip]").Count.ShouldBe(4);
    }

    [Fact]
    public void Duplicates_are_ignored()
    {
        var editor = Open(["a"]);

        Input(editor).Input("a b a B ");

        _value.ShouldBe(["a", "b", "B"]);
    }

    [Fact]
    public async Task Invalid_text_shows_the_error_and_adds_nothing()
    {
        var editor = Open(validate: v => Uri.IsWellFormedUriString(v, UriKind.Absolute) ? null : "Not an absolute address");

        Input(editor).Input("/callback");
        await EnterAsync(editor);

        _changes.ShouldBe(0);
        editor.Markup.ShouldContain("Not an absolute address");
        editor.FindAll("[data-chip]").ShouldBeEmpty();
        Input(editor).GetAttribute("value").ShouldBe("/callback");
    }

    [Fact]
    public void Valid_tokens_of_a_mixed_paste_are_added_and_the_invalid_ones_stay_in_the_field()
    {
        var editor = Open(validate: v => v.Contains('!') ? "No special characters" : null);

        Input(editor).Input("ok bad! fine");

        _value.ShouldBe(["ok", "fine"]);
        Input(editor).GetAttribute("value").ShouldBe("bad!");
        editor.Markup.ShouldContain("No special characters");
    }

    [Fact]
    public void Stored_invalid_values_are_shown_not_dropped()
    {
        var editor = Open(["relative/path", "https://a.example/cb"], v => v.StartsWith("https:") ? null : "Not an absolute address");

        editor.Find("[data-chip='relative/path']").ClassList.ShouldContain("mud-chip-color-error");
        editor.Find("[data-chip='https://a.example/cb']").ClassList.ShouldNotContain("mud-chip-color-error");
        _changes.ShouldBe(0);
    }

    [Fact]
    public async Task Closing_a_chip_removes_it()
    {
        var editor = Open(["a", "b"]);

        await editor.Find("[data-chip='a'] .mud-chip-close-button").ClickAsync(new());

        _value.ShouldBe(["b"]);
        editor.FindAll("[data-chip='a']").ShouldBeEmpty();
    }

    [Fact]
    public void Fixed_chips_cannot_be_closed()
    {
        var editor = Open(["openid", "profile"], fixedValues: ["openid"]);

        editor.FindAll("[data-chip='openid'] .mud-chip-close-button").ShouldBeEmpty();
        editor.FindAll("[data-chip='profile'] .mud-chip-close-button").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Suggestions_exclude_chosen_values()
    {
        var editor = Open(["profile"], suggestions: (text, _) => Task.FromResult(Standard.Where(s => s.Contains(text)).AsEnumerable()));
        var search = editor.FindComponent<MudAutocomplete<string>>();

        (await search.Instance.SearchFunc!("o", CancellationToken.None)!).ShouldBe(["openid"]);
        (await search.Instance.SearchFunc!(string.Empty, CancellationToken.None)!).ShouldBe(["openid", "email"]);
    }

    [Fact]
    public async Task Picking_a_suggestion_adds_it_and_clears_the_field()
    {
        var editor = Open(suggestions: (text, _) => Task.FromResult(Standard.Where(s => s.Contains(text)).AsEnumerable()));
        var search = editor.FindComponent<MudAutocomplete<string>>();

        Input(editor).Input("pro");
        await editor.InvokeAsync(() => search.Instance.SelectOptionAsync("profile"));

        _value.ShouldBe(["profile"]);
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Leaving_the_field_keeps_typed_text()
    {
        var editor = Open();

        Input(editor).Input("custom.scope");
        Input(editor).Blur();

        _value.ShouldBe(["custom.scope"]);
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Leaving_the_field_with_invalid_text_adds_nothing()
    {
        var editor = Open(validate: v => v.Contains('/') ? "No slashes" : null);

        Input(editor).Input("a/b");
        Input(editor).Blur();

        _changes.ShouldBe(0);
        editor.Markup.ShouldContain("No slashes");
    }
}

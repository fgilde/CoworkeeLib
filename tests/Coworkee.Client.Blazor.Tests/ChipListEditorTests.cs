using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class ChipListEditorTests : ClientTestBase
{
    private static readonly string[] Standard = ["openid", "profile", "email"];

    private IReadOnlyList<string> _value = [];
    private int _changes;
    private IRenderedComponent<MudPopoverProvider> _popovers = null!;

    private IRenderedComponent<ChipListEditor> Open(
        IReadOnlyList<string>? value = null,
        Func<string, string?>? validate = null,
        Func<string, CancellationToken, Task<IEnumerable<string>>>? suggestions = null,
        IReadOnlyCollection<string>? fixedValues = null)
    {
        _value = value ?? [];
        // the tests open the list themselves; the autocomplete's own debounced search must not reopen it in the middle of a pick on a busy machine
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        _popovers = Render<MudPopoverProvider>();
        return Render<ChipListEditor>(p => p
            .Add(e => e.Value, _value)
            .Add(e => e.ValueChanged, v => { _value = v; _changes++; })
            .Add(e => e.Validate, validate)
            .Add(e => e.Suggestions, suggestions)
            .Add(e => e.Fixed, fixedValues));
    }

    private static AngleSharp.Dom.IElement Input(IRenderedComponent<ChipListEditor> editor) => editor.Find("input");

    private static Task TypeAsync(IRenderedComponent<ChipListEditor> editor, string text) => Input(editor).InputAsync(new ChangeEventArgs { Value = text });

    private static Task BlurAsync(IRenderedComponent<ChipListEditor> editor) => Input(editor).BlurAsync(new FocusEventArgs());

    private static Task EnterAsync(IRenderedComponent<ChipListEditor> editor) => Input(editor).KeyUpAsync(new KeyboardEventArgs { Key = "Enter" });

    [Fact]
    public async Task Enter_adds_a_chip()
    {
        var editor = Open();

        await TypeAsync(editor, "profile");
        await EnterAsync(editor);

        _value.ShouldBe(["profile"]);
        editor.Find("[data-chip='profile']");
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Pasted_list_becomes_several_chips()
    {
        var editor = Open();

        await TypeAsync(editor, "openid profile\nemail,phone");

        _value.ShouldBe(["openid", "profile", "email", "phone"]);
        editor.FindAll("[data-chip]").Count.ShouldBe(4);
    }

    [Fact]
    public async Task Duplicates_are_ignored()
    {
        var editor = Open(["a"]);

        await TypeAsync(editor, "a b a B ");

        _value.ShouldBe(["a", "b", "B"]);
    }

    [Fact]
    public async Task Invalid_text_shows_the_error_and_adds_nothing()
    {
        var editor = Open(validate: v => Uri.IsWellFormedUriString(v, UriKind.Absolute) ? null : "Not an absolute address");

        await TypeAsync(editor, "/callback");
        await EnterAsync(editor);

        _changes.ShouldBe(0);
        editor.Markup.ShouldContain("Not an absolute address");
        editor.FindAll("[data-chip]").ShouldBeEmpty();
        Input(editor).GetAttribute("value").ShouldBe("/callback");
    }

    [Fact]
    public async Task Valid_tokens_of_a_mixed_paste_are_added_and_the_invalid_ones_stay_in_the_field()
    {
        var editor = Open(validate: v => v.Contains('!') ? "No special characters" : null);

        await TypeAsync(editor, "ok bad! fine");

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

        await TypeAsync(editor, "pro");
        await editor.InvokeAsync(() => search.Instance.SelectOptionAsync("profile"));

        _value.ShouldBe(["profile"]);
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Leaving_the_field_keeps_typed_text()
    {
        var editor = Open();

        await TypeAsync(editor, "custom.scope");
        await BlurAsync(editor);

        _value.ShouldBe(["custom.scope"]);
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Leaving_the_field_with_invalid_text_adds_nothing()
    {
        var editor = Open(validate: v => v.Contains('/') ? "No slashes" : null);

        await TypeAsync(editor, "a/b");
        await BlurAsync(editor);

        _changes.ShouldBe(0);
        editor.Markup.ShouldContain("No slashes");
    }

    private static Task<IEnumerable<string>> Offer(string text, CancellationToken _) => Task.FromResult(Standard.Where(s => s.Contains(text)).AsEnumerable());

    private async Task<MudAutocomplete<string>> OpenListAsync(IRenderedComponent<ChipListEditor> editor, string text)
    {
        var search = editor.FindComponent<MudAutocomplete<string>>().Instance;
        await TypeAsync(editor, text);
        await editor.InvokeAsync(search.OpenMenuAsync);
        search.Open.ShouldBeTrue();
        return search;
    }

    [Fact]
    public async Task Blur_with_the_list_open_waits_and_the_closing_click_outside_takes_the_text()
    {
        var editor = Open(suggestions: Offer);
        await OpenListAsync(editor, "pro");

        await BlurAsync(editor);
        _changes.ShouldBe(0);

        await _popovers.Find(".mud-overlay").ClickAsync(new());

        _value.ShouldBe(["pro"]);
        _changes.ShouldBe(1);
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Picking_a_suggestion_while_text_is_typed_adds_only_the_suggestion()
    {
        var editor = Open(suggestions: Offer);
        var search = await OpenListAsync(editor, "pro");

        await BlurAsync(editor); // the mousedown on the list item blurs the field before the click
        await editor.InvokeAsync(() => search.SelectOptionAsync("profile"));

        _value.ShouldBe(["profile"]);
        _changes.ShouldBe(1);
        search.Open.ShouldBeFalse();
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Enter_on_a_highlighted_suggestion_adds_only_the_suggestion()
    {
        var editor = Open(suggestions: Offer);
        await OpenListAsync(editor, "pro");

        await EnterAsync(editor);

        _value.ShouldBe(["profile"]);
        _changes.ShouldBe(1);
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Escape_discards_the_typed_text()
    {
        var editor = Open(suggestions: Offer);
        await OpenListAsync(editor, "pro");

        await Input(editor).KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        await Input(editor).KeyUpAsync(new KeyboardEventArgs { Key = "Escape" });

        _changes.ShouldBe(0);
        editor.FindAll("[data-chip]").ShouldBeEmpty();
        Input(editor).GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Rejected_tokens_are_not_repeated_in_the_field()
    {
        var editor = Open(validate: v => v.Contains('!') ? "No special characters" : null);

        await TypeAsync(editor, "bad! bad! ok");

        _value.ShouldBe(["ok"]);
        Input(editor).GetAttribute("value").ShouldBe("bad!");
    }

    [Fact]
    public async Task Typing_on_after_the_list_closed_for_lack_of_matches_keeps_the_text()
    {
        var editor = Open(suggestions: Offer);
        var search = await OpenListAsync(editor, "pr");

        await TypeAsync(editor, "prx");
        await editor.InvokeAsync(search.OpenMenuAsync);

        search.Open.ShouldBeFalse();
        _changes.ShouldBe(0);
        Input(editor).GetAttribute("value").ShouldBe("prx");

        await TypeAsync(editor, "prxy");
        await EnterAsync(editor);
        _value.ShouldBe(["prxy"]);
    }

    [Fact]
    public async Task Text_whose_only_match_is_already_chosen_is_not_committed_while_typing()
    {
        var editor = Open(["profile"], suggestions: Offer);
        var search = await OpenListAsync(editor, "p");

        await TypeAsync(editor, "pro");
        await editor.InvokeAsync(search.OpenMenuAsync);

        search.Open.ShouldBeFalse();
        _value.ShouldBe(["profile"]);
        _changes.ShouldBe(0);
        Input(editor).GetAttribute("value").ShouldBe("pro");
    }

    [Fact]
    public async Task Leaving_the_field_after_the_list_closed_for_lack_of_matches_keeps_the_text()
    {
        var editor = Open(suggestions: Offer);
        var search = await OpenListAsync(editor, "pr");
        await TypeAsync(editor, "prx");
        await editor.InvokeAsync(search.OpenMenuAsync);

        await BlurAsync(editor);

        _value.ShouldBe(["prx"]);
    }

    [Fact]
    public async Task Tab_commits_the_typed_text()
    {
        var editor = Open(suggestions: Offer);
        await OpenListAsync(editor, "pro");

        await Input(editor).KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });
        await BlurAsync(editor);

        _value.ShouldBe(["pro"]);
    }
}
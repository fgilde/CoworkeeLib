using Bunit;
using Coworkee.Client.Blazor.Components.Editors;
using Microsoft.JSInterop;
using Shouldly;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CodeEditorTests : ClientTestBase
{
    [Fact]
    public void Renders_the_label_and_a_fullscreen_button()
    {
        var cut = Render<CodeEditor>(p => p.Add(e => e.Label, "CSS"));

        cut.Markup.ShouldContain("CSS");
        cut.FindAll("[data-testid='code-editor-fullscreen']").Count.ShouldBe(1);
    }

    [Fact]
    public void Fullscreen_button_can_be_switched_off()
    {
        var cut = Render<CodeEditor>(p => p.Add(e => e.AllowFullscreen, false));

        cut.FindAll("[data-testid='code-editor-fullscreen']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_monaco_the_value_still_flows()
    {
        string? received = null;
        var cut = Render<CodeEditor>(p => p
            .Add(e => e.Value, "a{}")
            .Add(e => e.ValueChanged, v => received = v));

        cut.Markup.ShouldContain("a{}");
        await cut.InvokeAsync(() => cut.Instance.SetFromEditorAsync("b{}"));

        received.ShouldBe("b{}");
    }

    [Fact]
    public async Task Language_and_read_only_changes_reach_the_created_editor()
    {
        var cut = await RenderCreatedAsync(p => p.Add(e => e.Value, "a{}").Add(e => e.Language, "html"));

        cut.Render(p => p.Add(e => e.Language, "css").Add(e => e.ReadOnly, true));

        JSInterop.Invocations.ShouldContain(call => call.Identifier == "blazorMonaco.editor.setModelLanguage" && (string)call.Arguments[1]! == "css");
        JSInterop.Invocations.ShouldContain(call => call.Identifier == "blazorMonaco.editor.updateOptions" && call.Arguments[1]!.ToString()!.Contains("\"readOnly\":true"));
    }

    [Fact]
    public async Task Unchanged_language_and_read_only_are_not_sent_again()
    {
        var cut = await RenderCreatedAsync(p => p.Add(e => e.Language, "html"));

        cut.Render(p => p.Add(e => e.Value, "b{}"));

        JSInterop.Invocations.ShouldNotContain(call => call.Identifier == "blazorMonaco.editor.setModelLanguage" || call.Identifier == "blazorMonaco.editor.updateOptions");
    }

    [Fact]
    public async Task A_language_change_registers_the_completions_for_the_new_language()
    {
        var cut = await RenderCreatedAsync(p => p
            .Add(e => e.Language, "html")
            .Add(e => e.CompletionTrigger, "{{")
            .Add(e => e.Completions, [new CodeCompletion("a", "a")]));
        Registered("html").ShouldBe(1);

        cut.Render(p => p.Add(e => e.Language, "css"));

        Registered("css").ShouldBe(1);
        Registered("html").ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_registration_is_retried_instead_of_being_remembered()
    {
        var js = new FakeJs { Fail = true };
        await Should.ThrowAsync<JSException>(() => CodeCompletionRegistry.AddAsync(js, "html", "{", "model-1", () => null));

        js.Fail = false;
        await CodeCompletionRegistry.AddAsync(js, "html", "{", "model-1", () => null);
        await CodeCompletionRegistry.AddAsync(js, "html", "{", "model-2", () => null);

        js.Calls.ShouldBe(2);
    }

    private int Registered(string language) => JSInterop.Invocations.Count(call =>
        call.Identifier.EndsWith("registerCompletionItemProvider") && System.Text.Json.JsonSerializer.Serialize(call.Arguments[0]).Contains($"\"{language}\""));

    private async Task<IRenderedComponent<CodeEditor>> RenderCreatedAsync(Action<ComponentParameterCollectionBuilder<CodeEditor>> parameters)
    {
        var module = JSInterop.SetupModule("./_content/Coworkee.Client.Blazor/coworkee.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<bool>("loadMonaco").SetResult(true);
        JSInterop.Setup<BlazorMonaco.Editor.TextModel>(call => call.Identifier.EndsWith("getInstanceModel")).SetResult(new() { Id = "model-1", Uri = "inmemory://model/1" });
        var cut = Render<CodeEditor>(parameters);
        cut.WaitForAssertion(() => cut.FindAll("[data-testid='code-editor-fallback']").ShouldBeEmpty());
        await cut.InvokeAsync(() => cut.Instance.InitAsync());
        return cut;
    }

    private sealed class FakeJs : IJSRuntime
    {
        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls++;
            return Fail ? throw new JSException("registration failed") : default;
        }

        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<T>(identifier, args);
    }

    [Fact]
    public async Task Without_monaco_insert_appends_to_the_value()
    {
        string? received = null;
        var cut = Render<CodeEditor>(p => p
            .Add(e => e.Value, "a")
            .Add(e => e.ValueChanged, v => received = v));

        await cut.InvokeAsync(() => cut.Instance.InsertAsync("{{ Name }}"));

        received.ShouldBe("a{{ Name }}");
    }
}

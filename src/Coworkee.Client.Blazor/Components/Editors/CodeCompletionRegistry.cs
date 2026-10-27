using System.Runtime.CompilerServices;
using BlazorMonaco;
using BlazorMonaco.Languages;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// Monaco providers cannot be removed again, so one provider per language and trigger character is registered for the browser
/// (the JS runtime) and answers with the suggestions of the editor whose model it is asked about.
/// </summary>
internal static class CodeCompletionRegistry
{
    private static readonly ConditionalWeakTable<IJSRuntime, State> States = [];

    /// <param name="trigger">The whole trigger (<c>{{</c>); Monaco opens the list at its last character, Ctrl+Space anywhere.</param>
    public static async Task AddAsync(IJSRuntime js, string language, string trigger, string modelUri, Func<IReadOnlyList<CodeCompletion>?> completions)
    {
        var state = States.GetOrCreateValue(js);
        var key = Key(language, trigger);
        bool register;
        lock (state)
        {
            state.Editors[(key, modelUri)] = (completions, trigger);
            register = state.Providers.Add(key);
        }

        if (register)
        {
            var provider = new CompletionItemProvider(
                [trigger[^1..]],
                (uri, position, _) => SuggestAsync(js, state, key, uri, position),
                item => Task.FromResult(item));
            try
            {
                await BlazorMonaco.Languages.Global.RegisterCompletionItemProvider(js, language, provider);
            }
            catch
            {
                lock (state)
                {
                    state.Providers.Remove(key);
                    state.Editors.Remove((key, modelUri));
                }

                throw;
            }
        }
    }

    public static void Remove(IJSRuntime js, string language, string trigger, string modelUri)
    {
        if (States.TryGetValue(js, out var state))
        {
            lock (state)
            {
                state.Editors.Remove((Key(language, trigger), modelUri));
            }
        }
    }

    private static string Key(string language, string trigger) => $"{language}\n{trigger[^1..]}";

    private static async Task<CompletionList> SuggestAsync(IJSRuntime js, State state, string key, string modelUri, Position position)
    {
        (Func<IReadOnlyList<CodeCompletion>?> Completions, string Trigger) editor;
        lock (state)
        {
            if (!state.Editors.TryGetValue((key, modelUri), out editor))
            {
                return new CompletionList { Suggestions = [] };
            }
        }

        var model = await BlazorMonaco.Editor.Global.GetModel(js, modelUri);
        var line = await model.GetLineContent(position.LineNumber);
        return new CompletionList
        {
            Suggestions = [.. (editor.Completions() ?? []).Select((c, i) =>
            {
                var (start, end, filter) = CodeCompletionEdit.For(line, position.Column, editor.Trigger, c);
                return new CompletionItem
                {
                    LabelAsString = c.Label,
                    // in the given order, not alphabetical
                    SortText = i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
                    InsertText = c.InsertText,
                    InsertTextRules = c.Snippet ? CompletionItemInsertTextRule.InsertAsSnippet : null,
                    FilterText = filter,
                    RangeAsObject = new BlazorMonaco.Range(position.LineNumber, start, position.LineNumber, end),
                    Detail = c.Detail,
                    Kind = CompletionItemKind.Variable,
                };
            })],
        };
    }

    private sealed class State
    {
        public HashSet<string> Providers { get; } = [];

        public Dictionary<(string Key, string ModelUri), (Func<IReadOnlyList<CodeCompletion>?> Completions, string Trigger)> Editors { get; } = [];
    }
}

using System.Runtime.CompilerServices;
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

    public static async Task AddAsync(IJSRuntime js, string language, string trigger, string modelUri, Func<IReadOnlyList<CodeCompletion>?> completions)
    {
        var state = States.GetOrCreateValue(js);
        var key = $"{language}\n{trigger}";
        bool register;
        lock (state)
        {
            state.Editors[(key, modelUri)] = completions;
            register = state.Providers.Add(key);
        }

        if (register)
        {
            var provider = new CompletionItemProvider(
                [trigger],
                (uri, _, _) => Task.FromResult(Suggest(state, key, uri)),
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
                state.Editors.Remove(($"{language}\n{trigger}", modelUri));
            }
        }
    }

    private static CompletionList Suggest(State state, string key, string modelUri)
    {
        Func<IReadOnlyList<CodeCompletion>?>? completions;
        lock (state)
        {
            state.Editors.TryGetValue((key, modelUri), out completions);
        }

        return new CompletionList
        {
            Suggestions = [.. (completions?.Invoke() ?? []).Select(c => new CompletionItem
            {
                LabelAsString = c.Label,
                InsertText = c.InsertText,
                Detail = c.Detail,
                Kind = CompletionItemKind.Variable,
            })],
        };
    }

    private sealed class State
    {
        public HashSet<string> Providers { get; } = [];

        public Dictionary<(string Key, string ModelUri), Func<IReadOnlyList<CodeCompletion>?>> Editors { get; } = [];
    }
}

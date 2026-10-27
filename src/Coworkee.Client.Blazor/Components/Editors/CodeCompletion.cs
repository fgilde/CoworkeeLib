namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>
/// A suggestion in <see cref="CodeEditor"/>. <paramref name="InsertText"/> is the whole text, trigger included (<c>{{ name }}</c>):
/// it replaces what was typed of the trigger, the word before the cursor and the closing brackets Monaco added on its own.
/// With <paramref name="Snippet"/> it is a Monaco snippet (<c>{{ if $1 }}</c> puts the cursor at <c>$1</c>).
/// </summary>
public sealed record CodeCompletion(string Label, string InsertText, string? Detail = null, bool Snippet = false);

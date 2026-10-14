namespace Coworkee.Client.Blazor.Components.Editors;

/// <summary>A suggestion in <see cref="CodeEditor"/>; the text replaces the word being typed, so after a trigger like <c>{{</c> leave the trigger out of <paramref name="InsertText"/>.</summary>
public sealed record CodeCompletion(string Label, string InsertText, string? Detail = null);

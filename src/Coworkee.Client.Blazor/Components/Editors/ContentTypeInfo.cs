namespace Coworkee.Client.Blazor.Components.Editors;

/// <param name="Extensions">The file extensions of the type (".jpg, .jpeg"); searchable and shown next to the value.</param>
public sealed record ContentTypeInfo(string Value, string Name, string Icon, string Group, string Extensions = "");

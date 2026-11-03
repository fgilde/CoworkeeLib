namespace Coworkee.Contracts.Data;

/// <summary>A field rows can be mapped to; <see cref="KeyOnly"/> fields (the Id) only find rows to update.</summary>
public sealed record ImportFieldDto(string Name, string Label, string Type, bool Required, IReadOnlyList<string>? Choices = null, string? LookupSet = null, bool KeyOnly = false);

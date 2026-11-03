namespace Coworkee.Client.Blazor.Navigation;

/// <summary>A page found for an address: render <see cref="Component"/> with <see cref="Values"/>, or go to <see cref="RedirectTo"/>.</summary>
public sealed record PageMatch(CoworkeePage Page, Type Component, IReadOnlyDictionary<string, object?> Values, string? RedirectTo = null);

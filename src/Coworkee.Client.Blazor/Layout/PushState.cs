namespace Coworkee.Client.Blazor.Layout;

/// <summary><see cref="Permission"/>: unsupported, denied, default or granted; <see cref="Endpoint"/>: this browser's subscription.</summary>
public sealed record PushState(string Permission, string? Endpoint);

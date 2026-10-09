namespace Coworkee.Client.Blazor.Components.About;

/// <summary>A library the app is built with; <paramref name="Icon"/> is an image address or a MudBlazor icon.</summary>
public sealed record AboutCredit(string Title, string Url, string Icon, string? Version = null);

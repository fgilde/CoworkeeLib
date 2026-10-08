namespace Coworkee.Client.Blazor.Pages;

/// <summary>An extra tab on the account page, register it as singleton; Key is the last route segment, /profile/{Key}.</summary>
public sealed record ProfileTab(string Title, string Key, Type Component, string? Permission = null, string? Icon = null, int Order = 0);

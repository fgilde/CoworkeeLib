namespace Coworkee.Client.Blazor;

/// <summary>A typed configuration section the settings page offers; <see cref="Meta"/> is an Action&lt;ObjectEditMeta&lt;T&gt;&gt; or null.</summary>
public sealed record ClientAppConfiguration(string Section, string Title, Type Type, object? Meta, bool IsAppSettings = false);

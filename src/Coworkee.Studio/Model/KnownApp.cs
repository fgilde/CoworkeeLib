namespace Coworkee.Studio.Model;

/// <summary>An app of My apps: its solution folder, name, when it was created, the options that differ from the defaults and when the wizard last opened it.</summary>
public sealed record KnownApp(string Path, string Name, DateTimeOffset Created, Dictionary<string, string?> Options, DateTimeOffset? Opened = null);

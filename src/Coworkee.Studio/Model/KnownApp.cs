namespace Coworkee.Studio.Model;

/// <summary>An app of My apps: its solution folder, name, when it was created and the options that differ from the defaults.</summary>
public sealed record KnownApp(string Path, string Name, DateTimeOffset Created, Dictionary<string, string?> Options);

using System.Text.Json.Nodes;

namespace Coworkee.Search;

/// <summary>One document; <see cref="Id"/> is its key, <see cref="Body"/> the indexed fields (dotted names become nested objects).</summary>
public sealed record SearchDocument(string Id, JsonObject Body);

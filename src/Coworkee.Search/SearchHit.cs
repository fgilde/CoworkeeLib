using System.Text.Json.Nodes;

namespace Coworkee.Search;

public sealed record SearchHit(string Id, double? Score, JsonObject? Fields);

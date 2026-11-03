using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Search;

[JsonConverter(typeof(JsonStringEnumConverter<GlobalSearchProviderState>))]
public enum GlobalSearchProviderState
{
    Done,
    TimedOut,
    Failed,
}

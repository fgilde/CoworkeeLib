using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coworkee.Contracts;

/// <summary>
/// The JSON options of the clients: web defaults (camelCase, case-insensitive) and enums by name, as the API and OData send them.
/// Enum numbers still read, and [Flags] values round-trip as "A, B". The API reads names and numbers in request bodies.
/// </summary>
public static class CoworkeeJson
{
    /// <summary>Shared and read-only; derive with <c>new JsonSerializerOptions(CoworkeeJson.Web) { ... }</c>.</summary>
    public static JsonSerializerOptions Web { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}

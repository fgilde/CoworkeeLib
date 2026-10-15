using System.Text.Json;
using System.Text.Json.Nodes;

namespace Coworkee.Cli.Spec.Import;

/// <summary>Object schemas of components.schemas (or Swagger 2 definitions) become entities, string enums enums, $refs references.</summary>
public static class OpenApiImporter
{
    public static async Task<JsonObject> LoadAsync(string source, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                text = await http.GetStringAsync(uri, cancellationToken);
            }
            else
            {
                text = await File.ReadAllTextAsync(source, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            throw new ImportException($"Cannot read {source}: {exception.Message}");
        }

        // ponytail: JSON only; YAML needs a parser the CLI does not carry
        if (!text.TrimStart().StartsWith('{'))
        {
            throw new ImportException($"{source} is no JSON document; convert a YAML OpenAPI document to JSON first.");
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new ImportException($"{source} is no JSON object.");
        }
        catch (JsonException exception)
        {
            throw new ImportException($"{source} is no valid JSON: {exception.Message}");
        }
    }

    public static ImportResult Import(JsonObject document, ImportRequest request)
    {
        var schemas = (document["components"]?["schemas"] ?? document["definitions"]) as JsonObject
            ?? throw new ImportException("The document has no components.schemas (or definitions).");
        var skipped = new List<string>();
        var taken = ImportNames.Set();
        var enums = new Dictionary<string, EnumSpec>();
        var objects = new Dictionary<string, (string Name, JsonObject Schema)>();
        foreach (var (key, node) in schemas)
        {
            if (node is not JsonObject schema || (request.Tables is { Count: > 0 } only && !only.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (schema["enum"] is JsonArray values)
            {
                if (EnumOf(values, Strip(key), taken) is { } spec)
                {
                    enums[key] = spec;
                }
                else
                {
                    skipped.Add($"{key}: an enum without text values; properties using it become ints.");
                }
            }
            else if (Properties(schema, schemas).Count > 0)
            {
                objects[key] = (ImportNames.TypeName(Strip(key), taken), schema);
            }
            else
            {
                skipped.Add($"{key}: neither an object with properties nor an enum.");
            }
        }

        var hasText = objects.ToDictionary(o => o.Key, o => Properties(o.Value.Schema, schemas).Any(p => p.Value is JsonObject s && TypeOf(s) == "string"
            && s["enum"] is null && s["format"]?.ToString() is null or "email" && !ImportNames.IsBase(ImportNames.Pascal(p.Key))));
        var entities = new List<EntitySpec>();
        foreach (var (key, (name, schema)) in objects)
        {
            var entity = new EntitySpec { Name = name };
            var members = ImportNames.Set();
            var required = Required(schema, schemas);
            var dropped = new List<string>();
            foreach (var (propertyName, node) in Properties(schema, schemas))
            {
                var pascal = ImportNames.Pascal(propertyName);
                if (ImportNames.IsBase(pascal))
                {
                    dropped.Add(propertyName);
                    continue;
                }

                if (node is not JsonObject property)
                {
                    continue;
                }

                var spec = Property(key, propertyName, pascal, property, schemas, objects, enums, hasText, taken, skipped);
                if (spec is null)
                {
                    continue;
                }

                spec.Required = required.Contains(propertyName) && !IsNullable(property);
                spec.Name = ImportNames.Member(spec.Type == PropertyType.Reference ? spec.Name : pascal, name, members, spec.Type == PropertyType.Reference);
                entity.Properties.Add(spec);
            }

            if (dropped.Count > 0)
            {
                skipped.Add($"{key}: {string.Join(", ", dropped)} left out; every entity has Id, tenant and audit fields.");
            }

            if (entity.Properties.Count == 0)
            {
                skipped.Add($"{key}: no properties of its own.");
                continue;
            }

            entities.Add(entity);
        }

        var module = request.Module ?? (document["info"]?["title"]?.ToString() is { Length: > 0 } title ? Trim(ImportNames.Pascal(title), "Api") : "Imported");
        var used = entities.SelectMany(e => e.Properties).Select(p => p.Enum).OfType<string>().ToHashSet();
        return new ImportResult(new ModuleSpec { Name = module, Entities = entities }, [.. enums.Values.Where(e => used.Contains(e.Name))], skipped);
    }

    private static PropertySpec? Property(string owner, string propertyName, string pascal, JsonObject property, JsonObject schemas,
        Dictionary<string, (string Name, JsonObject Schema)> objects, Dictionary<string, EnumSpec> enums, Dictionary<string, bool> hasText, ISet<string> taken, List<string> skipped)
    {
        var at = $"{owner}.{propertyName}";
        if (RefOf(property) is { } target)
        {
            if (enums.TryGetValue(target, out var enumSpec))
            {
                return new PropertySpec { Type = PropertyType.Enum, Enum = enumSpec.Name };
            }

            if (objects.TryGetValue(target, out var referenced) && hasText[target])
            {
                return new PropertySpec { Name = pascal, Type = PropertyType.Reference, Reference = referenced.Name };
            }

            if (schemas[target] is JsonObject plain && plain["enum"] is not null)
            {
                return new PropertySpec { Type = PropertyType.Int };
            }

            skipped.Add(objects.ContainsKey(target) ? $"{at}: {target} has no text property to show, so there is no reference." : $"{at}: {target} is not imported.");
            return null;
        }

        var type = TypeOf(property);
        var format = property["format"]?.ToString();
        if (property["enum"] is JsonArray values && type == "string")
        {
            if (EnumOf(values, Strip(owner) + pascal, taken) is { } inline)
            {
                enums[$"{owner}.{propertyName}"] = inline;
                return new PropertySpec { Type = PropertyType.Enum, Enum = inline.Name };
            }
        }

        // customerId next to an imported Customer: a reference
        if (type is "string" or "integer" && pascal.Length > 2 && pascal.EndsWith("Id", StringComparison.Ordinal)
            && objects.FirstOrDefault(o => o.Value.Name == pascal[..^2]) is { Key: { } key } && hasText[key])
        {
            return new PropertySpec { Name = pascal[..^2], Type = PropertyType.Reference, Reference = pascal[..^2] };
        }

        PropertyType? result = (type, format) switch
        {
            ("string", "date-time") => PropertyType.DateTime,
            ("string", "date") => PropertyType.DateOnly,
            ("string", "time") => PropertyType.TimeOnly,
            ("string", "uuid") => PropertyType.Guid,
            ("string", "email") => PropertyType.Email,
            ("string", "uri" or "url") => PropertyType.Url,
            ("string", "binary" or "byte") => null,
            ("string", _) => PropertyType.String,
            ("integer", "int64") => PropertyType.Long,
            ("integer", _) => PropertyType.Int,
            ("number", "float" or "double") => PropertyType.Double,
            ("number", _) => PropertyType.Decimal,
            ("boolean", _) => PropertyType.Bool,
            _ => null,
        };
        if (result is null)
        {
            skipped.Add(type == "array" ? $"{at}: a list; collections are not supported, model them as a reference on the other side." : $"{at}: {type ?? "untyped"}{(format is null ? string.Empty : " " + format)} has no spec type.");
            return null;
        }

        return new PropertySpec
        {
            Type = result.Value,
            MaxLength = result is PropertyType.String or PropertyType.Email or PropertyType.Url && property["maxLength"]?.GetValue<int>() is > 0 and var max ? max : null,
        };
    }

    // allOf parts are merged, one $ref deep each
    private static List<KeyValuePair<string, JsonNode?>> Properties(JsonObject schema, JsonObject schemas, int depth = 0)
    {
        var own = (schema["properties"] as JsonObject)?.ToList() ?? [];
        if (depth < 5 && schema["allOf"] is JsonArray parts)
        {
            foreach (var part in parts.OfType<JsonObject>())
            {
                var resolved = RefOf(part) is { } name ? schemas[name] as JsonObject : part;
                own.AddRange(resolved is null ? [] : Properties(resolved, schemas, depth + 1).Where(p => own.All(o => o.Key != p.Key)));
            }
        }

        return own;
    }

    private static HashSet<string> Required(JsonObject schema, JsonObject schemas, int depth = 0)
    {
        var required = (schema["required"] as JsonArray)?.Select(r => r?.ToString()).OfType<string>().ToHashSet() ?? [];
        foreach (var part in depth < 5 && schema["allOf"] is JsonArray parts ? parts.OfType<JsonObject>() : [])
        {
            if ((RefOf(part) is { } name ? schemas[name] as JsonObject : part) is { } resolved)
            {
                required.UnionWith(Required(resolved, schemas, depth + 1));
            }
        }

        return required;
    }

    // $ref, or the allOf/anyOf/oneOf wrapper of a single $ref that OpenAPI 3.0 needs for a nullable reference
    private static string? RefOf(JsonObject schema)
    {
        if (schema["$ref"]?.ToString() is { } reference)
        {
            return reference[(reference.LastIndexOf('/') + 1)..];
        }

        var wrapper = (schema["allOf"] ?? schema["anyOf"] ?? schema["oneOf"]) as JsonArray;
        var refs = wrapper?.OfType<JsonObject>().Where(p => p["$ref"] is not null).ToList();
        return refs is [var single] && wrapper!.OfType<JsonObject>().All(p => p == single || p["type"]?.ToString() == "null") ? RefOf(single) : null;
    }

    // "string" or, in OpenAPI 3.1, ["string", "null"]
    private static string? TypeOf(JsonObject schema) => schema["type"] switch
    {
        JsonArray types => types.Select(t => t?.ToString()).FirstOrDefault(t => t != "null"),
        { } type => type.ToString(),
        _ => null,
    };

    private static bool IsNullable(JsonObject schema) =>
        schema["nullable"]?.GetValue<bool>() == true || (schema["type"] is JsonArray types && types.Any(t => t?.ToString() == "null"))
        || ((schema["anyOf"] ?? schema["oneOf"]) is JsonArray parts && parts.OfType<JsonObject>().Any(p => p["type"]?.ToString() == "null"));

    private static EnumSpec? EnumOf(JsonArray values, string name, ISet<string> taken)
    {
        if (values.Count == 0 || values.Any(v => v is not JsonValue value || value.GetValueKind() != JsonValueKind.String))
        {
            return null;
        }

        var labels = ImportNames.Set();
        return new EnumSpec { Name = ImportNames.TypeName(name, taken), Values = [.. values.Select(v => ImportNames.Unique(ImportNames.Pascal(v!.ToString()), labels))] };
    }

    private static string Strip(string key) => Trim(ImportNames.Pascal(key), "Dto");

    private static string Trim(string name, string suffix) => name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
}

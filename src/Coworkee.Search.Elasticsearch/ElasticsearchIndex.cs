using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Search.Elasticsearch;

public sealed class ElasticsearchOptions
{
    public const string Section = "Coworkee:Search:Elasticsearch";
    public const string ConnectionStringName = "elasticsearch";

    /// <summary>Base address; user info in the url (http://user:password@host:9200) is used for basic auth.</summary>
    public string? Url { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }
}

/// <summary>Elasticsearch through its REST API; one alias per schema, physical indexes swapped on reindex.</summary>
public sealed class CoworkeeElasticsearchModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var options = context.Configuration.GetSection(ElasticsearchOptions.Section).Get<ElasticsearchOptions>() ?? new ElasticsearchOptions();
        options.Url ??= context.Configuration.GetConnectionString(ElasticsearchOptions.ConnectionStringName) ?? "http://localhost:9200";
        context.Services.TryAddSingleton<ISearchIndex>(_ => new ElasticsearchIndex(options));
    }
}

internal sealed class ElasticsearchIndex : ISearchIndex, IDisposable
{
    private const string IdField = "cw_id";

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, SearchFieldType>> _types = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _aliases = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public ElasticsearchIndex(ElasticsearchOptions options)
    {
        var url = new Uri(options.Url!);
        _http = new HttpClient { BaseAddress = new Uri(url.GetLeftPart(UriPartial.Authority) + "/"), Timeout = TimeSpan.FromMinutes(2) };
        var (user, password) = url.UserInfo is { Length: > 0 } info && info.Split(':', 2) is [var u, var p]
            ? (Uri.UnescapeDataString(u), Uri.UnescapeDataString(p))
            : (options.Username, options.Password);
        if (user is not null)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _ensureLock.Dispose();
    }

    public async Task EnsureAsync(SearchSchema schema, CancellationToken cancellationToken)
    {
        // two jobs creating the alias at once would leave it on two indexes without a write index
        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            using var head = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"_alias/{schema.Alias}"), cancellationToken);
            if (head.StatusCode == HttpStatusCode.NotFound)
            {
                // an index carrying the alias name can only come from a write that auto created it; it holds nothing the next reindex would not restore
                await SendAsync(HttpMethod.Delete, schema.Alias, null, cancellationToken);
                await CreateIndexAsync(schema, cancellationToken, alias: schema.Alias);
            }
            else
            {
                // new fields are added; changing the type of an existing field needs a reindex
                await SendAsync(HttpMethod.Put, $"{schema.Alias}/_mapping", new JsonObject { ["properties"] = Properties(schema) }, cancellationToken);
            }

            _aliases.TryAdd(schema.Alias, true);
            Remember(schema.Alias, schema);
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    public Task<string> CreateIndexAsync(SearchSchema schema, CancellationToken cancellationToken) => CreateIndexAsync(schema, cancellationToken, alias: null);

    private async Task<string> CreateIndexAsync(SearchSchema schema, CancellationToken cancellationToken, string? alias)
    {
        var index = $"{schema.Alias}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..4]}";
        var body = new JsonObject { ["settings"] = Settings(), ["mappings"] = new JsonObject { ["properties"] = Properties(schema) } };
        if (alias is not null)
        {
            body["aliases"] = new JsonObject { [alias] = new JsonObject { ["is_write_index"] = true } };
        }

        await SendAsync(HttpMethod.Put, index, body, cancellationToken);
        Remember(index, schema);
        Remember(schema.Alias, schema);
        return index;
    }

    public async Task SwapAsync(string alias, string index, CancellationToken cancellationToken)
    {
        var previous = new List<string>();
        using (var response = await _http.GetAsync($"_alias/{alias}", cancellationToken))
        {
            if (response.IsSuccessStatusCode)
            {
                previous.AddRange((await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!.Select(p => p.Key).Where(name => name != index));
            }
        }

        var actions = new JsonArray(previous.Select(name => (JsonNode)new JsonObject { ["remove"] = new JsonObject { ["index"] = name, ["alias"] = alias } }).ToArray());
        actions.Add(new JsonObject { ["add"] = new JsonObject { ["index"] = index, ["alias"] = alias, ["is_write_index"] = true } });
        await SendAsync(HttpMethod.Post, "_aliases", new JsonObject { ["actions"] = actions }, cancellationToken);
        foreach (var name in previous)
        {
            await SendAsync(HttpMethod.Delete, name, null, cancellationToken);
        }

        _aliases.TryAdd(alias, true);
        if (_types.TryGetValue(index, out var types))
        {
            _types[alias] = types;
        }
    }

    public async Task<IReadOnlyList<SearchWriteError>> UpsertAsync(string target, IEnumerable<SearchDocument> documents, CancellationToken cancellationToken)
    {
        var body = new StringBuilder();
        foreach (var document in documents)
        {
            var source = (JsonObject)document.Body.DeepClone();
            source[IdField] = document.Id;
            body.Append(new JsonObject { ["index"] = new JsonObject { ["_index"] = target, ["_id"] = document.Id } }.ToJsonString()).Append('\n');
            body.Append(source.ToJsonString()).Append('\n');
        }

        return await BulkAsync(body, _aliases.ContainsKey(target), cancellationToken);
    }

    public async Task DeleteAsync(string target, IEnumerable<string> ids, CancellationToken cancellationToken)
    {
        var body = new StringBuilder();
        foreach (var id in ids)
        {
            body.Append(new JsonObject { ["delete"] = new JsonObject { ["_index"] = target, ["_id"] = id } }.ToJsonString()).Append('\n');
        }

        await BulkAsync(body, _aliases.ContainsKey(target), cancellationToken);
    }

    public async Task DeleteWhereAsync(string target, IReadOnlyList<SearchFilter> filters, CancellationToken cancellationToken)
    {
        var types = await TypesAsync(target, cancellationToken);
        await SendAsync(HttpMethod.Post, $"{target}/_delete_by_query?refresh=true&conflicts=proceed", new JsonObject { ["query"] = Bool(filters, types, null) }, cancellationToken);
    }

    public async Task<SearchResult> SearchAsync(string alias, SearchQuery query, CancellationToken cancellationToken)
    {
        var types = await TypesAsync(alias, cancellationToken);
        JsonObject? term = null;
        if (!string.IsNullOrWhiteSpace(query.Term) && query.TextFields.Count > 0)
        {
            term = new JsonObject
            {
                ["multi_match"] = new JsonObject
                {
                    ["query"] = query.Term,
                    ["fields"] = new JsonArray([.. query.TextFields.Select(f => (JsonNode)TextField(f, types))]),
                    ["type"] = query.Exact ? "phrase" : "best_fields",
                    ["operator"] = query.AllWords ? "and" : "or",
                },
            };
        }

        var sort = new JsonArray();
        foreach (var item in query.Sort)
        {
            sort.Add(new JsonObject { [ExactField(item.Field, types)] = new JsonObject { ["order"] = item.Descending ? "desc" : "asc", ["missing"] = "_last" } });
        }

        if (term is not null && query.Sort.Count == 0)
        {
            sort.Add(new JsonObject { ["_score"] = "desc" });
        }

        sort.Add(new JsonObject { [IdField] = "asc" });

        var body = new JsonObject
        {
            ["query"] = Bool(query.Filters, types, term),
            ["size"] = Math.Clamp(query.Size, 0, 1000),
            ["timeout"] = "10s",
            ["track_total_hits"] = true,
            ["sort"] = sort,
            ["_source"] = query.Include.Count == 0 ? false : new JsonArray([.. query.Include.Select(f => (JsonNode)f)]),
        };
        if (query.Cursor is { Length: > 0 } cursor)
        {
            body["search_after"] = Cursor(cursor, sort.Count);
        }

        if (query.FacetFilters.Count > 0)
        {
            body["post_filter"] = Bool(query.FacetFilters, types, null);
        }

        if (query.Facets.Count > 0)
        {
            var aggs = new JsonObject();
            foreach (var facet in query.Facets)
            {
                // every facet counts under the other facets' selections, so further values of the same facet stay selectable
                aggs[facet] = new JsonObject
                {
                    ["filter"] = Bool([.. query.FacetFilters.Where(f => f.Field != facet)], types, null),
                    ["aggs"] = new JsonObject { ["values"] = new JsonObject { ["terms"] = new JsonObject { ["field"] = ExactField(facet, types), ["size"] = 50 } } },
                };
            }

            body["aggs"] = aggs;
        }

        var response = (await SendAsync(HttpMethod.Post, $"{alias}/_search", body, cancellationToken))!;
        var hits = response["hits"]!["hits"]!.AsArray();
        var results = hits.Select(h => new SearchHit(h!["_id"]!.GetValue<string>(), h["_score"]?.GetValueKind() == JsonValueKind.Number ? h["_score"]!.GetValue<double>() : null, h["_source"] as JsonObject)).ToList();
        var facets = new Dictionary<string, IReadOnlyList<FacetValue>>(StringComparer.Ordinal);
        if (response["aggregations"] is JsonObject aggregations)
        {
            foreach (var (name, aggregation) in aggregations)
            {
                facets[name] = aggregation!["values"]!["buckets"]!.AsArray()
                    .Select(b => new FacetValue(b!["key_as_string"]?.GetValue<string>() ?? b["key"]!.ToString(), b["doc_count"]!.GetValue<long>())).ToList();
            }
        }

        var next = results.Count > 0 && results.Count == query.Size && hits[^1]!["sort"] is { } last
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(last.ToJsonString()))
            : null;
        return new SearchResult(results, response["hits"]!["total"]!["value"]!.GetValue<long>(), facets, next);
    }

    public async Task<IReadOnlyList<SearchHit>> SuggestAsync(string alias, string prefix, IReadOnlyList<string> fields, IReadOnlyList<SearchFilter> filters, int size, CancellationToken cancellationToken)
    {
        var types = await TypesAsync(alias, cancellationToken);
        var textFields = new JsonArray([.. fields.Select(f => (JsonNode)TextField(f, types))]);
        var match = new JsonObject
        {
            ["bool"] = new JsonObject
            {
                ["should"] = new JsonArray(
                    new JsonObject { ["multi_match"] = new JsonObject { ["query"] = prefix, ["type"] = "bool_prefix", ["fields"] = textFields.DeepClone() } },
                    new JsonObject { ["multi_match"] = new JsonObject { ["query"] = prefix, ["fuzziness"] = "AUTO", ["fields"] = textFields.DeepClone() } }),
                ["minimum_should_match"] = 1,
            },
        };
        var response = (await SendAsync(HttpMethod.Post, $"{alias}/_search", new JsonObject
        {
            ["query"] = Bool(filters, types, match),
            ["size"] = Math.Clamp(size, 1, 50),
            ["_source"] = new JsonArray([.. fields.Select(f => (JsonNode)f)]),
        }, cancellationToken))!;
        return response["hits"]!["hits"]!.AsArray().Select(h => new SearchHit(h!["_id"]!.GetValue<string>(), h["_score"]?.GetValue<double>(), h["_source"] as JsonObject)).ToList();
    }

    /// <summary>Text is split at punctuation and case changes too ("pixel.png", "QuarterlyReport"), keeps the original token, ignores case and accents.</summary>
    private static JsonObject Settings() => new()
    {
        ["analysis"] = new JsonObject
        {
            ["filter"] = new JsonObject
            {
                ["cw_split"] = new JsonObject { ["type"] = "word_delimiter_graph", ["preserve_original"] = true, ["split_on_numerics"] = false },
            },
            ["analyzer"] = new JsonObject
            {
                [TextAnalyzer] = new JsonObject { ["type"] = "custom", ["tokenizer"] = "whitespace", ["filter"] = new JsonArray("cw_split", "lowercase", "asciifolding") },
            },
        },
    };

    private const string TextAnalyzer = "cw_text";

    private static JsonObject Properties(SearchSchema schema)
    {
        var properties = new JsonObject { [IdField] = new JsonObject { ["type"] = "keyword" } };
        foreach (var field in schema.Fields)
        {
            properties[field.Name] = field.Type switch
            {
                SearchFieldType.Keyword => new JsonObject { ["type"] = "keyword", ["fields"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "text", ["analyzer"] = TextAnalyzer } } },
                SearchFieldType.Text => new JsonObject { ["type"] = "text", ["analyzer"] = TextAnalyzer, ["fields"] = new JsonObject { ["keyword"] = new JsonObject { ["type"] = "keyword", ["ignore_above"] = 512 } } },
                SearchFieldType.Date => new JsonObject { ["type"] = "date" },
                SearchFieldType.Long => new JsonObject { ["type"] = "long" },
                SearchFieldType.Double => new JsonObject { ["type"] = "double" },
                _ => new JsonObject { ["type"] = "boolean" },
            };
        }

        return properties;
    }

    private static JsonObject Bool(IReadOnlyList<SearchFilter> filters, IReadOnlyDictionary<string, SearchFieldType> types, JsonObject? must)
    {
        var filter = new JsonArray();
        var mustNot = new JsonArray();
        foreach (var condition in filters)
        {
            var (clause, negate) = Clause(condition, types);
            (negate ? mustNot : filter).Add(clause);
        }

        var result = new JsonObject { ["filter"] = filter, ["must_not"] = mustNot };
        if (must is not null)
        {
            result["must"] = must;
        }

        return new JsonObject { ["bool"] = result };
    }

    private static (JsonObject Clause, bool Negate) Clause(SearchFilter filter, IReadOnlyDictionary<string, SearchFieldType> types)
    {
        var values = filter.Values ?? [];
        var exact = ExactField(filter.Field, types);
        JsonArray Terms() => new([.. values.Select(v => (JsonNode)v)]);
        JsonObject AnyOf(Func<string, JsonObject> each) => new() { ["bool"] = new JsonObject { ["should"] = new JsonArray([.. values.Select(v => (JsonNode)each(v))]), ["minimum_should_match"] = 1 } };
        // substring, case insensitive, on the exact value; wildcard characters in the value are literal
        JsonObject Contains() => AnyOf(v => new JsonObject { ["wildcard"] = new JsonObject { [exact] = new JsonObject { ["value"] = $"*{EscapeWildcard(v)}*", ["case_insensitive"] = true } } });
        JsonObject Range(string op, string value) => new() { ["range"] = new JsonObject { [exact] = new JsonObject { [op] = value } } };

        return filter.Operator switch
        {
            FilterOperator.HasValue => (new JsonObject { ["exists"] = new JsonObject { ["field"] = filter.Field } }, false),
            FilterOperator.HasNoValue => (new JsonObject { ["exists"] = new JsonObject { ["field"] = filter.Field } }, true),
            FilterOperator.Equals or FilterOperator.IdentifierEquals => (new JsonObject { ["terms"] = new JsonObject { [exact] = Terms() } }, false),
            FilterOperator.NotEquals or FilterOperator.IdentifierNotEquals => (new JsonObject { ["terms"] = new JsonObject { [exact] = Terms() } }, true),
            FilterOperator.Contains => (Contains(), false),
            FilterOperator.DoesNotContain => (Contains(), true),
            FilterOperator.FuzzyEquals => (AnyOf(v => new JsonObject { ["fuzzy"] = new JsonObject { [exact] = new JsonObject { ["value"] = v, ["fuzziness"] = "AUTO" } } }), false),
            FilterOperator.FuzzyContains => (AnyOf(v => new JsonObject { ["match"] = new JsonObject { [TextField(filter.Field, types)] = new JsonObject { ["query"] = v, ["fuzziness"] = "AUTO" } } }), false),
            FilterOperator.GreaterThanOrEquals => (Range("gte", values[0]), false),
            FilterOperator.LessThanOrEquals => (Range("lte", values[0]), false),
            FilterOperator.Between => (new JsonObject { ["range"] = new JsonObject { [exact] = new JsonObject { ["gte"] = values[0], ["lte"] = values[1] } } }, false),
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter.Operator, "Unknown operator."),
        };
    }

    private static string EscapeWildcard(string value) => value.Replace(@"\", @"\\").Replace("*", @"\*").Replace("?", @"\?");

    private static JsonNode Cursor(string cursor, int sortFields)
    {
        try
        {
            if (JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(cursor))) is JsonArray values && values.Count == sortFields)
            {
                return values;
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
        }

        throw new SearchIndexException("The cursor does not belong to this search.", isInvalidRequest: true);
    }

    private static string ExactField(string field, IReadOnlyDictionary<string, SearchFieldType> types) =>
        types.GetValueOrDefault(field) == SearchFieldType.Text ? field + ".keyword" : field;

    private static string TextField(string field, IReadOnlyDictionary<string, SearchFieldType> types) =>
        types.GetValueOrDefault(field) == SearchFieldType.Keyword ? field + ".text" : field;

    private void Remember(string name, SearchSchema schema)
    {
        var known = _types.GetValueOrDefault(name) ?? new Dictionary<string, SearchFieldType>();
        _types[name] = known.Concat(schema.Fields.Where(f => !known.ContainsKey(f.Name)).Select(f => KeyValuePair.Create(f.Name, f.Type))).ToDictionary(StringComparer.Ordinal);
    }

    /// <summary>Field types of an alias or index, read from its mapping once per process.</summary>
    private async Task<IReadOnlyDictionary<string, SearchFieldType>> TypesAsync(string target, CancellationToken cancellationToken)
    {
        if (_types.TryGetValue(target, out var known))
        {
            return known;
        }

        var mapping = (await SendAsync(HttpMethod.Get, $"{target}/_mapping", null, cancellationToken))!;
        var types = new Dictionary<string, SearchFieldType>(StringComparer.Ordinal);
        foreach (var (_, index) in mapping)
        {
            Collect(index!["mappings"]?["properties"] as JsonObject, string.Empty, types);
        }

        _types[target] = types;
        return types;
    }

    private static void Collect(JsonObject? properties, string prefix, Dictionary<string, SearchFieldType> types)
    {
        foreach (var (name, definition) in properties ?? [])
        {
            var path = prefix + name;
            if (definition!["properties"] is JsonObject nested)
            {
                Collect(nested, path + ".", types);
                continue;
            }

            types[path] = definition["type"]?.GetValue<string>() switch
            {
                "text" => SearchFieldType.Text,
                "date" => SearchFieldType.Date,
                "long" or "integer" => SearchFieldType.Long,
                "double" or "float" => SearchFieldType.Double,
                "boolean" => SearchFieldType.Boolean,
                _ => SearchFieldType.Keyword,
            };
        }
    }

    /// <summary>Writes through an alias require it to exist, so a missing alias fails instead of auto creating an index of that name.</summary>
    private async Task<IReadOnlyList<SearchWriteError>> BulkAsync(StringBuilder body, bool requireAlias, CancellationToken cancellationToken)
    {
        if (body.Length == 0)
        {
            return [];
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "_bulk?refresh=wait_for" + (requireAlias ? "&require_alias=true" : string.Empty)) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/x-ndjson") };
        using var response = await _http.SendAsync(request, cancellationToken);
        var result = await ReadAsync(response, cancellationToken);
        return result?["errors"]?.GetValue<bool>() == true
            ? result["items"]!.AsArray().Select(i => i!.AsObject().First().Value!).Where(i => i["error"] is not null && i["status"]?.GetValue<int>() != 404)
                .Select(i => new SearchWriteError(i["_id"]!.GetValue<string>(), i["error"]!["reason"]?.ToString() ?? i["error"]!.ToJsonString())).ToList()
            : [];
    }

    private async Task<JsonObject?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (method == HttpMethod.Delete && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadAsync(response, cancellationToken);
    }

    private static async Task<JsonObject?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            throw new SearchIndexException($"Elasticsearch answered {status}: {(text.Length > 2000 ? text[..2000] : text)}", isInvalidRequest: status == 400);
        }

        return text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject;
    }
}


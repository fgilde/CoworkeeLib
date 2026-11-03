using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Search.Elasticsearch;

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

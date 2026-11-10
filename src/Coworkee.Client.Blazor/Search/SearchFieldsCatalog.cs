namespace Coworkee.Client.Blazor.Search;

/// <summary>The search fields the server declares per OData set, for data tables that name none; asked once per session, again after a failure.</summary>
internal sealed class SearchFieldsCatalog(IServiceProvider services)
{
    private Task<IReadOnlyDictionary<string, IReadOnlyList<string>>>? _fields;

    public async Task<IReadOnlyList<string>> OfAsync(string entitySet)
    {
        if (services.GetService(typeof(IGlobalSearchApi)) is not IGlobalSearchApi api)
        {
            return [];
        }

        try
        {
            return (await (_fields ??= api.GetSearchFieldsAsync())).GetValueOrDefault(entitySet) ?? [];
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or System.Text.Json.JsonException)
        {
            _fields = null;
            return [];
        }
    }
}

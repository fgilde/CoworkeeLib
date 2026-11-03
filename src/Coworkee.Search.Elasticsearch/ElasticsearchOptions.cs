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

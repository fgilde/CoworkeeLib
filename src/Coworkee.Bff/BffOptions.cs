namespace Coworkee.Bff;

public sealed class BffOptions
{
    public const string Section = "Coworkee:Bff";

    public string Authority { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string ApiAddress { get; set; } = string.Empty;

    public List<string> Scopes { get; set; } = [];

    public string? AuthorizationEndpoint { get; set; }
}

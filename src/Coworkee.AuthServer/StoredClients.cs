namespace Coworkee.AuthServer;

/// <summary>Addresses of the stored clients the content security policy allows: redirect targets and launcher logos.</summary>
internal sealed record StoredClients(string[] Uris, string[] Logos)
{
    public static readonly StoredClients None = new([], []);
}

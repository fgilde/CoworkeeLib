namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>Where the queries of a mirrored set run once its copy is complete.</summary>
public enum ClientEntityMode
{
    /// <summary>In the browser, on the local copy (the default).</summary>
    Client,

    /// <summary>On the server; the copy keeps syncing, so switching back is instant.</summary>
    Server,
}

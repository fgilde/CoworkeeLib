namespace Coworkee.Client.Blazor.Data;

/// <summary>Rows the client keeps of what the server shows (client entities); dropped when that changes, e.g. after a new group selection.</summary>
public interface IClientDataCache
{
    Task ClearAsync();
}

namespace Coworkee.Client.Blazor.ClientEntities;

public sealed class NoClientEntityStorage : IClientEntityStorage
{
    public Task<byte[]?> LoadAsync(string key) => Task.FromResult<byte[]?>(null);

    public Task SaveAsync(string key, byte[] data) => Task.CompletedTask;

    public Task ClearAsync() => Task.CompletedTask;
}

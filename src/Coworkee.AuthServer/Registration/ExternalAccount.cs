namespace Coworkee.AuthServer.Registration;

public sealed record ExternalAccount(string Provider, string Key, string? DisplayName);

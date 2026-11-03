namespace Coworkee.Contracts.Search;

public sealed record GlobalSearchProviderStatus(string Name, string Group, GlobalSearchProviderState State, int Count);

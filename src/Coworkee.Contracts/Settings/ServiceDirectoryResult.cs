namespace Coworkee.Contracts.Settings;

/// <summary>The services of all sources; UnavailableSources names the sources that could not be asked, their services are missing.</summary>
public sealed record ServiceDirectoryResult(IReadOnlyList<ServiceDto> Services, IReadOnlyList<string> UnavailableSources);

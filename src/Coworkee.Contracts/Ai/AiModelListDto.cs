namespace Coworkee.Contracts.Ai;

/// <summary>The models a provider lists for a key, or why it listed none.</summary>
public sealed record AiModelListDto(IReadOnlyList<AiModelDto> Models, string? Error = null);

namespace Coworkee.Contracts.Auditing;

/// <param name="Version">The state of the record the preview shows; the restore sends it back.</param>
public sealed record EntityHistoryPreviewDto(bool Undeletes, IReadOnlyList<EntityHistoryFieldDto> Fields, string Version = "");

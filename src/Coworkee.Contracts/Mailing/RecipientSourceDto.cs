namespace Coworkee.Contracts.Mailing;

public sealed record RecipientSourceDto(
    RecipientSourceKind Kind,
    RecipientPreset? Preset = null,
    int? Days = null,
    IReadOnlyList<Guid>? RoleIds = null,
    string? Filter = null,
    string? QueryJson = null,
    IReadOnlyList<Guid>? UserIds = null);

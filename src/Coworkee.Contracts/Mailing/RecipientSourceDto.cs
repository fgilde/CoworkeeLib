namespace Coworkee.Contracts.Mailing;

public sealed record RecipientSourceDto(
    RecipientSourceKind Kind,
    RecipientPreset? Preset = null,
    int? Days = null,
    IReadOnlyList<Guid>? RoleIds = null,
    string? Filter = null,
    string? QueryJson = null,
    IReadOnlyList<Guid>? UserIds = null)
{
    /// <summary>The users of <see cref="RecipientSourceKind.Selection"/>, resolved with the rights of the sender.</summary>
    public Identity.UserSelection? Selection { get; init; }
}

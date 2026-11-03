namespace Coworkee.Contracts.Mailing;

/// <param name="Cultures">Every language variant: the shipped defaults and the added ones.</param>
/// <param name="DefaultCultures">The shipped defaults of a registered template; only these can be reset, the others removed.</param>
public sealed record MailTemplateSummaryDto(
    string Name, string DisplayName, IReadOnlyList<string> Cultures, IReadOnlyList<string> OverriddenCultures, bool IsCustom, string? Description,
    IReadOnlyList<string>? DefaultCultures = null);

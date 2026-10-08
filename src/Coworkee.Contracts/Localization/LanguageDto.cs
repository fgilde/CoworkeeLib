namespace Coworkee.Contracts.Localization;

public sealed record LanguageDto(Guid? Id, string Culture, string Name, bool IsEnabled, bool IsDefault);

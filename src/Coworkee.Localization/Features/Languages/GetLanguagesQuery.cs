using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Languages;

/// <summary>Languages users can pick; without any in the database, English plus every culture a module ships texts for.</summary>
public sealed record GetLanguagesQuery(bool IncludeDisabled = false) : IQuery<Result<IReadOnlyList<LanguageDto>>>;

using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;

namespace Coworkee.Localization.Features.Texts;

public sealed record GetTextsQuery(string Culture) : IQuery<Result<TextsDto>>;

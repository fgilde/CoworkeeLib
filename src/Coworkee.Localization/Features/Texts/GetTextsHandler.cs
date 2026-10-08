using Coworkee.Application.Messaging;
using Coworkee.Contracts.Localization;
using Coworkee.Core.Results;
using Coworkee.Localization.Texts;

namespace Coworkee.Localization.Features.Texts;

internal sealed class GetTextsHandler(TextStore store) : IHandler<GetTextsQuery, Result<TextsDto>>
{
    public async Task<Result<TextsDto>> HandleAsync(GetTextsQuery query, CancellationToken cancellationToken) =>
        new TextsDto(query.Culture, await store.GetAsync(query.Culture, cancellationToken));
}

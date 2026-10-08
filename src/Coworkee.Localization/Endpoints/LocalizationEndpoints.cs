using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Localization;
using Coworkee.Localization.Features.Languages;
using Coworkee.Localization.Features.Texts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Localization.Endpoints;

internal static class LocalizationEndpoints
{
    public static void MapLocalizationEndpoints(this IEndpointRouteBuilder app)
    {
        var localization = app.MapCoworkeeApi("/api/v1/localization").WithTags("Localization");
        localization.MapGet("/languages", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetLanguagesQuery(), ct).ToHttpResult()).AllowAnonymous();
        localization.MapGet("/texts/{culture}", (string culture, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetTextsQuery(culture), ct).ToHttpResult()).AllowAnonymous();

        var managed = localization.MapGroup(string.Empty).RequireAuthorization();
        managed.MapPost("/missing", (MissingTextsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ReportMissingTextsCommand(body.Keys), ct).ToHttpResult());
        managed.MapGet("/languages/all", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetLanguagesQuery(IncludeDisabled: true), ct).ToHttpResult());
        managed.MapPost("/languages", (AddEditLanguageRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEditLanguageCommand(null, body), ct).ToHttpResult());
        managed.MapPut("/languages/{id:guid}", (Guid id, AddEditLanguageRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEditLanguageCommand(id, body), ct).ToHttpResult());
        managed.MapPost("/languages/delete", (IdListRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteLanguagesCommand(body.Ids), ct).ToHttpResult());
        managed.MapGet("/translations/{culture}", (string culture, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetTranslationRowsQuery(culture), ct).ToHttpResult());
        managed.MapPut("/translations", (SetTranslationRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetTranslationCommand(body), ct).ToHttpResult());
    }
}

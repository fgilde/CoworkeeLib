using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Auditing;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Auditing;

/// <summary>Headers and bodies of one logged request, loaded when its row opens (the list leaves them out).</summary>
public partial class RequestLogDetail
{
    private RequestLogDetailDto? _detail;

    [Parameter, EditorRequired] public Guid Id { get; set; }

    [Inject] private IAuditApi Api { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    protected override Task OnParametersSetAsync() => _detail?.Request.Id == Id ? Task.CompletedTask : Snackbar.RunAsync(async () => _detail = await Api.GetRequestAsync(Id));

    private static string Headers(IReadOnlyDictionary<string, string> headers) => string.Join('\n', headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase).Select(h => $"{h.Key}: {h.Value}"));

    private static string? Body(string? body) => body is null ? null : LogJson.Pretty(body);
}

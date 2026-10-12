using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Ai;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Ai;

[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record ListAiModels(AiConnectionDraft Draft) : IQuery<Result<AiModelListDto>>;

[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.Manage)]
public sealed record TestAiConnection(AiConnectionDraft Draft) : IQuery<Result<AiConnectionTestDto>>;

internal sealed class AiConnectionDraftValidator : AbstractValidator<AiConnectionDraft>
{
    public AiConnectionDraftValidator()
    {
        RuleFor(d => d.Provider).Must(p => AiProviderCatalog.Find(p) is not null).WithMessage($"Expected one of: {string.Join(", ", AiProviderCatalog.Ids)}.");
        RuleFor(d => d.Model).MaximumLength(200);
        RuleFor(d => d.ApiKey).MaximumLength(2000);
        RuleFor(d => d.Endpoint).MaximumLength(500)
            .Must(e => string.IsNullOrWhiteSpace(e) || (Uri.TryCreate(e.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            .WithMessage("Expected an http or https address.");
        RuleFor(d => d.Headers).MaximumLength(4000)
            .Must(h => AiEndpoints.InvalidHeaderLine(h) is null).WithMessage(d => $"Expected lines \"Name: value\", not \"{AiEndpoints.InvalidHeaderLine(d.Headers)}\".");
        RuleFor(d => d.AuthHeader).Must(h => string.IsNullOrWhiteSpace(h) || AiEndpoints.IsHeaderName(h.Trim())).WithMessage("Expected a header name such as api-key.");
    }
}

internal sealed class ListAiModelsValidator : AbstractValidator<ListAiModels>
{
    public ListAiModelsValidator() => RuleFor(q => q.Draft).NotNull().SetValidator(new AiConnectionDraftValidator());
}

internal sealed class TestAiConnectionValidator : AbstractValidator<TestAiConnection>
{
    public TestAiConnectionValidator() => RuleFor(q => q.Draft).NotNull().SetValidator(new AiConnectionDraftValidator());
}

internal sealed class AiConnectionHandlers(AiConnections connections, AiConnectionProbe probe, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<ListAiModels, Result<AiModelListDto>>, IHandler<TestAiConnection, Result<AiConnectionTestDto>>
{
    public async Task<Result<AiModelListDto>> HandleAsync(ListAiModels query, CancellationToken cancellationToken) =>
        await ResolveAsync(query.Draft, cancellationToken) is { } connection
            ? await probe.ListModelsAsync(connection, cancellationToken)
            : Forbidden;

    public async Task<Result<AiConnectionTestDto>> HandleAsync(TestAiConnection query, CancellationToken cancellationToken)
    {
        if (await ResolveAsync(query.Draft, cancellationToken) is not { } connection)
        {
            return Forbidden;
        }

        return connection.IsComplete
            ? await probe.TestAsync(connection, cancellationToken)
            : new AiConnectionTestDto(false, 0, null, false, "The connection lacks a key, an endpoint or a model.");
    }

    private static readonly Error Forbidden = Error.Forbidden("ai.global_forbidden", "The AI connection can only be changed from the system organisation.");

    // the AI settings are system wide; the stored key only goes to the provider and endpoint it was saved for
    private async Task<AiConnection?> ResolveAsync(AiConnectionDraft draft, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not { } tenantId
            || services.GetService<ITenantDirectory>() is not { } tenants
            || !await tenants.IsSystemTenantAsync(tenantId, cancellationToken))
        {
            return null;
        }

        var saved = await connections.GetSavedAsync(cancellationToken);
        var draftConnection = new AiConnection(draft.Provider, draft.Model, draft.Endpoint, draft.ApiKey);
        var sameTarget = saved.Provider == draft.Provider && saved.EffectiveEndpoint == draftConnection.EffectiveEndpoint;
        return draftConnection with
        {
            ApiKey = draft.ApiKey ?? (sameTarget ? saved.ApiKey : null),
            Headers = draft.Headers,
            AuthHeader = draft.AuthHeader?.Trim(),
        };
    }
}

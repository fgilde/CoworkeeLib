using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

internal sealed class HeadScriptHandlers(ISettingDefinitionManager definitions, ISettingProvider settings, SettingWriter writer, ICurrentUser currentUser, IServiceProvider services)
    : IHandler<GetHeadScript, Result<HeadScriptDto>>, IHandler<SetHeadScript, Result>
{
    public async Task<Result<HeadScriptDto>> HandleAsync(GetHeadScript query, CancellationToken cancellationToken)
    {
        if (definitions.Find(HeadScriptSettings.Value) is null)
        {
            return new HeadScriptDto(false, string.Empty);
        }

        var enabled = string.Equals(await settings.GetAsync(HeadScriptSettings.Enabled, cancellationToken), "true", StringComparison.OrdinalIgnoreCase);
        return new HeadScriptDto(enabled, await settings.GetAsync(HeadScriptSettings.Value, cancellationToken) ?? string.Empty);
    }

    public async Task<Result> HandleAsync(SetHeadScript command, CancellationToken cancellationToken)
    {
        if (definitions.Find(HeadScriptSettings.Value) is not { } definition || currentUser.TenantId is not { } tenantId)
        {
            return Error.Forbidden("settings.head_script", "The head script is not turned on in this app.");
        }

        // the system organisation sets the installation's script, every other organisation its own (when the app allows that)
        var system = services.GetService<ITenantDirectory>() is { } tenants && await tenants.IsSystemTenantAsync(tenantId, cancellationToken);
        if (!system && !definition.Scopes.Contains(SettingScope.Tenant))
        {
            return Error.Forbidden("settings.head_script", "Only the system organisation sets the head script.");
        }

        var (scope, key) = system ? (SettingScope.Global, (Guid?)null) : (SettingScope.Tenant, tenantId);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [HeadScriptSettings.Enabled] = command.Enabled ? "true" : "false",
            [HeadScriptSettings.Value] = command.Value ?? string.Empty,
        };
        return await writer.WriteAsync(scope, key, values, cancellationToken) is { } invalid ? invalid : Result.Success();
    }
}

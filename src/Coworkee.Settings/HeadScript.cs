using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

/// <summary>The head script as it applies to the caller; disabled and empty while the app has not turned the head script on.</summary>
// open: the value is public by design (anonymous clients run it), like the theme
[AiTool(Exclude = true)]
public sealed record GetHeadScript : IQuery<Result<HeadScriptDto>>;

/// <summary>Sets the head script of the caller's organisation, or of the installation from the system organisation.</summary>
// never an AI tool: a script written from a prompt would run in every user's browser
[AiTool(Exclude = true)]
[RequiresPermission(SettingsPermissions.HeadScript)]
public sealed record SetHeadScript(bool Enabled, string? Value) : ICommand<Result>;

internal sealed class GetHeadScriptValidator : AbstractValidator<GetHeadScript>;

internal sealed class SetHeadScriptValidator : AbstractValidator<SetHeadScript>
{
    public SetHeadScriptValidator() => RuleFor(c => c.Value).MaximumLength(HeadScriptSettings.MaxLength);
}

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

internal sealed class HeadScriptDefinitions(bool perOrganisation) : ISettingDefinitionContributor, IPermissionDefinitionContributor
{
    public void Define(SettingDefinitionContext context)
    {
        SettingScope[] scopes = perOrganisation ? [SettingScope.Global, SettingScope.Tenant] : [SettingScope.Global];
        context.Group("HeadScript", "Head script")
            .Add(HeadScriptSettings.Enabled, "Head script on", SettingType.Bool, scopes, "false", visibleToClient: true,
                description: "The app adds the head script to its page.", permission: SettingsPermissions.HeadScript)
            .Add(HeadScriptSettings.Value, "Head script", SettingType.Text, scopes, string.Empty, visibleToClient: true,
                description: "HTML with script tags, e.g. a tag manager. It runs in every user's browser.", permission: SettingsPermissions.HeadScript);
    }

    public void Define(PermissionDefinitionContext context) =>
        context.Group(SettingsPermissions.GroupName, "Settings").Add(SettingsPermissions.HeadScript, "Change the head script");
}

public static class HeadScriptExtensions
{
    /// <summary>
    /// Turns the head script on: the settings <see cref="HeadScriptSettings"/> and the permission <see cref="SettingsPermissions.HeadScript"/>.
    /// With <paramref name="perOrganisation"/> every organisation may set its own script for its users; otherwise only the system organisation sets one for all.
    /// </summary>
    public static IServiceCollection AddCoworkeeHeadScript(this IServiceCollection services, bool perOrganisation = false)
    {
        var definitions = new HeadScriptDefinitions(perOrganisation);
        services.AddSingleton<ISettingDefinitionContributor>(definitions);
        services.AddSingleton<IPermissionDefinitionContributor>(definitions);
        return services;
    }
}

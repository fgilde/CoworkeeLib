using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Clients;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record GetScopes : IQuery<Result<IReadOnlyList<ScopeDto>>>;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record CreateScope(ScopeRequest Scope) : ICommand<Result<Guid>>;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record UpdateScope(Guid Id, ScopeRequest Scope) : ICommand<Result>;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record DeleteScope(Guid Id) : ICommand<Result>;

internal sealed class ScopeRequestValidator : AbstractValidator<ScopeRequest>
{
    private static readonly string[] Standard = [Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.Roles, Scopes.OfflineAccess, Scopes.Phone, Scopes.Address];

    public ScopeRequestValidator()
    {
        RuleFor(s => s.Name).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._:-]+$")
            .Must(n => !Standard.Contains(n)).WithMessage("The OpenID Connect scopes are built in.");
        RuleFor(s => s.DisplayName).MaximumLength(200);
        RuleFor(s => s.Description).MaximumLength(1000);
        RuleForEach(s => s.Resources).NotEmpty().MaximumLength(200);
    }
}

internal sealed class CreateScopeValidator : AbstractValidator<CreateScope>
{
    public CreateScopeValidator() => RuleFor(c => c.Scope).SetValidator(new ScopeRequestValidator());
}

internal sealed class UpdateScopeValidator : AbstractValidator<UpdateScope>
{
    public UpdateScopeValidator() => RuleFor(c => c.Scope).SetValidator(new ScopeRequestValidator());
}

internal sealed class ScopeHandlers(IOpenIddictScopeManager scopes, HostAccess host)
    : IHandler<GetScopes, Result<IReadOnlyList<ScopeDto>>>,
      IHandler<CreateScope, Result<Guid>>,
      IHandler<UpdateScope, Result>,
      IHandler<DeleteScope, Result>
{
    private static readonly Error NotFound = Error.NotFound("auth.scope_not_found", "The scope does not exist.");

    private static readonly Error Managed = Error.Conflict("auth.scope_managed", "This scope comes from the configuration; change it there.");

    private static readonly Error Duplicate = Error.Conflict("auth.scope_exists", "A scope with this name exists already.");

    public async Task<Result<IReadOnlyList<ScopeDto>>> HandleAsync(GetScopes query, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        var list = new List<ScopeDto>();
        await foreach (var scope in scopes.ListAsync(null, null, cancellationToken))
        {
            list.Add(new ScopeDto(
                Guid.Parse((await scopes.GetIdAsync(scope, cancellationToken))!),
                (await scopes.GetNameAsync(scope, cancellationToken))!,
                await scopes.GetDisplayNameAsync(scope, cancellationToken),
                await scopes.GetDescriptionAsync(scope, cancellationToken),
                [.. await scopes.GetResourcesAsync(scope, cancellationToken)],
                (await scopes.GetPropertiesAsync(scope, cancellationToken)).ContainsKey(AuthClientSeeder.ManagedProperty)));
        }

        return list.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Result<Guid>> HandleAsync(CreateScope command, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        if (await scopes.FindByNameAsync(command.Scope.Name.Trim(), cancellationToken) is not null)
        {
            return Duplicate;
        }

        var descriptor = new OpenIddictScopeDescriptor();
        Apply(descriptor, command.Scope);
        return Guid.Parse((await scopes.GetIdAsync(await scopes.CreateAsync(descriptor, cancellationToken), cancellationToken))!);
    }

    public async Task<Result> HandleAsync(UpdateScope command, CancellationToken cancellationToken)
    {
        var (scope, error) = await EditableAsync(command.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (await scopes.FindByNameAsync(command.Scope.Name.Trim(), cancellationToken) is { } other
            && await scopes.GetIdAsync(other, cancellationToken) != command.Id.ToString())
        {
            return Duplicate;
        }

        var descriptor = new OpenIddictScopeDescriptor();
        await scopes.PopulateAsync(descriptor, scope!, cancellationToken);
        Apply(descriptor, command.Scope);
        await scopes.UpdateAsync(scope!, descriptor, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleAsync(DeleteScope command, CancellationToken cancellationToken)
    {
        var (scope, error) = await EditableAsync(command.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        await scopes.DeleteAsync(scope!, cancellationToken);
        return Result.Success();
    }

    private async Task<(object? Scope, Error? Error)> EditableAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return (null, HostAccess.Forbidden);
        }

        if (await scopes.FindByIdAsync(id.ToString(), cancellationToken) is not { } scope)
        {
            return (null, NotFound);
        }

        return (await scopes.GetPropertiesAsync(scope, cancellationToken)).ContainsKey(AuthClientSeeder.ManagedProperty) ? (null, Managed) : (scope, null);
    }

    private static void Apply(OpenIddictScopeDescriptor descriptor, ScopeRequest request)
    {
        descriptor.Name = request.Name.Trim();
        descriptor.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        descriptor.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        descriptor.Resources.Clear();
        descriptor.Resources.UnionWith(request.Resources.Select(r => r.Trim()));
    }
}

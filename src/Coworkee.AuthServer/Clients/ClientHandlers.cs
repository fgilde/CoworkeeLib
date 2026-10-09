using System.Security.Cryptography;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Clients;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record GetClients : IQuery<Result<IReadOnlyList<ClientDto>>>;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record CreateClient(ClientRequest Client) : ICommand<Result<ClientSecretDto>>;

/// <summary>Changes a client; turning a public client confidential generates its secret.</summary>
[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record UpdateClient(Guid Id, ClientRequest Client) : ICommand<Result<ClientSecretDto>>;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record RegenerateClientSecret(Guid Id) : ICommand<Result<ClientSecretDto>>;

[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record DeleteClient(Guid Id) : ICommand<Result>;

internal sealed class ClientRequestValidator : AbstractValidator<ClientRequest>
{
    public ClientRequestValidator()
    {
        RuleFor(c => c.ClientId).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._-]+$");
        RuleFor(c => c.DisplayName).MaximumLength(200);
        RuleFor(c => c.ClientType).Must(t => t is ClientTypes.Public or ClientTypes.Confidential).WithMessage("Choose public or confidential.");
        RuleFor(c => c.ConsentType).Must(t => t is ConsentTypes.Implicit or ConsentTypes.Explicit).WithMessage("Choose implicit or explicit.");
        RuleFor(c => c.GrantTypes).NotEmpty().Must(g => g.All(ClientGrantTypes.All.Contains)).WithMessage("Unknown grant type.");
        RuleFor(c => c.RedirectUris).NotEmpty().When(c => c.GrantTypes.Contains(ClientGrantTypes.AuthorizationCode));
        RuleFor(c => c.ClientType).Equal(ClientTypes.Confidential).When(c => c.GrantTypes.Contains(ClientGrantTypes.ClientCredentials))
            .WithMessage("Service clients have to be confidential.");
        RuleForEach(c => c.Permissions).NotEmpty();
        RuleForEach(c => c.RedirectUris).Must(BeAbsolute).WithMessage("'{PropertyValue}' is not an absolute address.");
        RuleForEach(c => c.PostLogoutRedirectUris).Must(BeAbsolute).WithMessage("'{PropertyValue}' is not an absolute address.");
        RuleForEach(c => c.Scopes).NotEmpty().Matches("^[A-Za-z0-9._:-]+$");
        RuleFor(c => c.ClientUri).Must(ClientApp.IsWebAddress).When(c => !string.IsNullOrWhiteSpace(c.ClientUri)).WithMessage("'{PropertyValue}' is not a web address.");
        RuleFor(c => c.LogoUrl).Must(ClientApp.IsWebAddress).When(c => !string.IsNullOrWhiteSpace(c.LogoUrl)).WithMessage("'{PropertyValue}' is not a web address.");
        RuleFor(c => c.Description).MaximumLength(500);
    }

    private static bool BeAbsolute(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out _);
}

internal sealed class CreateClientValidator : AbstractValidator<CreateClient>
{
    public CreateClientValidator() => RuleFor(c => c.Client).SetValidator(new ClientRequestValidator());
}

internal sealed class UpdateClientValidator : AbstractValidator<UpdateClient>
{
    public UpdateClientValidator() => RuleFor(c => c.Client).SetValidator(new ClientRequestValidator());
}

internal sealed class ClientHandlers(IOpenIddictApplicationManager applications, HostAccess host, ServiceClientRights rights)
    : IHandler<GetClients, Result<IReadOnlyList<ClientDto>>>,
      IHandler<CreateClient, Result<ClientSecretDto>>,
      IHandler<UpdateClient, Result<ClientSecretDto>>,
      IHandler<RegenerateClientSecret, Result<ClientSecretDto>>,
      IHandler<DeleteClient, Result>
{
    private static readonly Error NotFound = Error.NotFound("auth.client_not_found", "The client does not exist.");

    private static readonly Error Managed = Error.Conflict("auth.client_managed", "This client comes from the configuration; change it there.");

    private static readonly Error Duplicate = Error.Conflict("auth.client_exists", "A client with this client id exists already.");

    public async Task<Result<IReadOnlyList<ClientDto>>> HandleAsync(GetClients query, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        var clients = new List<ClientDto>();
        await foreach (var application in applications.ListAsync(null, null, cancellationToken))
        {
            clients.Add(await ToDtoAsync(application, cancellationToken));
        }

        return clients.OrderBy(c => c.ClientId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Result<ClientSecretDto>> HandleAsync(CreateClient command, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return HostAccess.Forbidden;
        }

        if (await applications.FindByClientIdAsync(command.Client.ClientId.Trim(), cancellationToken) is not null)
        {
            return Duplicate;
        }

        if (await rights.CheckAsync(command.Client, cancellationToken) is { } refused)
        {
            return refused;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        var secret = Apply(descriptor, command.Client);
        var application = await applications.CreateAsync(descriptor, cancellationToken);
        return new ClientSecretDto(Guid.Parse((await applications.GetIdAsync(application, cancellationToken))!), secret);
    }

    public async Task<Result<ClientSecretDto>> HandleAsync(UpdateClient command, CancellationToken cancellationToken)
    {
        var (application, error) = await EditableAsync(command.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (await applications.FindByClientIdAsync(command.Client.ClientId.Trim(), cancellationToken) is { } other
            && await applications.GetIdAsync(other, cancellationToken) != command.Id.ToString())
        {
            return Duplicate;
        }

        if (await rights.CheckAsync(command.Client, cancellationToken) is { } refused)
        {
            return refused;
        }

        var descriptor = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(descriptor, application!, cancellationToken);
        var secret = Apply(descriptor, command.Client);
        await applications.UpdateAsync(application!, descriptor, cancellationToken);
        return new ClientSecretDto(command.Id, secret);
    }

    public async Task<Result<ClientSecretDto>> HandleAsync(RegenerateClientSecret command, CancellationToken cancellationToken)
    {
        var (application, error) = await EditableAsync(command.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (!await applications.HasClientTypeAsync(application!, ClientTypes.Confidential, cancellationToken))
        {
            return Error.Conflict("auth.client_public", "Public clients have no secret.");
        }

        var secret = NewSecret();
        await applications.UpdateAsync(application!, secret, cancellationToken);
        return new ClientSecretDto(command.Id, secret);
    }

    public async Task<Result> HandleAsync(DeleteClient command, CancellationToken cancellationToken)
    {
        var (application, error) = await EditableAsync(command.Id, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        await applications.DeleteAsync(application!, cancellationToken);
        return Result.Success();
    }

    private async Task<(object? Application, Error? Error)> EditableAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await host.IsHostAsync(cancellationToken))
        {
            return (null, HostAccess.Forbidden);
        }

        if (await applications.FindByIdAsync(id.ToString(), cancellationToken) is not { } application)
        {
            return (null, NotFound);
        }

        return (await applications.GetPropertiesAsync(application, cancellationToken)).ContainsKey(AuthClientSeeder.ManagedProperty)
            ? (null, Managed)
            : (application, null);
    }

    /// <summary>Fills the descriptor from the request; returns a new secret when the client turned confidential.</summary>
    private static string? Apply(OpenIddictApplicationDescriptor descriptor, ClientRequest request)
    {
        string? secret = null;
        if (request.ClientType == ClientTypes.Public)
        {
            descriptor.ClientSecret = null;
        }
        else if (descriptor.ClientType != ClientTypes.Confidential || string.IsNullOrEmpty(descriptor.ClientSecret))
        {
            descriptor.ClientSecret = secret = NewSecret();
        }

        descriptor.ClientId = request.ClientId.Trim();
        descriptor.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        descriptor.ClientType = request.ClientType;
        descriptor.ConsentType = request.ConsentType;
        descriptor.RedirectUris.Clear();
        descriptor.RedirectUris.UnionWith(request.RedirectUris.Select(u => new Uri(u)));
        descriptor.PostLogoutRedirectUris.Clear();
        descriptor.PostLogoutRedirectUris.UnionWith(request.PostLogoutRedirectUris.Select(u => new Uri(u)));
        descriptor.Permissions.Clear();
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.UnionWith(request.GrantTypes.Select(g => Permissions.Prefixes.GrantType + g));
        descriptor.Permissions.UnionWith(request.Scopes.Where(s => s != Scopes.OpenId).Select(s => Permissions.Prefixes.Scope + s));
        descriptor.Requirements.Clear();
        if (request.GrantTypes.Contains(ClientGrantTypes.AuthorizationCode))
        {
            descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.EndSession, Permissions.ResponseTypes.Code]);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        var service = request.GrantTypes.Contains(ClientGrantTypes.ClientCredentials);
        ServiceClient.Write(descriptor, service ? request.Roles ?? [] : [], service ? request.Permissions ?? [] : []);
        ClientApp.Of(request.ClientUri, request.LogoUrl, request.Description, request.ShowInLauncher).Write(descriptor);
        return secret;
    }

    private static string NewSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private async Task<ClientDto> ToDtoAsync(object application, CancellationToken cancellationToken)
    {
        var permissions = await applications.GetPermissionsAsync(application, cancellationToken);
        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        var app = ClientApp.Read(properties);
        return new ClientDto(
            Guid.Parse((await applications.GetIdAsync(application, cancellationToken))!),
            (await applications.GetClientIdAsync(application, cancellationToken))!,
            await applications.GetDisplayNameAsync(application, cancellationToken),
            await applications.GetClientTypeAsync(application, cancellationToken) ?? ClientTypes.Public,
            await applications.GetConsentTypeAsync(application, cancellationToken) ?? ConsentTypes.Implicit,
            [.. await applications.GetRedirectUrisAsync(application, cancellationToken)],
            [.. await applications.GetPostLogoutRedirectUrisAsync(application, cancellationToken)],
            [.. Unprefixed(permissions, Permissions.Prefixes.GrantType)],
            [.. Unprefixed(permissions, Permissions.Prefixes.Scope)],
            properties.ContainsKey(AuthClientSeeder.ManagedProperty),
            ServiceClient.Roles(properties),
            ServiceClient.Permissions(properties),
            app.ClientUri,
            app.LogoUrl,
            app.Description,
            app.ShowInLauncher);
    }

    private static IEnumerable<string> Unprefixed(IEnumerable<string> permissions, string prefix) =>
        permissions.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).Select(p => p[prefix.Length..]);
}

using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;

namespace Coworkee.AuthServer.Clients;

/// <summary>Changes a client; turning a public client confidential generates its secret.</summary>
[RequiresPermission(IdentityPermissions.Clients.Manage)]
public sealed record UpdateClient(Guid Id, ClientRequest Client) : ICommand<Result<ClientSecretDto>>;

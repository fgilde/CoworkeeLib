using Coworkee.Application.Messaging;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Setup;

public sealed record GetSetupStatus : IQuery<Result<SetupStatusDto>>;

public sealed record GetSetupChecks : IQuery<Result<IReadOnlyList<SetupCheckDto>>>;

public sealed record CompleteSetup(CompleteSetupRequest Request) : ICommand<Result<SetupResultDto>>;

internal sealed class CompleteSetupValidator : AbstractValidator<CompleteSetup>
{
    public CompleteSetupValidator()
    {
        RuleFor(c => c.Request.SetupToken).NotEmpty();
        RuleFor(c => c.Request.TenantName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Request.AdminEmail).NotEmpty().EmailAddress();
        RuleFor(c => c.Request.AdminPassword).NotEmpty().MinimumLength(8);
    }
}

internal sealed class GetSetupStatusHandler(SystemStateCache state) : IHandler<GetSetupStatus, Result<SetupStatusDto>>
{
    public async Task<Result<SetupStatusDto>> HandleAsync(GetSetupStatus request, CancellationToken cancellationToken) =>
        new SetupStatusDto(await state.IsInitializedAsync(cancellationToken));
}

internal sealed class GetSetupChecksHandler(SystemStateCache state, IEnumerable<ISetupCheck> checks) : IHandler<GetSetupChecks, Result<IReadOnlyList<SetupCheckDto>>>
{
    public async Task<Result<IReadOnlyList<SetupCheckDto>>> HandleAsync(GetSetupChecks request, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        var results = new List<SetupCheckDto>();
        foreach (var check in checks)
        {
            try
            {
                results.Add(await check.RunAsync(cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new SetupCheckDto(check.Name, SetupCheckStatus.Error, exception.Message));
            }
        }

        return results;
    }
}

internal sealed class DatabaseSetupCheck(CoworkeeDbContext db) : ISetupCheck
{
    public string Name => "Database";

    public async Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? new SetupCheckDto(Name, SetupCheckStatus.Ok, null)
            : new SetupCheckDto(Name, SetupCheckStatus.Error, "The database is not reachable.");
}

internal sealed class CompleteSetupHandler(SetupToken token, SystemStateCache state, SystemInitializer initializer)
    : IHandler<CompleteSetup, Result<SetupResultDto>>
{
    public async Task<Result<SetupResultDto>> HandleAsync(CompleteSetup command, CancellationToken cancellationToken)
    {
        if (await state.IsInitializedAsync(cancellationToken))
        {
            return Error.Conflict("setup.completed", "The system is already set up.");
        }

        return token.Matches(command.Request.SetupToken)
            ? await initializer.InitializeAsync(command.Request, cancellationToken)
            : Error.Forbidden("setup.token_invalid", "The setup token is not valid.");
    }
}

internal static class IdentityErrors
{
    public static Error ToError(IdentityResult result) =>
        Error.Validation(result.Errors
            .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "Password"
                : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "Email"
                : "User")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray(), StringComparer.Ordinal));
}

using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Infrastructure.Persistence;

namespace Coworkee.Identity.Setup;

internal sealed class DatabaseSetupCheck(CoworkeeDbContext db) : ISetupCheck
{
    public string Name => "Database";

    public async Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? new SetupCheckDto(Name, SetupCheckStatus.Ok, null)
            : new SetupCheckDto(Name, SetupCheckStatus.Error, "The database is not reachable.");
}

using Coworkee.Contracts.Settings;
using Coworkee.Core.Results;

namespace Coworkee.Settings;

internal sealed class SettingsSetupStep(SettingWriter writer) : Coworkee.Application.Setup.ISetupStep
{
    public async Task<Error?> ApplyAsync(Coworkee.Contracts.Identity.CompleteSetupRequest request, Guid tenantId, CancellationToken cancellationToken) =>
        request.Settings is { Count: > 0 } settings ? await writer.WriteAsync(SettingScope.Global, null, settings, cancellationToken) : null;
}

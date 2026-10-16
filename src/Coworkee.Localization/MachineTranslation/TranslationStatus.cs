using Coworkee.Application.Status;
using Coworkee.Contracts.Localization;
using Coworkee.Contracts.Settings;

namespace Coworkee.Localization.MachineTranslation;

internal sealed class TranslationStatus(TextTranslator translator) : ISystemStatusContributor
{
    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var connection = await translator.ConnectionAsync(cancellationToken);
        var none = string.Equals(connection.Provider, TranslationProviders.None, StringComparison.OrdinalIgnoreCase);
        var ready = !none && await translator.IsAvailableAsync(cancellationToken);
        return new SystemStatusSection("translation", "Machine translation",
        [
            new("Provider", connection.Provider),
            SystemStatusEntry.Text("Configured", ready ? "Yes" : "No", ready ? SystemStatusLevel.Ok : none ? SystemStatusLevel.Info : SystemStatusLevel.Warning),
        ], Link: "/admin/settings", Order: 7);
    }
}

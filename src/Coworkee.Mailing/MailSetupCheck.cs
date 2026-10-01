using System.Net.Sockets;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Settings;

namespace Coworkee.Mailing;

internal sealed class MailSetupCheck(ISettingProvider settings) : ISetupCheck
{
    public string Name => "Mail";

    public async Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken)
    {
        var host = await settings.GetAsync(MailSettings.Host, cancellationToken);
        if (string.IsNullOrWhiteSpace(host))
        {
            return new SetupCheckDto(Name, SetupCheckStatus.Warning, "No mail server is configured yet. You can enter one in the next steps.");
        }

        var port = await settings.GetAsync<int>(MailSettings.Port, cancellationToken);
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await client.ConnectAsync(host, port, timeout.Token);
            return new SetupCheckDto(Name, SetupCheckStatus.Ok, $"{host}:{port} is reachable.");
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return new SetupCheckDto(Name, SetupCheckStatus.Warning, $"{host}:{port} is not reachable.");
        }
    }
}

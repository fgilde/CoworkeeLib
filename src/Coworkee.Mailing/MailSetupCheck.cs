using System.Net.Sockets;
using Coworkee.Application.Setup;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

internal sealed class MailSetupCheck(MailConnectionResolver resolver, IEnumerable<IMailTransport> transports) : ISetupCheck
{
    public string Name => "Mail";

    public async Task<SetupCheckDto> RunAsync(CancellationToken cancellationToken)
    {
        MailConnection connection;
        try
        {
            connection = await resolver.ResolveAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return new SetupCheckDto(Name, SetupCheckStatus.Warning, "No mail server is configured yet. You can enter one in the next steps.");
        }

        if (!transports.Any(t => t.Provider == connection.Provider))
        {
            return new SetupCheckDto(Name, SetupCheckStatus.Warning, $"No mail transport is registered for provider '{connection.Provider}'.");
        }

        if (connection.Provider != MailProviders.Smtp)
        {
            return new SetupCheckDto(Name, SetupCheckStatus.Ok, $"{connection.Provider} is configured.");
        }

        var host = connection[MailSettings.Host]!;
        var port = int.Parse(connection[MailSettings.Port] ?? "25", System.Globalization.CultureInfo.InvariantCulture);
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

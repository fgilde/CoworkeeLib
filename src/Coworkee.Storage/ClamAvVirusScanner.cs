using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Coworkee.Storage;

/// <summary>
/// The default <see cref="IVirusScanner"/>: clamd's <c>INSTREAM</c> at <c>ConnectionStrings:antivirus</c> (<c>tcp://host:3310</c>; the app host's ClamAV
/// with <c>options.UseAntivirus()</c>). Without that connection string nothing is scanned and every file passes (logged once). With it a file passes only
/// when clamd answers OK: a finding refuses it with the finding's name, an unreachable clamd or any other answer refuses it as not checked.
/// </summary>
public sealed partial class ClamAvVirusScanner(IConfiguration configuration, ILogger<ClamAvVirusScanner> logger) : IVirusScanner
{
    public const string ConnectionStringName = "antivirus";

    public const int DefaultPort = 3310;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private int _reportedMissing;

    public async Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        if (configuration.GetConnectionString(ConnectionStringName) is not { Length: > 0 } value)
        {
            if (Interlocked.Exchange(ref _reportedMissing, 1) == 0)
            {
                LogNotConfigured();
            }

            return VirusScanResult.NotScanned;
        }

        if (Address(value) is not { } clamd)
        {
            LogFailed(fileName, $"ConnectionStrings:{ConnectionStringName} is not an address like tcp://host:{DefaultPort}");
            return VirusScanResult.Failed("invalid address");
        }

        string reply;
        try
        {
            reply = await SendAsync(clamd, "zINSTREAM\0"u8.ToArray(), content, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or IOException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnreachable(fileName, clamd.Authority, exception);
            return VirusScanResult.Failed($"clamd at {clamd.Authority} is not reachable ({exception.GetType().Name})");
        }

        // "stream: OK", "stream: Eicar-Test-Signature FOUND", "INSTREAM size limit exceeded. ERROR"
        if (reply.EndsWith(" OK", StringComparison.Ordinal))
        {
            return VirusScanResult.Clean;
        }

        if (reply.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var finding = reply[..^" FOUND".Length];
            finding = finding[(finding.IndexOf(": ", StringComparison.Ordinal) is >= 0 and var colon ? colon + 2 : 0)..];
            LogFound(fileName, finding);
            return VirusScanResult.Infected(finding);
        }

        LogFailed(fileName, $"clamd answered '{reply}'");
        return VirusScanResult.Failed($"clamd answered '{reply}'");
    }

    /// <summary><c>tcp://host:port</c> (also <c>host:port</c> or a host alone, port 3310); null when it is none.</summary>
    public static Uri? Address(string value) =>
        Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "tcp://" + value, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? new UriBuilder(uri) { Port = uri.IsDefaultPort || uri.Port <= 0 ? DefaultPort : uri.Port }.Uri
            : null;

    /// <summary>A command of clamd (<c>zVERSION\0</c>, <c>zINSTREAM\0</c> with <paramref name="content"/> in chunks) and its answer without the terminator.</summary>
    internal static async Task<string> SendAsync(Uri clamd, byte[] command, Stream? content, CancellationToken cancellationToken)
    {
        using var tcp = new TcpClient();
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connect.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(clamd.Host, clamd.Port, connect.Token);
        }

        await using var stream = tcp.GetStream();
        await stream.WriteAsync(command, cancellationToken);
        if (content is not null)
        {
            var buffer = new byte[64 * 1024];
            var size = new byte[4];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                BinaryPrimitives.WriteInt32BigEndian(size, read);
                await stream.WriteAsync(size, cancellationToken);
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await stream.WriteAsync(new byte[4], cancellationToken);
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        return (await reader.ReadToEndAsync(cancellationToken)).Split('\0')[0].Trim();
    }

    [LoggerMessage(1, LogLevel.Information, "No virus scanner is configured (ConnectionStrings:antivirus): uploads are stored unscanned.")]
    private partial void LogNotConfigured();

    [LoggerMessage(2, LogLevel.Warning, "Malware found in {FileName}: {Finding}; the file is refused.")]
    private partial void LogFound(string fileName, string finding);

    [LoggerMessage(3, LogLevel.Error, "{FileName} could not be scanned, the file is refused: {Reason}")]
    private partial void LogFailed(string fileName, string reason);

    [LoggerMessage(4, LogLevel.Error, "ClamAV at {Address} is not reachable; {FileName} is refused.")]
    private partial void LogUnreachable(string fileName, string address, Exception exception);
}

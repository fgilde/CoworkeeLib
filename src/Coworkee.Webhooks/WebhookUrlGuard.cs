using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

/// <summary>Keeps webhooks away from the server's own network: https, standard ports and, after DNS resolution, public addresses only.</summary>
public sealed class WebhookUrlGuard(IOptions<WebhookOptions> options)
{
    private WebhookOptions Options => options.Value;

    /// <summary>Why the address may not be used, or null; checks what is visible without DNS.</summary>
    public string? Check(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return "Enter an absolute https address.";
        }

        if (uri.Scheme == "http" && Options.RequireHttps)
        {
            return "Webhooks need https.";
        }

        if (uri.UserInfo.Length > 0)
        {
            return "The address may not contain a user name or password.";
        }

        if (!uri.IsDefaultPort && !Options.AllowedPorts.Contains(uri.Port))
        {
            return "Only the standard port is allowed.";
        }

        var literal = uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 ? IPAddress.Parse(uri.DnsSafeHost) : null;
        return !Options.AllowPrivateNetworks && (uri.IsLoopback || (literal is not null && !IsPublic(literal)))
            ? "The address points into a private network."
            : null;
    }

    /// <summary>Connects only to public addresses of the host, so DNS cannot point a checked name somewhere else between check and send.</summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(a => Options.AllowPrivateNetworks || IsPublic(a)).ToArray();
        if (allowed.Length == 0)
        {
            throw new WebhookBlockedException($"{host} resolves to a private or reserved address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 or >= 224
                     || (b[0] == 100 && b[1] is >= 64 and < 128)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 172 && b[1] is >= 16 and < 32)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
                     || (b[0] == 198 && b[1] is 18 or 19));
        }

        // NAT64 (64:ff9b::/96) and 6to4 (2002::/16) carry an IPv4 address
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b)
        {
            return IsPublic(new IPAddress(b[12..16]));
        }

        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublic(new IPAddress(b[2..6]));
        }

        return !(IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
                 || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);
    }
}

public sealed class WebhookBlockedException(string message) : Exception(message);

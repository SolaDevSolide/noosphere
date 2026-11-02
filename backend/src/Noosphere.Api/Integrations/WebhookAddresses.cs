using System.Net;
using System.Net.Sockets;

namespace Noosphere.Api.Integrations;

/// <summary>Resolves a webhook's host name (replaced in tests).</summary>
public interface IWebhookResolver
{
    Task<IPAddress[]> Resolve(string host, CancellationToken cancel);
}

public sealed class DnsWebhookResolver : IWebhookResolver
{
    public Task<IPAddress[]> Resolve(string host, CancellationToken cancel) => Dns.GetHostAddressesAsync(host, cancel);
}

/// <summary>
/// Protection against server-side request forgery: a webhook may not target this host or its network (loopback,
/// private, link-local, carrier-grade NAT, unique local, multicast, reserved), unless the integration settings allow
/// private addresses. The URL is checked when the webhook is saved, and every address is checked again right before
/// connecting (a DNS answer that changed in between is refused too). Redirects are never followed.
/// </summary>
public static class WebhookAddresses
{
    private static readonly (IPAddress Network, int Bits)[] Blocked =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4),
        (IPAddress.Parse("::"), 128),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("64:ff9b:1::"), 48),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("fec0::"), 10),
        (IPAddress.Parse("ff00::"), 8),
    ];

    /// <summary>True for an address a webhook may not reach without the "private addresses" setting.</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        foreach (var (network, bits) in Blocked)
            if (network.AddressFamily == address.AddressFamily && new IPNetwork(network, bits).Contains(address)) return true;
        return false;
    }

    /// <summary>
    /// Checks a webhook URL: absolute, HTTPS (HTTP too when private addresses are allowed), without credentials, and a
    /// host that resolves to public addresses only (unless allowed). Returns the problem, or null.
    /// </summary>
    public static async Task<string?> Check(string url, bool allowPrivate, IWebhookResolver resolver, CancellationToken cancel = default)
    {
        if (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "An absolute HTTPS URL is expected.";
        if (uri.Scheme != Uri.UriSchemeHttps && !(allowPrivate && uri.Scheme == Uri.UriSchemeHttp))
            return allowPrivate ? "An http(s) URL is expected." : "An HTTPS URL is expected.";
        if (uri.UserInfo.Length > 0) return "Credentials in the URL are not allowed: the receiver checks the signature instead.";
        if (uri.Fragment.Length > 0) return "A URL without fragment (#) is expected.";
        if (allowPrivate) return null;
        return await CheckHost(uri.IdnHost, resolver, cancel);
    }

    /// <summary>The problem with a host, or null when every address it resolves to is public.</summary>
    public static async Task<string?> CheckHost(string host, IWebhookResolver resolver, CancellationToken cancel = default)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return PrivateMessage;
        IPAddress[] addresses;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal)) addresses = [literal];
        else
        {
            try
            {
                addresses = await resolver.Resolve(host, cancel);
            }
            catch (SocketException)
            {
                return $"The host '{host}' cannot be resolved.";
            }
        }
        if (addresses.Length == 0) return $"The host '{host}' cannot be resolved.";
        return addresses.Any(IsPrivate) ? PrivateMessage : null;
    }

    public const string PrivateMessage =
        "This address is private, loopback or link-local: webhooks may only reach public addresses. An administrator can allow private addresses in the integration settings.";

    /// <summary>Request option: whether this delivery may reach private addresses (read by the connection callback).</summary>
    public static readonly HttpRequestOptionsKey<bool> AllowPrivateOption = new("noosphere.webhook.allowPrivate");

    /// <summary>
    /// The handler of the webhook HTTP client: no redirect, no proxy, no cookie, and a connection callback that refuses a
    /// private address at connection time (DNS rebinding).
    /// </summary>
    public static SocketsHttpHandler Handler(IWebhookResolver resolver) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (context, cancel) =>
        {
            var allowPrivate = context.InitialRequestMessage.Options.TryGetValue(AllowPrivateOption, out var allowed) && allowed;
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? [literal] : await resolver.Resolve(host, cancel);
            var usable = allowPrivate ? addresses : [.. addresses.Where(a => !IsPrivate(a))];
            if (usable.Length == 0) throw new HttpRequestException(PrivateMessage);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(usable, context.DnsEndPoint.Port, cancel);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}

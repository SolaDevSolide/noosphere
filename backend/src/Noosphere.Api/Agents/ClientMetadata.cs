using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>An OAuth client as Noosphere knows it: from its metadata document, or from dynamic registration.</summary>
public sealed record OAuthClientInfo(string ClientId, string ClientName, string? ClientUri, IReadOnlyList<string> RedirectUris, bool MetadataDocument);

/// <summary>A client id, metadata document or redirect URI is refused.</summary>
public sealed class ClientMetadataException(string message) : Exception(message);

/// <summary>Fetches a Client ID Metadata Document. Replaced in tests.</summary>
public interface IClientMetadataFetcher
{
    /// <summary>The body of the document (at most <see cref="ClientMetadata.MaxBytes"/> bytes); throws <see cref="ClientMetadataException"/>.</summary>
    Task<string> Fetch(Uri url, CancellationToken cancel = default);
}

/// <summary>
/// Client ID Metadata Documents: the client id is an https URL serving JSON with <c>client_id</c> (the same URL),
/// <c>client_name</c> and <c>redirect_uris</c>. Redirect URIs are https, http on a loopback address (native clients, any
/// port), or a private-use scheme of a native application.
/// </summary>
public static class ClientMetadata
{
    public const int MaxBytes = 64 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public const int MaxRedirectUris = 10;
    public const int MaxUriLength = 2000;

    private static readonly string[] ForbiddenSchemes = ["javascript", "data", "file", "vbscript", "about", "blob", "ftp", "ws", "wss"];

    /// <summary>A client id that names a metadata document: an https URL (anything else is a registered client id).</summary>
    public static bool IsMetadataUrl(string clientId) => clientId.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks a metadata document URL: https, a path, no credentials, fragment or dot segments, and not a local or private
    /// host given literally (names are checked again when connecting).
    /// </summary>
    public static Uri CheckUrl(string clientId)
    {
        if (clientId.Length > MaxUriLength || !Uri.TryCreate(clientId, UriKind.Absolute, out var url))
            throw new ClientMetadataException("The client id is not a valid URL.");
        if (url.Scheme != Uri.UriSchemeHttps) throw new ClientMetadataException("A client metadata document must be served over https.");
        if (url.UserInfo.Length > 0) throw new ClientMetadataException("The client id URL must not hold credentials.");
        if (url.Fragment.Length > 0) throw new ClientMetadataException("The client id URL must not have a fragment.");
        if (url.AbsolutePath is "" or "/") throw new ClientMetadataException("The client id URL must have a path.");
        if (clientId.Contains("/./", StringComparison.Ordinal) || clientId.Contains("/../", StringComparison.Ordinal))
            throw new ClientMetadataException("The client id URL must not have dot segments.");
        if (url.IsLoopback || url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            throw new ClientMetadataException("A client metadata document cannot be on a local address.");
        if (IPAddress.TryParse(url.DnsSafeHost, out var ip) && !IsPublic(ip))
            throw new ClientMetadataException("A client metadata document cannot be on a private network address.");
        return url;
    }

    /// <summary>True for an address on the public internet: not loopback, private, link-local, shared, multicast or reserved.</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)       // shared address space
                || (b[0] == 169 && b[1] == 254)                    // link-local
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)    // IETF protocol assignments, documentation
                || (b[0] == 198 && b[1] is 18 or 19)               // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)
                || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast
                || (b[0] & 0xfe) == 0xfc                           // unique local fc00::/7
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8)); // documentation
        }
        return false;
    }

    /// <summary>Why a redirect URI is refused; null when it is acceptable.</summary>
    public static string? RedirectProblem(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > MaxUriLength || !Uri.TryCreate(uri, UriKind.Absolute, out var u)
            || !uri.StartsWith(u.Scheme + ":", StringComparison.OrdinalIgnoreCase))
            return "A redirect URI must be an absolute URI.";
        if (u.Fragment.Length > 0) return "A redirect URI must not have a fragment.";
        if (u.Scheme == Uri.UriSchemeHttps) return u.UserInfo.Length > 0 ? "A redirect URI must not hold credentials." : null;
        if (u.Scheme == Uri.UriSchemeHttp) return IsLoopback(u) ? null : "An http redirect URI is allowed on a loopback address only (localhost, 127.0.0.1, [::1]).";
        if (ForbiddenSchemes.Contains(u.Scheme, StringComparer.OrdinalIgnoreCase)) return $"The scheme '{u.Scheme}' is not allowed for a redirect URI.";
        return null;
    }

    private static bool IsLoopback(Uri u) => u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The redirect URI asked equals a registered one. For loopback http URIs, the port may differ (RFC 8252: native
    /// clients listen on any free port).
    /// </summary>
    public static bool RedirectMatches(string requested, IEnumerable<string> registered)
    {
        foreach (var r in registered)
        {
            if (string.Equals(r, requested, StringComparison.Ordinal)) return true;
            if (Uri.TryCreate(r, UriKind.Absolute, out var a) && Uri.TryCreate(requested, UriKind.Absolute, out var b)
                && a.Scheme == Uri.UriSchemeHttp && b.Scheme == Uri.UriSchemeHttp && IsLoopback(a) && IsLoopback(b)
                && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.PathAndQuery, b.PathAndQuery, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Parses and checks a metadata document fetched from <paramref name="clientId"/>.</summary>
    public static OAuthClientInfo Parse(string clientId, string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            throw new ClientMetadataException("The client metadata document is not valid JSON.");
        }
        if (root.ValueKind != JsonValueKind.Object) throw new ClientMetadataException("The client metadata document must be a JSON object.");
        if (Text(root, "client_id") != clientId) throw new ClientMetadataException("The client_id of the metadata document must equal its URL.");
        var name = Text(root, "client_name")?.Trim();
        if (string.IsNullOrEmpty(name)) throw new ClientMetadataException("The client metadata document has no client_name.");
        if (name.Length > Rules.MaxNameLength) name = name[..Rules.MaxNameLength];
        var method = Text(root, "token_endpoint_auth_method");
        if (method is not (null or "none"))
            throw new ClientMetadataException("Only public clients are supported: token_endpoint_auth_method must be \"none\".");
        var redirects = RedirectUris(root.TryGetProperty("redirect_uris", out var r) ? r : default);
        var uri = ClientUri(Text(root, "client_uri"));
        return new OAuthClientInfo(clientId, name, uri, redirects, MetadataDocument: true);
    }

    /// <summary>Checks a list of redirect URIs (1 to 10).</summary>
    public static List<string> RedirectUris(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) throw new ClientMetadataException("redirect_uris must list at least one URI.");
        if (array.GetArrayLength() > MaxRedirectUris) throw new ClientMetadataException($"At most {MaxRedirectUris} redirect URIs.");
        var list = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (RedirectProblem(value) is { } problem) throw new ClientMetadataException(problem);
            list.Add(value!);
        }
        return list;
    }

    /// <summary>An https home page, or null.</summary>
    public static string? ClientUri(string? value) =>
        value is { Length: > 0 and <= MaxUriLength } && Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? value : null;

    private static string? Text(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Fetches metadata documents safely: https only, no redirects, at most 64 KB in 5 seconds, and every address the host
/// resolves to must be public (checked when connecting, so that DNS cannot point it at the local network afterwards).
/// </summary>
public sealed class SafeClientMetadataFetcher : IClientMetadataFetcher, IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = ClientMetadata.Timeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = Connect,
    })
    {
        Timeout = ClientMetadata.Timeout,
        MaxResponseContentBufferSize = ClientMetadata.MaxBytes,
    };

    private static async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancel)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancel);
        if (addresses.Length == 0 || addresses.Any(a => !ClientMetadata.IsPublic(a)))
            throw new ClientMetadataException("The client metadata host resolves to a private or local address.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancel);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task<string> Fetch(Uri url, CancellationToken cancel = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode) throw new ClientMetadataException($"The client metadata document answered {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > ClientMetadata.MaxBytes) throw new ClientMetadataException("The client metadata document is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            var buffer = new byte[ClientMetadata.MaxBytes + 1];
            var read = 0;
            while (read < buffer.Length && await stream.ReadAsync(buffer.AsMemory(read), cancel) is var n and > 0) read += n;
            if (read > ClientMetadata.MaxBytes) throw new ClientMetadataException("The client metadata document is too large.");
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            throw e.InnerException as ClientMetadataException ?? new ClientMetadataException("The client metadata document could not be fetched.");
        }
    }

    public void Dispose() => _http.Dispose();
}

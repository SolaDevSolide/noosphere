using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Noosphere.Domain;

namespace Noosphere.Api.Links;

/// <summary>Constants and wire formats of the link protocol (<c>/link/v1</c>). See docs/en/links.md.</summary>
public static class LinkProtocol
{
    public const string BasePath = "/link/v1";
    public const string Product = "noosphere";

    public const string CatalogRead = "catalog:read";
    public const string NoticesSend = "notices:send";
    public const string DeploymentsReceive = "deployments:receive";
    public static readonly IReadOnlyList<string> AllCapabilities = [CatalogRead, NoticesSend, DeploymentsReceive];

    /// <summary>A signature's <c>created</c> may differ from the receiver's clock by this much.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(300);
    /// <summary>A nonce is refused again for this long.</summary>
    public static readonly TimeSpan NonceWindow = TimeSpan.FromMinutes(10);
    /// <summary>After a key renewal, the previous key is still accepted for this long.</summary>
    public static readonly TimeSpan RekeyGrace = TimeSpan.FromMinutes(5);
    /// <summary>At most one change notice per link in this window.</summary>
    public static readonly TimeSpan NoticeInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan BootstrapCodeLifetime = TimeSpan.FromHours(24);
    public const int MaxCodeFailures = 5;
    public const int MinBootstrapCodeLength = 16;
    public const int ExchangeDays = 90;
    public const int DeploymentDays = 365;
    public const int RequestsPerMinute = 120;

    /// <summary>Refusal reasons: the problem type is <c>https://noosphere.dev/problems/{reason}</c>.</summary>
    public static class Reasons
    {
        public const string UnknownKey = "unknown-key";
        public const string LinkCut = "link-cut";
        public const string NotConfirmed = "link-not-confirmed";
        public const string ClockSkew = "clock-skew";
        public const string Replay = "replay";
        public const string BadDigest = "bad-digest";
        public const string BadSignature = "bad-signature";
        /// <summary>Not one of the seven signature refusals: a capability the link does not have (403).</summary>
        public const string CapabilityMissing = "capability-missing";
        /// <summary>A body that is not a JWE for this side's current (or previous) encryption key, or not the expected JSON (400).</summary>
        public const string BadBody = "bad-body";
        /// <summary>Pairing: a wrong MAC (401) before the code is burnt.</summary>
        public const string BadMac = "bad-mac";
    }

    public static string ProblemType(string reason) => "https://noosphere.dev/problems/" + reason;

    /// <summary>Wire JSON: camelCase, nulls written, enums as their protocol values.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false), new UtcTimeConverter() },
    };

    /// <summary>Times are written in UTC, to the second, with a Z: <c>2026-11-12T20:00:00Z</c>.</summary>
    public static string Time(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseTime(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    private sealed class UtcTimeConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ParseTime(reader.GetString()) ?? throw new JsonException("Expected an ISO 8601 time.");

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(Time(value));
    }

    public static JsonNode ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Json)!;
}

/// <summary>Body of <c>GET /link/v1/status</c> (JWE).</summary>
/// <param name="Confirmed">The answering side has confirmed the fingerprint.</param>
public sealed record LinkStatusPayload(Guid LinkId, string Product, string InstanceName, string Version, bool Confirmed,
    IReadOnlyList<string> Capabilities, DateTimeOffset? KeysRenewedAt);

public sealed record CatalogDomain(string Id, string Name);

public sealed record CatalogTeam(string Id, string Name, string? Contact);

public sealed record CatalogApplication(string Id, string Name, string? Description, string DomainId, string? TeamId, string Kind, string Lifecycle, string? Tech, string Url);

public sealed record CatalogFlow(string Id, string Name, string FromId, string ToId, string Type, string Status, string? Schedule, string Url);

/// <summary>Body of <c>GET /link/v1/catalog</c> (JWE): what everyone sees.</summary>
public sealed record CatalogPayload(DateTimeOffset GeneratedAt, string InstanceName, string BaseUrl, IReadOnlyList<CatalogDomain> Domains,
    IReadOnlyList<CatalogTeam> Teams, IReadOnlyList<CatalogApplication> Applications, IReadOnlyList<CatalogFlow> Flows);

/// <summary>One change of a shared record. <c>kind</c>: application, flow, domain, team; <c>operation</c>: upsert, delete.</summary>
public sealed record NoticeChange(string Kind, string Id, string Operation, DateTimeOffset At);

/// <summary>Body of <c>POST /link/v1/notices</c> (JWE).</summary>
public sealed record NoticePayload(IReadOnlyList<NoticeChange> Changes);

/// <summary>Response of <c>POST /link/v1/deployments</c> (JWE).</summary>
/// <param name="Accepted">Events stored (created or updated, or already newer here).</param>
/// <param name="IgnoredApplicationIds">Application ids unknown here, or not shared, left out of the events.</param>
public sealed record DeploymentsReceipt(int Accepted, IReadOnlyList<string> IgnoredApplicationIds);

/// <summary>A deployment event as read from the wire, before it is stored.</summary>
public sealed record DeploymentEvent(string DeploymentId, string Title, DeploymentPhase Phase, DeploymentOutcome? Outcome, bool Postponed,
    DateTimeOffset? PlannedStart, DateTimeOffset? PlannedEnd, string Url, IReadOnlyList<string> ApplicationIds,
    IReadOnlyList<DeploymentEnvironment> Environments, DateTimeOffset UpdatedAt);

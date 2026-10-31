using System.Text.Json.Serialization;

namespace Noosphere.Domain;

/// <summary>
/// <c>pending</c> until an administrator of this instance confirms the fingerprint, then <c>active</c>; <c>cut</c> once
/// either side cuts it (its keys are erased, its history stays).
/// </summary>
public enum LinkState { Pending, Active, Cut }

/// <summary>
/// A link with a gestion-mep instance, paired with a single-use code. Each side holds two P-256 key pairs per link (signing
/// and encryption): this side's private keys are stored encrypted with ASP.NET Core Data Protection, the peer's public keys
/// as JWK. Key ids (RFC 7638 thumbprints) are kept after a cut, so that late requests are answered "link cut".
/// </summary>
public sealed class Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The peer product, <c>gestion-mep</c>.</summary>
    public string Product { get; set; } = "";
    public string InstanceName { get; set; } = "";
    /// <summary>Base URL of the peer; link endpoints are under <c>{Url}/link/v1</c>.</summary>
    public string Url { get; set; } = "";
    public string? PeerVersion { get; set; }
    public LinkState State { get; set; }
    public bool ConfirmedHere { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ConfirmedBy { get; set; }
    /// <summary>The peer's confirmation, as last read from its status (or implied by a data request from it); null until known.</summary>
    public bool? PeerConfirmed { get; set; }
    public DateTimeOffset? PeerCheckedAt { get; set; }
    /// <summary><c>catalog:read</c>, <c>notices:send</c>, <c>deployments:receive</c>.</summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>This side's signing private key (PKCS#8), protected; null once cut.</summary>
    public string? OwnSigningKey { get; set; }
    public string OwnSigningKid { get; set; } = "";
    /// <summary>This side's encryption private key (PKCS#8), protected; null once cut.</summary>
    public string? OwnEncryptionKey { get; set; }
    public string OwnEncryptionKid { get; set; } = "";
    /// <summary>After a key renewal: the previous encryption private key, still accepted until <see cref="OwnPreviousUntil"/>.</summary>
    public string? OwnPreviousEncryptionKey { get; set; }
    public string? OwnPreviousEncryptionKid { get; set; }
    public DateTimeOffset? OwnPreviousUntil { get; set; }

    /// <summary>The peer's signing public key (JWK); null once cut.</summary>
    public string? PeerSigningKey { get; set; }
    public string PeerSigningKid { get; set; } = "";
    /// <summary>The peer's encryption public key (JWK); null once cut.</summary>
    public string? PeerEncryptionKey { get; set; }
    public string PeerEncryptionKid { get; set; } = "";
    /// <summary>After the peer renewed its keys: its previous signing key, still accepted until <see cref="PeerPreviousUntil"/>.</summary>
    public string? PeerPreviousSigningKey { get; set; }
    public string? PeerPreviousSigningKid { get; set; }
    public DateTimeOffset? PeerPreviousUntil { get; set; }

    /// <summary>Four words computed from the four public keys, shown on both sides.</summary>
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset PairedAt { get; set; }
    /// <summary>The administrator who created the pairing code (<c>bootstrap</c> for NOOSPHERE_LINK_BOOTSTRAP_CODE).</summary>
    public string PairedBy { get; set; } = "";
    public DateTimeOffset? KeysRenewedAt { get; set; }
    public DateTimeOffset? LastExchangeAt { get; set; }
    public DateTimeOffset? CutAt { get; set; }
    /// <summary>Who cut it: an administrator's username, or <c>peer</c>.</summary>
    public string? CutBy { get; set; }
    /// <summary>A fictional peer of the demo: nothing is ever sent to it.</summary>
    public bool Demo { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// A single-use pairing code. Its <see cref="CodeId"/> (the first four characters) finds it; the code itself, the key of
/// the pairing MAC, is stored protected. Five wrong MACs burn it.
/// </summary>
public sealed class LinkCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CodeId { get; set; } = "";
    public string ProtectedCode { get; set; } = "";
    /// <summary>SHA-256 (hex) of the normalized code: the bootstrap code is created once.</summary>
    public string CodeHash { get; set; } = "";
    public List<string> Capabilities { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? LinkId { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? BurntAt { get; set; }
    /// <summary>Created from NOOSPHERE_LINK_BOOTSTRAP_CODE.</summary>
    public bool Bootstrap { get; set; }
}

public enum LinkDirection { In, Out }

/// <summary>Outcome of an exchange: done, refused (by the receiver), failed (network, bad response), or skipped (demo peer).</summary>
public enum LinkExchangeResult { Ok, Refused, Failed, Demo }

/// <summary>One exchange of the link protocol, sent or received, refusals included. Kept 90 days.</summary>
public sealed class LinkExchange
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    /// <summary>Null for a request whose key is unknown.</summary>
    public Guid? LinkId { get; set; }
    public LinkDirection Direction { get; set; }
    /// <summary><c>pair</c>, <c>status</c>, <c>catalog</c>, <c>notices</c>, <c>deployments</c>, <c>rekey</c>, <c>unpair</c>.</summary>
    public string Operation { get; set; } = "";
    public LinkExchangeResult Result { get; set; }
    /// <summary>HTTP status answered (received requests) or received (sent requests).</summary>
    public int? Status { get; set; }
    /// <summary>For a refusal: <c>unknown-key</c>, <c>link-cut</c>, <c>link-not-confirmed</c>, <c>clock-skew</c>, <c>replay</c>, <c>bad-digest</c>, <c>bad-signature</c>…</summary>
    public string? Reason { get; set; }
    public string? Detail { get; set; }
}

/// <summary>A signature nonce seen from a peer, refused again for 10 minutes (replay).</summary>
public sealed class LinkNonce
{
    public Guid LinkId { get; set; }
    public string Nonce { get; set; } = "";
    public DateTimeOffset SeenAt { get; set; }
}

public enum DeploymentPhase { Preparation, Execution, Rollback, Acceptance, Closed, Cancelled }

public enum DeploymentOutcome
{
    [JsonStringEnumMemberName("success")] Success,
    [JsonStringEnumMemberName("success-with-incidents")] SuccessWithIncidents,
    [JsonStringEnumMemberName("rolled-back")] RolledBack,
    [JsonStringEnumMemberName("failed")] Failed,
}

/// <summary>An environment a deployment goes to.</summary>
public sealed record DeploymentEnvironment(string Name, bool Production);

/// <summary>
/// A deployment (a MEP of gestion-mep) touching applications of the landscape, upserted by its id from deployment
/// events. Kept 365 days after it is closed or cancelled; it stays, without its link, when the link is cut.
/// </summary>
public sealed class Deployment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LinkId { get; set; }
    /// <summary>The MEP id on the gestion-mep side.</summary>
    public string DeploymentId { get; set; } = "";
    public string Title { get; set; } = "";
    public DeploymentPhase Phase { get; set; }
    public DeploymentOutcome? Outcome { get; set; }
    public bool Postponed { get; set; }
    public DateTimeOffset? PlannedStart { get; set; }
    public DateTimeOffset? PlannedEnd { get; set; }
    public string Url { get; set; } = "";
    /// <summary>Applications of this landscape it touches (unknown ids are left out).</summary>
    public List<string> ApplicationIds { get; set; } = [];
    /// <summary>JSON array of <see cref="DeploymentEnvironment"/>.</summary>
    public string Environments { get; set; } = "[]";
    /// <summary>When the event was last changed on the gestion-mep side.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    /// <summary>When it was first seen closed or cancelled: the 365-day retention starts there.</summary>
    public DateTimeOffset? EndedAt { get; set; }
}

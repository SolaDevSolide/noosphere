using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

// ---------- Links with gestion-mep (administrators) ----------

/// <summary>A new pairing code.</summary>
/// <param name="ExpiresInMinutes">1 to 1440; 15 by default.</param>
/// <param name="Capabilities">Granted to the link: <c>catalog:read</c>, <c>notices:send</c>, <c>deployments:receive</c>. All three by default.</param>
public sealed record LinkCodeInput(int? ExpiresInMinutes, List<string>? Capabilities);

public enum LinkCodeState { Active, Used, Expired, Burnt }

/// <summary>A pairing code, without its value (shown once, at creation).</summary>
/// <param name="CodeId">Its first four characters, sent by gestion-mep to find it.</param>
/// <param name="FailedAttempts">Wrong MACs so far; the fifth burns the code.</param>
/// <param name="Bootstrap">Created from NOOSPHERE_LINK_BOOTSTRAP_CODE.</param>
public sealed record LinkCodeDto(Guid Id, string CodeId, LinkCodeState State, IReadOnlyList<string> Capabilities, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, string CreatedBy, DateTimeOffset? UsedAt, Guid? LinkId, int FailedAttempts, bool Bootstrap);

/// <summary>A pairing code just created. <c>code</c> is shown only this once: enter it in gestion-mep with the URL of this instance.</summary>
/// <param name="PairUrl">The URL to enter in gestion-mep (this instance's public address).</param>
public sealed record LinkCodeCreatedDto(Guid Id, string Code, string CodeId, DateTimeOffset ExpiresAt, IReadOnlyList<string> Capabilities, string PairUrl);

/// <summary>Key ids (RFC 7638 thumbprints) of a link's four public keys; empty once cut.</summary>
public sealed record LinkKeyIdsDto(string Signing, string Encryption, string PeerSigning, string PeerEncryption);

/// <summary>A link with a gestion-mep instance.</summary>
/// <param name="State"><c>pending</c> until an administrator confirms the fingerprint here, <c>active</c> after, <c>cut</c> once cut by either side.</param>
/// <param name="PeerConfirmed">Whether the peer's administrator confirmed it, as last read from the peer; null until known.</param>
/// <param name="Fingerprint">Four words, the same on both sides: compare them before confirming.</param>
/// <param name="KeysRenewedAt">Last key renewal, by either side ("Keys renewed on …").</param>
/// <param name="CutBy">The administrator who cut it here, or <c>peer</c>.</param>
/// <param name="Demo">A fictional peer of the demo: nothing is sent to it.</param>
public sealed record LinkDto(Guid Id, string Product, string InstanceName, string Url, string? PeerVersion, LinkState State, bool ConfirmedHere,
    DateTimeOffset? ConfirmedAt, string? ConfirmedBy, bool? PeerConfirmed, DateTimeOffset? PeerCheckedAt, IReadOnlyList<string> Capabilities,
    string Fingerprint, DateTimeOffset PairedAt, string PairedBy, DateTimeOffset? KeysRenewedAt, DateTimeOffset? LastExchangeAt,
    DateTimeOffset? CutAt, string? CutBy, bool Demo, LinkKeyIdsDto Keys, int Version);

/// <summary>Confirms a link: the fingerprint shown on gestion-mep, typed or pasted (case and extra spaces do not matter).</summary>
public sealed record LinkConfirmInput(string? Fingerprint);

/// <summary>Replaces the capabilities of a link.</summary>
public sealed record LinkCapabilitiesInput(List<string>? Capabilities);

/// <summary>Cutting a link cannot be undone: <c>confirm</c> must be true.</summary>
public sealed record LinkCutInput(bool? Confirm);

/// <summary>One exchange with the peer, sent (<c>out</c>) or received (<c>in</c>).</summary>
/// <param name="Operation"><c>pair</c>, <c>status</c>, <c>catalog</c>, <c>notices</c>, <c>deployments</c>, <c>rekey</c> or <c>unpair</c>.</param>
/// <param name="Reason">For a refusal: <c>unknown-key</c>, <c>link-cut</c>, <c>link-not-confirmed</c>, <c>clock-skew</c>, <c>replay</c>, <c>bad-digest</c>, <c>bad-signature</c>...</param>
public sealed record LinkExchangeDto(long Id, DateTimeOffset At, Guid? LinkId, LinkDirection Direction, string Operation, LinkExchangeResult Result,
    int? Status, string? Reason, string? Detail);

/// <param name="NextCursor">Pass it as <c>cursor</c> for the next (older) page; null on the last page.</param>
public sealed record LinkExchangePageDto(IReadOnlyList<LinkExchangeDto> Items, string? NextCursor);

// ---------- Deployments received from gestion-mep ----------

public sealed record DeploymentEnvironmentDto(string Name, bool Production);

/// <summary>Where a deployment comes from.</summary>
/// <param name="LinkActive">False once the link is cut: the deployment stays as history.</param>
public sealed record DeploymentSourceDto(Guid LinkId, string Product, string InstanceName, bool LinkActive);

/// <summary>A deployment (a MEP of gestion-mep) touching the record.</summary>
/// <param name="Url">The page of the deployment in the application that sent it; kept once the link is cut, so the history still points to it.</param>
/// <param name="ApplicationIds">Applications it touches, among those the reader sees.</param>
/// <param name="UpdatedAt">Last change on the gestion-mep side.</param>
public sealed record DeploymentDto(string DeploymentId, string Title, DeploymentPhase Phase, DeploymentOutcome? Outcome, bool Postponed,
    DateTimeOffset? PlannedStart, DateTimeOffset? PlannedEnd, string Url, IReadOnlyList<string> ApplicationIds,
    IReadOnlyList<DeploymentEnvironmentDto> Environments, DateTimeOffset UpdatedAt, DateTimeOffset ReceivedAt, DeploymentSourceDto Source);

/// <summary>Deployments of a record.</summary>
/// <param name="Upcoming">Not closed nor cancelled, soonest first.</param>
/// <param name="Past">Closed or cancelled, latest first (the last three in a record's detail).</param>
public sealed record DeploymentsDto(IReadOnlyList<DeploymentDto> Upcoming, IReadOnlyList<DeploymentDto> Past);

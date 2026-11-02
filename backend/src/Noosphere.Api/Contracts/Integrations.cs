using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

// ---------- Simple connection for third-party tools: API keys and webhooks (administrators) ----------

/// <summary>A new API key.</summary>
/// <param name="Scopes"><c>catalog:read</c> and/or <c>deployments:write</c>; at least one.</param>
/// <param name="ExpiresInDays">1 to 3650; null: the key never expires.</param>
public sealed record ApiKeyInput(string? Name, List<string>? Scopes, int? ExpiresInDays = null);

public enum ApiKeyState { Active, Expired, Revoked }

/// <summary>An API key, without its secret (shown once, at creation).</summary>
/// <param name="Prefix">The start of the key (<c>nsk_</c> and 8 characters), to recognise it.</param>
public sealed record ApiKeyDto(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, ApiKeyState State, DateTimeOffset CreatedAt,
    string CreatedBy, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, string? RevokedBy);

/// <summary>An API key just created. <c>key</c> is shown only this once: store it in the tool's secrets.</summary>
public sealed record ApiKeyCreatedDto(ApiKeyDto ApiKey, string Key);

/// <summary>A new webhook, or its new settings.</summary>
/// <param name="Url">An HTTPS URL. Private, loopback and link-local addresses are refused unless the integration settings allow them.</param>
/// <param name="Events"><c>catalog.changed</c> and/or <c>deployment.updated</c>; at least one.</param>
/// <param name="Enabled">true by default.</param>
/// <param name="Version">On an update: the version read, to refuse a concurrent change.</param>
public sealed record WebhookInput(string? Name, string? Url, List<string>? Events, bool? Enabled = null, int? Version = null);

/// <summary>A webhook, without its secret.</summary>
/// <param name="Pending">Deliveries waiting for a (next) attempt.</param>
public sealed record WebhookDto(Guid Id, string Name, string Url, IReadOnlyList<string> Events, bool Enabled, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset? LastDeliveryAt, int Pending, int Version);

/// <summary>A webhook just created. <c>secret</c> (the HMAC-SHA256 key of <c>X-Noosphere-Signature</c>) is shown only this once.</summary>
public sealed record WebhookCreatedDto(WebhookDto Webhook, string Secret);

/// <summary>One event delivered (or to deliver) to a webhook.</summary>
/// <param name="EventId">Sent as <c>X-Noosphere-Delivery</c>, the same for every attempt.</param>
/// <param name="NextAttemptAt">When it is tried again (pending only).</param>
/// <param name="ResponseStatus">HTTP status answered by the receiver at the last attempt, if any.</param>
public sealed record WebhookDeliveryDto(long Id, Guid EventId, string EventType, WebhookDeliveryState State, int Attempts, DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt, int? ResponseStatus, string? Error, bool Test);

/// <param name="NextCursor">Pass it as <c>cursor</c> for the next (older) page; null on the last page.</param>
public sealed record WebhookDeliveryPageDto(IReadOnlyList<WebhookDeliveryDto> Items, string? NextCursor);

/// <summary>Instance-wide settings of integrations.</summary>
/// <param name="AllowPrivateAddresses">Webhooks may target private, loopback and link-local addresses, and plain HTTP. Off by default: turn it on only for receivers on a trusted network.</param>
public sealed record IntegrationSettingsDto(bool AllowPrivateAddresses, DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record IntegrationSettingsInput(bool? AllowPrivateAddresses);

// ---------- Called with an API key ----------

public sealed record IntegrationDomainDto(string Id, string Name);

public sealed record IntegrationTeamDto(string Id, string Name, string? Contact);

/// <param name="Kind"><c>app</c>, <c>bus</c>, <c>saas</c>, <c>data</c>, <c>file</c> or <c>external</c>.</param>
/// <param name="Lifecycle"><c>planned</c>, <c>live</c>, <c>retiring</c> or <c>retired</c>.</param>
/// <param name="Url">The record's page.</param>
public sealed record IntegrationApplicationDto(string Id, string Name, string? Description, string DomainId, string? TeamId, string Kind, string Lifecycle,
    string? Tech, string Url);

public sealed record IntegrationFlowDto(string Id, string Name, string FromId, string ToId, string Type, string Status, string? Schedule, string Url);

/// <summary>
/// One page of the shared catalogue (what everyone sees, like the catalogue of links): applications by id, and the flows
/// that start from them (each flow is on exactly one page). Domains and teams, short lists, are on every page.
/// </summary>
/// <param name="NextCursor">Pass it as <c>cursor</c> for the next page; null on the last one.</param>
public sealed record IntegrationCatalogPageDto(DateTimeOffset GeneratedAt, string InstanceName, string BaseUrl, IReadOnlyList<IntegrationDomainDto> Domains,
    IReadOnlyList<IntegrationTeamDto> Teams, IReadOnlyList<IntegrationApplicationDto> Applications, IReadOnlyList<IntegrationFlowDto> Flows, string? NextCursor);

/// <summary>A deployment event, in the same shape as the link protocol's.</summary>
/// <param name="DeploymentId">The deployment's id in the sending tool (at most 100 characters): events are upserted by it.</param>
/// <param name="Phase"><c>preparation</c>, <c>execution</c>, <c>rollback</c>, <c>acceptance</c>, <c>closed</c> or <c>cancelled</c>.</param>
/// <param name="Outcome"><c>success</c>, <c>success-with-incidents</c>, <c>rolled-back</c>, <c>failed</c> or null.</param>
/// <param name="Url">The deployment's page in the sending tool.</param>
/// <param name="ApplicationIds">Noosphere application ids; unknown or restricted ones are ignored and listed in the answer.</param>
/// <param name="UpdatedAt">Last change on the sender's side: an older event than the stored one changes nothing.</param>
public sealed record DeploymentEventInput(string DeploymentId, string Title, string Phase, string? Outcome, bool? Postponed, DateTimeOffset? PlannedStart,
    DateTimeOffset? PlannedEnd, string Url, List<string>? ApplicationIds, List<DeploymentEnvironmentDto>? Environments, DateTimeOffset UpdatedAt);

/// <summary>Body of <c>POST /api/integrations/deployments</c>: at most 500 events.</summary>
public sealed record DeploymentEventsInput(List<DeploymentEventInput> Events);

/// <param name="Accepted">Events received (created, updated, or already newer here).</param>
/// <param name="IgnoredApplicationIds">Application ids unknown here, or not shared, left out of the events.</param>
public sealed record DeploymentsReceiptDto(int Accepted, IReadOnlyList<string> IgnoredApplicationIds);

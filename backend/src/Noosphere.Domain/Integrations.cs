namespace Noosphere.Domain;

/// <summary>
/// An API key for a third-party tool that cannot speak the link protocol (a CI/CD pipeline, an ITSM tool, a script).
/// Created by an administrator, shown once, stored hashed: <see cref="Prefix"/> finds it, <see cref="SecretHash"/> (the
/// SHA-256 of the whole key) checks it. Calls use <c>Authorization: Bearer</c>, on the integration endpoints only.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>The start of the key (<c>nsk_</c> and 8 characters), shown in lists to recognise it.</summary>
    public string Prefix { get; set; } = "";
    /// <summary>SHA-256 (hex) of the whole key.</summary>
    public string SecretHash { get; set; } = "";
    /// <summary><c>catalog:read</c>, <c>deployments:write</c>.</summary>
    public List<string> Scopes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    /// <summary>Null: never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
}

/// <summary>
/// The answer to a <c>POST /api/integrations/deployments</c> sent with an <c>Idempotency-Key</c>: the same key and body
/// get the same answer again for 24 hours; the same key with another body is refused.
/// </summary>
public sealed class IdempotentRequest
{
    public Guid ApiKeyId { get; set; }
    public string Key { get; set; } = "";
    /// <summary>SHA-256 (hex) of the request body.</summary>
    public string RequestHash { get; set; } = "";
    public int Status { get; set; }
    public string Response { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An outgoing webhook: an HTTPS URL that receives events (change notices, deployments), signed with HMAC-SHA256 in
/// <c>X-Noosphere-Signature</c>. The secret is stored protected with ASP.NET Core Data Protection (it signs every delivery).
/// </summary>
public sealed class Webhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";
    /// <summary>Event types it receives: <c>catalog.changed</c>, <c>deployment.updated</c>.</summary>
    public List<string> Events { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? LastDeliveryAt { get; set; }
    public int Version { get; set; }
}

public enum WebhookDeliveryState { Pending, Delivered, Failed }

/// <summary>One event to deliver to a webhook, with its attempts: retried with a growing delay, then failed. Kept 90 days.</summary>
public sealed class WebhookDelivery
{
    public long Id { get; set; }
    public Guid WebhookId { get; set; }
    /// <summary>The same for every attempt (<c>X-Noosphere-Delivery</c>): receivers drop duplicates by it.</summary>
    public Guid EventId { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = "";
    /// <summary>The JSON body sent.</summary>
    public string Payload { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public WebhookDeliveryState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    /// <summary>HTTP status of the last attempt, if the receiver answered.</summary>
    public int? ResponseStatus { get; set; }
    public string? Error { get; set; }
    /// <summary>Sent with the test button.</summary>
    public bool Test { get; set; }
}

/// <summary>Instance-wide settings of integrations (one row).</summary>
public sealed class IntegrationSettings
{
    public int Id { get; set; } = 1;
    /// <summary>Webhooks may target private, loopback and link-local addresses (and plain HTTP). Off by default (SSRF).</summary>
    public bool AllowPrivateWebhookAddresses { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Where a deployment comes from: a link, or an API key.</summary>
public enum DeploymentSourceKind { Link, ApiKey }

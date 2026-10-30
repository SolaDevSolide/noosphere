using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

// ---------- Settings ----------

/// <summary>Settings of AI agents (MCP server, OAuth, llms.txt).</summary>
/// <param name="Enabled">The MCP server and the OAuth endpoints answer. Off: every agent is refused, and nobody can connect one.</param>
/// <param name="Ceiling"><c>public</c> (default): records visible to everyone, without protected notes. <c>public+protected</c>: the same with protected notes. <c>asUser</c>: what the person sees (restricted records also need the <c>docs:read:restricted</c> scope).</param>
/// <param name="QuotaPerHour">Calls per hour and per connection; over it, the connection is paused until the next hour.</param>
/// <param name="WhoConnects"><c>anyMember</c>: everyone connects agents for themselves. <c>approvalRequired</c>: a new connection waits for an administrator.</param>
/// <param name="DynamicRegistration">OAuth dynamic client registration (RFC 7591) is open, for clients without a Client ID Metadata Document.</param>
/// <param name="AnonymousLlms"><c>/llms.txt</c>, <c>/llms-full.txt</c> and the Markdown views answer without a session: records visible to everyone, without protected notes.</param>
/// <param name="McpUrl">Address of the MCP server, to paste in an agent's configuration.</param>
public sealed record AgentSettingsDto(
    bool Enabled, AgentCeiling Ceiling, int QuotaPerHour, AgentWhoConnects WhoConnects, bool DynamicRegistration, bool AnonymousLlms,
    string McpUrl, int Version, DateTimeOffset? UpdatedAt);

/// <param name="Version">The version that was read (409 when someone saved in between).</param>
public sealed record AgentSettingsInput(
    bool? Enabled, AgentCeiling? Ceiling, int? QuotaPerHour, AgentWhoConnects? WhoConnects, bool? DynamicRegistration, bool? AnonymousLlms, int? Version);

// ---------- Connections and activity ----------

/// <summary><c>pending</c>: waits for an administrator's approval; <c>paused</c>: over the hourly quota; <c>revoked</c>: disconnected.</summary>
public enum AgentState { Pending, Active, Paused, Revoked }

/// <summary>One call of an agent.</summary>
/// <param name="Tool">MCP tool (<c>search</c>, <c>draft_change</c>...), or <c>llms.txt</c>, <c>markdown</c>, <c>revoke</c>, <c>stop-all</c>.</param>
/// <param name="Kind"><c>read</c>, <c>draft</c>, <c>proposal</c>, <c>refused</c>, <c>quota</c> or <c>revoked</c>.</param>
/// <param name="Summary">In English, without the agent's name: "Read Billing (application)."</param>
/// <param name="RecordTitle">Name of the record now, when the reader may see it.</param>
public sealed record AgentActivityDto(
    long Id, DateTimeOffset At, Guid ConnectionId, string AgentName, UserRefDto Person, string Tool, AgentActivityKind Kind, string Summary,
    RecordKind? RecordKind, string? RecordId, string? RecordTitle);

/// <param name="NextCursor">Pass it as <c>cursor</c> for the next (older) page; null on the last page.</param>
public sealed record AgentActivityPageDto(IReadOnlyList<AgentActivityDto> Items, string? NextCursor);

/// <summary>An AI agent connected by a person.</summary>
/// <param name="ClientId">OAuth client id: the URL of its metadata document, or the id given at registration.</param>
/// <param name="Person">The person it acts for.</param>
/// <param name="Scopes"><c>docs:read</c>, <c>docs:draft</c>, <c>docs:read:restricted</c>.</param>
/// <param name="CallsLastHour">Calls since the start of the current hour (the quota window).</param>
/// <param name="OpenDrafts">Drafts it has not submitted yet.</param>
/// <param name="Demo">Made by the demo toolbox.</param>
public sealed record AgentConnectionDto(
    Guid Id, string ClientId, string ClientName, string? ClientUri, UserRefDto Person, IReadOnlyList<string> Scopes, AgentState State,
    DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt, DateTimeOffset? PausedUntil, DateTimeOffset? ApprovedAt,
    int CallsLastHour, int OpenDrafts, AgentActivityDto? LastActivity, bool Demo);

/// <summary>What the "My agents" page shows to its reader.</summary>
public sealed record AgentSettingsSummaryDto(bool Enabled, AgentCeiling Ceiling, AgentWhoConnects WhoConnects, int QuotaPerHour, string McpUrl);

/// <param name="Drafts">Drafts of the reader's agents (seen by nobody else but administrators).</param>
/// <param name="Proposals">Proposals the reader's agents submitted, newest first.</param>
public sealed record MyAgentsDto(
    IReadOnlyList<AgentConnectionDto> Connections, IReadOnlyList<ProposalDto> Drafts, IReadOnlyList<ProposalDto> Proposals, AgentSettingsSummaryDto Settings);

/// <summary>A text in the two languages of the interface.</summary>
public sealed record LocalizedTextDto(string En, string Fr);

/// <summary>One line of "What this agent can do".</summary>
/// <param name="Key"><c>read</c>, <c>draft</c>, <c>publish</c>, <c>restricted</c>, <c>protectedNotes</c> or <c>quota</c>.</param>
/// <param name="Allowed">false for what the agent cannot do.</param>
public sealed record CapabilityDto(string Key, bool Allowed, LocalizedTextDto Text);

/// <summary>"What this agent can do", in plain words: min(person's rights, ceiling, scopes).</summary>
public sealed record AgentCapabilitiesDto(
    Guid ConnectionId, string AgentName, UserRefDto Person, AgentCeiling Ceiling, IReadOnlyList<string> Scopes, IReadOnlyList<CapabilityDto> Items);

/// <param name="Confirm">Must be true.</param>
public sealed record StopAllInput(bool? Confirm);

public sealed record StopAllResultDto(int RevokedConnections, int RevokedTokens, DateTimeOffset At);

// ---------- OAuth consent page ----------

/// <summary><c>metadataDocument</c>: the client id is the https URL of its Client ID Metadata Document; <c>registered</c>: dynamic registration.</summary>
public enum OAuthClientKind { MetadataDocument, Registered }

/// <summary>A scope asked by the agent.</summary>
/// <param name="Grantable">false when the ceiling or the person's role does not allow it (it is then left out).</param>
public sealed record ConsentScopeDto(string Scope, bool Grantable, LocalizedTextDto Text);

/// <summary>What the consent page shows: who asks, for whom, and what it will be allowed to do.</summary>
/// <param name="RedirectHost">Where the browser goes back after the decision.</param>
/// <param name="AlreadyConnected">The person already has a connection with this client: approving replaces its access.</param>
/// <param name="ApprovalRequired">An administrator approves new connections before they work.</param>
public sealed record ConsentRequestDto(
    Guid Id, string ClientId, OAuthClientKind ClientKind, string ClientName, string? ClientUri, string RedirectHost, UserRefDto Person,
    IReadOnlyList<ConsentScopeDto> Scopes, IReadOnlyList<CapabilityDto> Capabilities, AgentCeiling Ceiling, bool ApprovalRequired,
    bool AlreadyConnected, DateTimeOffset ExpiresAt);

/// <param name="Scopes">Optional: grant fewer scopes than asked.</param>
public sealed record ConsentApproveInput(List<string>? Scopes);

/// <param name="RedirectTo">Where the consent page sends the browser: the agent's redirect URI with the code (or the error).</param>
/// <param name="PendingApproval">The connection waits for an administrator before it works.</param>
public sealed record ConsentResultDto(string RedirectTo, bool PendingApproval);

// ---------- Demo toolbox ----------

/// <summary>What the interface shows as a toast after a demo action, in the language of the signed-in user.</summary>
/// <param name="Link">A path of the interface to open (<c>/review/…</c>).</param>
public sealed record DemoNoticeDto(string Title, string Detail, string? Link);

/// <summary>A demo account, with what to try with it.</summary>
public sealed record DemoPersonaDto(string Username, string DisplayName, Role Role, IReadOnlyList<TeamRefDto> Teams, LocalizedTextDto WhatToTry, bool Current);

/// <summary>A demo action.</summary>
/// <param name="Id"><c>edit</c>, <c>proposal</c>, <c>decision</c>, <c>agent:draft</c>, <c>agent:submit</c>, <c>agent:refused</c>, <c>agent:quota</c>, <c>guest-read</c>, <c>reset</c>.</param>
/// <param name="Endpoint">The endpoint that runs it.</param>
public sealed record DemoScenarioDto(string Id, string Endpoint, LocalizedTextDto Label, LocalizedTextDto Description);

/// <param name="Current">The signed-in demo user; null when signed out.</param>
public sealed record DemoStateDto(IReadOnlyList<DemoPersonaDto> Personas, IReadOnlyList<DemoScenarioDto> Scenarios, string? Current, DemoNoticeDto Notice);

public sealed record DemoSwitchInput(string? Username);

public sealed record DemoSwitchResultDto(MeDto Me, DemoNoticeDto Notice);

/// <param name="Kind"><c>application</c>, <c>flow</c>, <c>dataObject</c> or <c>page</c>.</param>
public sealed record DemoRecordInput(string? Kind, string? Id);

/// <param name="Version">The new version: a save based on an older one now gets 409.</param>
/// <param name="OutdatedProposals">Open proposals on the record, outdated by this change.</param>
public sealed record DemoEditResultDto(RecordKind Kind, string Id, int Version, UserRefDto Author, IReadOnlyList<Guid> OutdatedProposals, DemoNoticeDto Notice);

public sealed record DemoProposalResultDto(Guid ProposalId, RecordKind Kind, string Id, UserRefDto Author, DemoNoticeDto Notice);

/// <param name="Decision"><c>accept</c> or <c>refuse</c>.</param>
/// <param name="Reason">For a refusal; a default reason is given when empty.</param>
public sealed record DemoDecisionInput(Guid? ProposalId, string? Decision, string? Reason = null);

public sealed record DemoDecisionResultDto(Guid ProposalId, ProposalStatus Status, UserRefDto DecidedBy, string? RefusalReason, DemoNoticeDto Notice);

/// <param name="Scenario"><c>draft</c>, <c>submit</c>, <c>refused</c> or <c>quota</c>.</param>
public sealed record DemoAgentInput(string? Scenario);

/// <param name="Activity">The activity lines written by the scenario, oldest first.</param>
public sealed record DemoAgentResultDto(string Scenario, Guid ConnectionId, IReadOnlyList<AgentActivityDto> Activity, Guid? ProposalId, DemoNoticeDto Notice);

/// <param name="Read">The guest could read the record in full.</param>
/// <param name="AccessLogLines">Access log lines written by the read.</param>
public sealed record DemoGuestReadResultDto(RecordKind Kind, string Id, bool Read, int AccessLogLines, DemoNoticeDto Notice);

public sealed record DemoResetInput(bool? Confirm);

public sealed record DemoResetResultDto(DemoNoticeDto Notice);

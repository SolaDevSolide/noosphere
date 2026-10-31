using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Noosphere.Api.Data;

namespace Noosphere.Api.Agents;

/// <summary>
/// The MCP tools of Noosphere. Each call runs with the rights of the person who connected the agent, limited by the
/// ceiling and the token's scopes, and writes one activity line. Refusals come back as tool errors with the reason;
/// the hourly quota is a JSON-RPC error.
/// </summary>
[McpServerToolType]
public sealed class NoosphereMcpTools
{
    public const string Instructions =
        "Noosphere documents an application landscape: applications, the flows between them, the data objects they carry, the owning teams, "
        + "business domains and documentation pages. Start with search or list_landscape, then read records with get_record (Markdown with a "
        + "YAML front matter). You never publish: draft_change prepares a draft that only the person you act for sees, submit_draft sends it to "
        + "the owning team, and get_proposal_status tells their decision. Never write secrets (passwords, tokens, keys): Noosphere refuses them; "
        + "write where they live instead. Some records are restricted or hold protected notes above your ceiling: you get the reason instead.";

    [McpServerTool(Name = "search", Title = "Search the landscape", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Searches applications, flows, data objects, teams, domains and documentation pages by name, id, technology or text "
        + "(accent- and case-insensitive, top 20). Returns kind, id, title, subtitle and the URL of each Markdown view. Use the kind and id with get_record.")]
    public static Task<string> Search(RequestContext<CallToolRequestParams> context,
        [Description("Words to look for, e.g. \"billing\", \"nightly import\", \"PostgreSQL\".")] string query,
        [Description("Optional: limit to these kinds: application, flow, dataObject, team, domain, page.")] string[]? kinds = null,
        CancellationToken cancellationToken = default) =>
        Run(context, "search", call => AgentTools.Search(call, query, kinds), cancellationToken);

    [McpServerTool(Name = "get_record", Title = "Read a record", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Reads one record as Markdown with a YAML front matter: its fields, flows in and out, data carried, pages, deployments "
        + "(upcoming and the last three, from gestion-mep) for applications and flows, and protected notes when the agent may read them. "
        + "Kinds: application, flow, dataObject, team, domain, page.")]
    public static Task<string> GetRecord(RequestContext<CallToolRequestParams> context,
        [Description("application, flow, dataObject, team, domain or page.")] string kind,
        [Description("The record id, a kebab-case slug such as \"billing\" or \"crm-to-billing-customers\".")] string id,
        CancellationToken cancellationToken = default) =>
        Run(context, "get_record", call => AgentTools.GetRecord(call, kind, id), cancellationToken);

    [McpServerTool(Name = "get_page", Title = "Read a documentation page", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Reads a documentation page as Markdown with a YAML front matter (title, owner, tags, links). Wiki links look like "
        + "[[application:billing]]: read their targets with get_record.")]
    public static Task<string> GetPage(RequestContext<CallToolRequestParams> context,
        [Description("The page id, e.g. \"replay-nightly-import\".")] string id,
        CancellationToken cancellationToken = default) =>
        Run(context, "get_page", call => AgentTools.GetPage(call, id), cancellationToken);

    [McpServerTool(Name = "list_landscape", Title = "List the landscape", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists domains, teams, applications (id, name, domain, team, kind, lifecycle, sensitivity) and the flows between them "
        + "(from, to, type, status, schedule, data objects). Filter by domain or team to keep the answer short.")]
    public static Task<string> ListLandscape(RequestContext<CallToolRequestParams> context,
        [Description("Optional domain id, e.g. \"finance\".")] string? domain = null,
        [Description("Optional team id, e.g. \"team-finance\".")] string? team = null,
        CancellationToken cancellationToken = default) =>
        Run(context, "list_landscape", call => AgentTools.ListLandscape(call, domain, team), cancellationToken);

    [McpServerTool(Name = "data_lineage", Title = "Trace a data object", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Traces a data object: its source application (source of truth), every flow carrying it with both ends, and the applications receiving it.")]
    public static Task<string> DataLineage(RequestContext<CallToolRequestParams> context,
        [Description("The data object id, e.g. \"customer\" or \"invoice\".")] string dataObjectId,
        CancellationToken cancellationToken = default) =>
        Run(context, "data_lineage", call => AgentTools.DataLineage(call, dataObjectId), cancellationToken);

    [McpServerTool(Name = "who_owns", Title = "Who owns a record", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Tells which team owns a record (its contact and members), who publishes changes to it, and how to propose one.")]
    public static Task<string> WhoOwns(RequestContext<CallToolRequestParams> context,
        [Description("application, flow, dataObject, page, domain or team.")] string kind,
        [Description("The record id.")] string id,
        CancellationToken cancellationToken = default) =>
        Run(context, "who_owns", call => AgentTools.WhoOwns(call, kind, id), cancellationToken);

    [McpServerTool(Name = "list_my_drafts", Title = "List my drafts", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the drafts this agent is working on and its proposals waiting for validation, with their ids and changes.")]
    public static Task<string> ListMyDrafts(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default) =>
        Run(context, "list_my_drafts", AgentTools.ListMyDrafts, cancellationToken);

    [McpServerTool(Name = "draft_change", Title = "Draft a change", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Creates or updates this agent's draft change to one record (application, flow, dataObject or page). Nothing is published: "
        + "only the person you act for sees the draft until submit_draft. For update, give only the fields to change (they are merged onto the "
        + "current record); for create, give the whole record; for delete, no fields. Field names are those of get_record's front matter in "
        + "camelCase: name, description, tech, teamId, domainId, lifecycle, schedule, dataObjectIds, title, body... Returns a validation summary: "
        + "changed fields, warnings, or the errors to fix. Secrets are refused.")]
    public static Task<string> DraftChange(RequestContext<CallToolRequestParams> context,
        [Description("application, flow, dataObject or page.")] string kind,
        [Description("The record id; null to create (the id is then derived from the name, or taken from fields.id).")] string? id,
        [Description("create, update or delete.")] string operation,
        [Description("The fields to set, e.g. {\"description\": \"…\"}.")] Dictionary<string, JsonElement>? fields = null,
        [Description("Why the change is needed: shown to the validators.")] string? comment = null,
        CancellationToken cancellationToken = default) =>
        Run(context, "draft_change", call => AgentTools.DraftChange(call, kind, id, operation, ToObject(fields), comment), cancellationToken);

    [McpServerTool(Name = "submit_draft", Title = "Submit a draft", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Submits one of this agent's drafts as a change proposal: the owning team (or an administrator) then accepts or refuses it, "
        + "with a reason. The agent never publishes.")]
    public static Task<string> SubmitDraft(RequestContext<CallToolRequestParams> context,
        [Description("The draftId returned by draft_change.")] string draftId,
        [Description("A short title for the validators, e.g. \"Billing: document the credit notes\".")] string title,
        [Description("Optional: the reasons and sources of the change.")] string? comment = null,
        CancellationToken cancellationToken = default) =>
        Run(context, "submit_draft", call => AgentTools.SubmitDraft(call, draftId, title, comment), cancellationToken);

    [McpServerTool(Name = "get_proposal_status", Title = "Proposal status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Tells the status of one of this agent's proposals: draft, submitted, accepted, refused (with the validator's reason) or withdrawn, "
        + "and whether the record changed since.")]
    public static Task<string> GetProposalStatus(RequestContext<CallToolRequestParams> context,
        [Description("The proposal id (the draftId).")] string id,
        CancellationToken cancellationToken = default) =>
        Run(context, "get_proposal_status", call => AgentTools.GetProposalStatus(call, id), cancellationToken);

    private static JsonObject? ToObject(Dictionary<string, JsonElement>? fields)
    {
        if (fields is null) return null;
        var o = new JsonObject();
        foreach (var (key, value) in fields) o[key] = JsonNode.Parse(value.GetRawText());
        return o;
    }

    /// <summary>Runs a tool: refusals become tool errors (the model reads the reason), the quota a JSON-RPC error.</summary>
    private static async Task<string> Run(RequestContext<CallToolRequestParams> context, string tool, Func<AgentCall, Task<string>> body, CancellationToken cancel)
    {
        var services = context.Services ?? throw new InvalidOperationException("No services for this MCP request.");
        var user = context.User;
        var connection = AgentBearerHandler.ConnectionOf(user) ?? throw new McpProtocolException("Not authenticated.", McpErrorCode.InvalidRequest);
        var db = services.GetRequiredService<NoosphereDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var http = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var settings = services.GetRequiredService<Settings>();
        var baseUrl = http is null ? settings.PublicUrl ?? "" : Api.Agents.PublicAddress.Of(http.Request, settings);
        try
        {
            return await AgentRuntime.Run(db, connection, AgentBearerHandler.ScopesOf(user), tool, now, baseUrl, body, cancel: cancel);
        }
        catch (AgentUnavailable e)
        {
            throw new McpProtocolException(e.Message, (McpErrorCode)AgentRuntime.QuotaErrorCode);
        }
        catch (AgentRefusal r)
        {
            throw new McpException(r.Message);
        }
    }
}

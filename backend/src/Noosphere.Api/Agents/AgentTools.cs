using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Contracts;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>
/// What agents can do, shared by the MCP tools and the demo toolbox. Every method works with the agent's limited rights
/// and records what the call did (<see cref="AgentCall.Log"/>); refusals throw <see cref="AgentRefusal"/> with the reason.
/// </summary>
public static class AgentTools
{
    public static string ToJson(object value) => JsonSerializer.Serialize(value, Records.Json);

    private static readonly RecordKind[] SearchKinds = [RecordKind.Application, RecordKind.Flow, RecordKind.DataObject, RecordKind.Team, RecordKind.Domain, RecordKind.Page];

    // ---------- Reading ----------

    public static async Task<string> Search(AgentCall call, string? query, IReadOnlyList<string>? kinds)
    {
        call.Require(Scopes.Read);
        var text = query?.Trim() ?? "";
        if (text.Length == 0) throw new AgentRefusal("Give a query: a word of a name, an id, a technology or a page text.", "invalid", AgentActivityKind.Read, summary: "Searched with an empty query.");
        var wanted = new HashSet<RecordKind>();
        foreach (var k in kinds ?? [])
            wanted.Add(MarkdownViews.ParseKind(k) is { } kind && SearchKinds.Contains(kind) ? kind
                : throw new AgentRefusal($"Unknown kind '{k}'. Kinds: application, flow, dataObject, team, domain, page.", "invalid", AgentActivityKind.Read, summary: $"Searched \"{text}\" with an unknown kind."));
        var views = await MarkdownViews.Load(call.Db, call.Access, call.BaseUrl);
        var pages = await call.Db.Pages.AsNoTracking().ToListAsync();
        var results = LandscapeQueries.Search(views.Snapshot, call.Access, text, pages)
            .Select(r => (Kind: Kind(r.Kind), r))
            .Where(r => wanted.Count == 0 || wanted.Contains(r.Kind))
            // Outline records (a guest's view) are not readable: they are left out.
            .Where(r => call.Access.Sight(r.Kind, r.r.Id) == Sight.Full)
            .Select(r => new { kind = Rules.KindName(r.Kind), id = r.r.Id, title = r.r.Title, subtitle = r.r.Subtitle, url = views.Url(r.Kind, r.r.Id) })
            .ToList();
        call.Log(AgentActivityKind.Read, $"Searched \"{Clip(text, 80)}\": {results.Count} result(s).");
        return ToJson(new { query = text, count = results.Count, results });
    }

    public static async Task<string> GetRecord(AgentCall call, string? kindName, string? id)
    {
        call.Require(Scopes.Read);
        var kind = MarkdownViews.ParseKind(kindName)
            ?? throw new AgentRefusal($"Unknown kind '{kindName}'. Kinds: application, flow, dataObject, team, domain, page.", "invalid", AgentActivityKind.Read, summary: $"Asked for an unknown kind '{Clip(kindName ?? "", 40)}'.");
        var key = id?.Trim() ?? "";
        var views = await MarkdownViews.Load(call.Db, call.Access, call.BaseUrl);
        CheckReadable(call, views, kind, key);
        var markdown = views.Record(kind, key)!;
        var title = call.Access.Facts.Title(kind, key) ?? key;
        var withheld = views.NotesWithheld(kind, key);
        var shown = views.NotesShown(kind, key);
        await LogAccess(call, kind, key, shown);
        call.Log(AgentActivityKind.Read, $"Read {title} ({Noun(kind)})" + (withheld ? "; protected notes withheld (above the agent ceiling)." : "."), kind, key);
        return withheld
            ? markdown + "\n> Protected notes exist on this record but are above the agent ceiling: the agent cannot read them.\n"
            : markdown;
    }

    /// <summary>Refuses a record the agent may not read, with the reason: missing, the ceiling, or the person's own visibility.</summary>
    public static void CheckReadable(AgentCall call, MarkdownViews views, RecordKind kind, string id)
    {
        var noun = Noun(kind);
        if (id.Length == 0 || !views.Exists(kind, id))
            throw new AgentRefusal($"No {noun} with the id '{id}'. Use search to find ids.", "notFound", AgentActivityKind.Read, kind, null, $"Asked for an unknown {noun} '{Clip(id, 64)}'.");
        switch (call.Access.Sight(kind, id))
        {
            case Sight.Full: return;
            case Sight.Outline:
                throw new AgentRefusal($"Refused (visibility): the {noun} '{id}' is not shared with {call.Person.DisplayName}, who is a guest.", "visibility", recordKind: kind, recordId: id);
        }
        if (call.PersonAccess.Sight(kind, id) == Sight.Full)
            throw new AgentRefusal(
                $"Refused (ceiling): the {noun} '{id}' is restricted. {call.Person.DisplayName} can see it, but the agent ceiling of this instance "
                + (call.Settings.Ceiling == AgentCeiling.AsUser
                    ? $"needs the {Scopes.ReadRestricted} scope for restricted records: connect the agent again and allow it."
                    : "only lets agents read records visible to everyone. An administrator can raise the ceiling."),
                "ceiling", recordKind: kind, recordId: id);
        throw new AgentRefusal($"Refused (visibility): the {noun} '{id}' is restricted and {call.Person.DisplayName} is not on its access list.", "visibility", recordKind: kind, recordId: id);
    }

    public static Task<string> GetPage(AgentCall call, string? id) => GetRecord(call, "page", id);

    public static async Task<string> ListLandscape(AgentCall call, string? domain, string? team)
    {
        call.Require(Scopes.Read);
        var views = await MarkdownViews.Load(call.Db, call.Access, call.BaseUrl);
        var s = views.Snapshot;
        var filter = new Filter(Blank(domain), Blank(team), null, null, null, null);
        var landscape = LandscapeQueries.Landscape(s, call.Access, filter, call.Now);
        var apps = landscape.Applications.OfType<ApplicationDto>().Select(a => new
        {
            a.Id, a.Name, a.DomainId, a.TeamId, kind = a.Kind, a.Lifecycle, a.Sensitivity, url = views.Url(RecordKind.Application, a.Id),
        }).ToList();
        var flows = landscape.Flows.OfType<FlowDto>().Select(f => new
        {
            f.Id, f.Name, f.FromId, f.ToId, f.Type, f.Status, f.Schedule, f.DataObjectIds, f.Sensitivity, url = views.Url(RecordKind.Flow, f.Id),
        }).ToList();
        var hidden = landscape.Applications.Count - apps.Count;
        call.Log(AgentActivityKind.Read, $"Listed the landscape{Scope(domain, team)}: {apps.Count} application(s), {flows.Count} flow(s).");
        return ToJson(new
        {
            domains = landscape.Domains.Select(d => new { d.Id, d.Name }),
            teams = landscape.Teams.Select(t => new { t.Id, t.Name, t.Contact }),
            applications = apps,
            flows,
            notVisible = hidden > 0 ? $"{hidden} application(s) are restricted or not shared and are left out." : null,
        });
    }

    public static async Task<string> DataLineage(AgentCall call, string? dataObjectId)
    {
        call.Require(Scopes.Read);
        var id = dataObjectId?.Trim() ?? "";
        var views = await MarkdownViews.Load(call.Db, call.Access, call.BaseUrl);
        CheckReadable(call, views, RecordKind.DataObject, id);
        var s = views.Snapshot;
        var o = s.ObjectsById[id];
        object? App(string? appId) => appId is null || call.Access.Sight(RecordKind.Application, appId) != Sight.Full || !s.AppsById.TryGetValue(appId, out var a)
            ? (appId is null ? null : new { id = (string?)null, name = "a restricted application", url = (string?)null })
            : new { id = (string?)a.Id, name = a.Name, url = (string?)views.Url(RecordKind.Application, a.Id) };
        var flows = s.Flows.Where(f => f.DataObjectIds.Contains(id) && call.Access.Sight(RecordKind.Flow, f.Id) == Sight.Full).ToList();
        var consumers = flows.Select(f => f.ToId).Distinct().Where(a => call.Access.Sight(RecordKind.Application, a) == Sight.Full).ToList();
        call.Log(AgentActivityKind.Read, $"Traced the lineage of {o.Name} (data object): {flows.Count} flow(s).", RecordKind.DataObject, id);
        return ToJson(new
        {
            dataObject = new { o.Id, o.Name, o.Sensitivity, o.Description, url = views.Url(RecordKind.DataObject, id) },
            source = App(o.SourceApplicationId),
            flows = flows.Select(f => new { f.Id, f.Name, from = App(f.FromId), to = App(f.ToId), f.Type, f.Status, f.Schedule, url = views.Url(RecordKind.Flow, f.Id) }),
            consumers = consumers.Select(App),
        });
    }

    public static async Task<string> WhoOwns(AgentCall call, string? kindName, string? id)
    {
        call.Require(Scopes.Read);
        var kind = MarkdownViews.ParseKind(kindName)
            ?? throw new AgentRefusal($"Unknown kind '{kindName}'. Kinds: application, flow, dataObject, team, domain, page.", "invalid", AgentActivityKind.Read, summary: "Asked who owns an unknown kind.");
        var key = id?.Trim() ?? "";
        var views = await MarkdownViews.Load(call.Db, call.Access, call.BaseUrl);
        CheckReadable(call, views, kind, key);
        var facts = call.Access.Facts;
        var owner = kind is RecordKind.Domain or RecordKind.Team ? null : facts.Owner(kind, key);
        var team = owner is null ? null : views.Snapshot.Teams.FirstOrDefault(t => t.Id == owner);
        var members = owner is null ? [] : await call.Db.TeamMembers.Where(m => m.TeamId == owner).Join(call.Db.Users, m => m.UserId, u => u.Id, (_, u) => u)
            .OrderBy(u => u.DisplayName).Select(u => new { u.Username, u.DisplayName }).ToListAsync();
        var title = facts.Title(kind, key) ?? key;
        call.Log(AgentActivityKind.Read, $"Asked who owns {title} ({Noun(kind)}).", kind, key);
        return ToJson(new
        {
            kind = Rules.KindName(kind), id = key, title,
            owningTeam = team is null ? null : new { team.Id, team.Name, team.Contact, members },
            publishers = owner is null ? "Administrators only." : $"Members of {team?.Name ?? owner} and administrators.",
            howToChange = kind is RecordKind.Domain or RecordKind.Team
                ? "Domains and teams are changed by administrators only, not through proposals."
                : $"Prepare a draft with draft_change, then submit_draft: {(owner is null ? "an administrator" : $"a member of {team?.Name ?? owner}")} validates it. Agents never publish.",
        });
    }

    // ---------- Drafts and proposals ----------

    public static async Task<string> ListMyDrafts(AgentCall call)
    {
        call.Require(Scopes.Draft);
        var list = await Proposals.Query(call.Db).AsNoTracking()
            .Where(p => p.AgentConnectionId == call.Connection.Id && (p.Status == ProposalStatus.Draft || p.Status == ProposalStatus.Submitted))
            .OrderByDescending(p => p.UpdatedAt).ToListAsync();
        call.Log(AgentActivityKind.Read, $"Listed its drafts and open proposals: {list.Count}.");
        return ToJson(new
        {
            count = list.Count,
            items = list.Select(p => new
            {
                id = p.Id, p.Title, p.Status, p.UpdatedAt, p.SubmittedAt,
                changes = p.Changes.OrderBy(c => c.Position).Select(c => new { kind = Rules.KindName(c.Kind), targetId = c.TargetId ?? Records.ParseObject(c.After)?["id"]?.GetValue<string>(), c.Operation }),
            }),
        });
    }

    /// <summary>
    /// Creates or updates the agent's draft for one record: <paramref name="fields"/> are merged onto the current record
    /// (update), or are the whole new record (create). Returns a validation summary; secrets and fields above the ceiling
    /// are refused.
    /// </summary>
    public static async Task<string> DraftChange(AgentCall call, string? kindName, string? id, string? operationName, JsonObject? fields, string? comment)
    {
        call.Require(Scopes.Draft);
        if (!call.Access.Viewer.CanEdit)
            throw new AgentRefusal($"Refused (role): {call.Person.DisplayName} is a guest and cannot propose changes.", "role");
        var kind = MarkdownViews.ParseKind(kindName);
        if (kind is not { } k || !Records.Of(k).Proposable)
            throw new AgentRefusal($"Unknown or unchangeable kind '{kindName}'. Agents draft changes to: application, flow, dataObject, page.", "invalid", AgentActivityKind.Draft, summary: "Draft refused: unknown kind.");
        var type = Records.Of(k);
        if (!Enum.TryParse<ChangeOperation>(operationName?.Trim(), ignoreCase: true, out var operation) || !Enum.IsDefined(operation))
            throw new AgentRefusal("The operation is create, update or delete.", "invalid", AgentActivityKind.Draft, summary: "Draft refused: unknown operation.");
        var key = Blank(id);
        var views = await MarkdownViews.Load(call.Db, call.Access, call.BaseUrl);
        VersionedEntity? current = null;
        if (operation != ChangeOperation.Create)
        {
            if (key is null) throw new AgentRefusal($"Give the id of the {type.Noun} to {operation.ToString().ToLowerInvariant()}.", "invalid", AgentActivityKind.Draft, summary: "Draft refused: no id.");
            CheckReadable(call, views, k, key);
            current = await type.Find(call.Db, key, tracked: false);
        }
        var known = type.Fields.Select(f => f.Field).Append("id").ToHashSet(StringComparer.Ordinal);
        if (fields?.Select(f => f.Key).FirstOrDefault(f => !known.Contains(f)) is { } unknown)
            throw new AgentRefusal($"Unknown field '{unknown}'. Fields of a {type.Noun}: {string.Join(", ", type.Fields.Select(f => f.Field))}.", "invalid", AgentActivityKind.Draft, k, key,
                $"Draft refused on {type.Noun} '{key ?? "new"}': unknown field '{Clip(unknown, 40)}'.");
        var readsNotes = current is null ? call.Access.Limits is not { ReadsProtectedNotes: false } : call.Access.ReadsProtected(k, current);
        if (fields?.ContainsKey(ProtectedNotesMask.Field) == true && !readsNotes)
            throw new AgentRefusal("Refused (protected notes): protected notes are above the agent ceiling; the agent cannot read or change them.", "protectedNotes", AgentActivityKind.Refused, k, key);

        JsonObject? after = null;
        if (operation == ChangeOperation.Update)
        {
            if (fields is null || fields.Count == 0) throw new AgentRefusal("Give the fields to change.", "invalid", AgentActivityKind.Draft, k, key, $"Draft refused on {type.Noun} '{key}': no field given.");
            after = type.Snapshot(current!);
            foreach (var (name, value) in fields) after[name] = value?.DeepClone();
        }
        else if (operation == ChangeOperation.Create)
        {
            after = (JsonObject?)fields?.DeepClone() ?? [];
            if (key is not null) after["id"] = key;
        }

        // The agent's open draft on the same record is updated rather than duplicated.
        var drafts = await Proposals.Query(call.Db).Where(p => p.AgentConnectionId == call.Connection.Id && p.Status == ProposalStatus.Draft).ToListAsync();
        var targetKey = key ?? (after?["id"] as JsonValue)?.GetValue<string>();
        var draft = targetKey is null ? null : drafts.FirstOrDefault(p => p.Changes.Count == 1 && p.Changes[0].Kind == k
            && (p.Changes[0].TargetId ?? (Records.ParseObject(p.Changes[0].After)?["id"] as JsonValue)?.GetValue<string>()) == targetKey);
        var isNew = draft is null;
        draft ??= new Proposal
        {
            AuthorId = call.Person.Id, AuthorName = call.Person.Username, AuthorKind = AuthorKind.Agent, OnBehalfOf = call.Person.Username,
            AgentConnectionId = call.Connection.Id, Status = ProposalStatus.Draft, CreatedAt = call.Now, Version = 0,
        };
        var input = new ProposalChangeInput(k, key, operation, current?.Version, after);
        List<ProposalChange> changes;
        var errors = new FieldErrors();
        var commentText = errors.Optional("comment", comment, Rules.MaxTextLength);
        try
        {
            errors.ThrowIfAny();
            changes = await Proposals.BuildChanges(call.Db, draft.Id, [input], call.Access);
        }
        catch (ApiProblem p) when (p.Errors is not null)
        {
            var secret = p.Status == 422;
            var fieldErrors = p.Errors.ToDictionary(e => e.Key.Replace("changes[0].after.", "", StringComparison.Ordinal), e => e.Value);
            var summary = ToJson(new
            {
                saved = false, secretRefused = secret, errors = fieldErrors,
                message = secret
                    ? "Refused (secret): a field looks like a secret. Noosphere is documentation, not a vault: write where the secret lives (a vault path), never its value."
                    : "The draft was not saved: fix the fields listed in errors and call draft_change again.",
            });
            throw new AgentRefusal(summary, secret ? "secret" : "invalid", secret ? AgentActivityKind.Refused : AgentActivityKind.Draft, k, key,
                secret ? $"Refused a draft on {type.Noun} '{key ?? targetKey ?? "new"}': {string.Join(", ", fieldErrors.Keys)} looks like a secret."
                       : $"Draft on {type.Noun} '{key ?? targetKey ?? "new"}' not saved: {fieldErrors.Count} invalid field(s).");
        }
        var change = changes[0];
        var title = type.Title(Records.ParseObject(change.After ?? change.Before)!);
        draft.Title = $"{operation switch { ChangeOperation.Create => "Create", ChangeOperation.Delete => "Delete", _ => "Update" }} {title} ({type.Noun})";
        if (draft.Title.Length > Rules.MaxNameLength) draft.Title = draft.Title[..Rules.MaxNameLength];
        draft.Comment = commentText ?? draft.Comment;
        draft.UpdatedAt = call.Now;
        draft.Version++;
        if (isNew)
        {
            draft.Changes = changes;
            call.Db.Proposals.Add(draft);
        }
        else
        {
            call.Db.ProposalChanges.RemoveRange(draft.Changes);
            draft.Changes.Clear();
            await call.Db.SaveChangesAsync();
            draft.Changes.AddRange(changes);
            call.Db.ProposalChanges.AddRange(changes);
        }
        await call.Db.SaveChangesAsync();

        var readable = current is not null ? call.Access.ReadsProtected(k, current) : readsNotes;
        var diff = ProtectedNotesMask.Mask(Diffs.Compare(type, Records.ParseObject(change.Before), Records.ParseObject(change.After), call.Access.Facts, call.Access), !readable, !readable);
        var target = change.TargetId ?? targetKey;
        var warnings = new List<string>();
        if (!readable && call.Access.Facts.HasProtectedNotes(k, target ?? "")) warnings.Add("The record has protected notes above the agent ceiling: they are kept unchanged.");
        if (operation == ChangeOperation.Update && diff.Count == 0) warnings.Add("The fields given equal the current record: the draft changes nothing.");
        call.Log(AgentActivityKind.Draft, $"{(isNew ? "Drafted" : "Updated its draft of")} a change to {title} ({type.Noun})"
            + (diff.Count > 0 ? ": " + string.Join(", ", diff.Select(d => d.Field)) + "." : "."), k, target);
        return ToJson(new
        {
            saved = true, draftId = draft.Id, status = "draft", title = draft.Title, kind = Rules.KindName(k), targetId = target, operation,
            changedFields = diff.Select(d => new { d.Field, d.Label, d.Before, d.After }),
            warnings,
            next = $"Call submit_draft with draftId {draft.Id} when it is ready: the owners then validate it. {call.Person.DisplayName} can also review and submit it from \"My agents\". Nobody else sees the draft before.",
        });
    }

    public static async Task<string> SubmitDraft(AgentCall call, string? draftId, string? title, string? comment)
    {
        call.Require(Scopes.Draft);
        var draft = await OwnProposal(call, draftId, tracked: true);
        if (draft.Status != ProposalStatus.Draft)
            throw new AgentRefusal($"This proposal is already {Status(draft.Status)}: use get_proposal_status.", "invalid", AgentActivityKind.Proposal,
                summary: $"Tried to submit \"{Clip(draft.Title, 80)}\", already {Status(draft.Status)}.");
        var errors = new FieldErrors();
        var name = errors.Required("title", string.IsNullOrWhiteSpace(title) ? draft.Title : title);
        var text = errors.Optional("comment", comment, Rules.MaxTextLength);
        try
        {
            errors.ThrowIfAny();
        }
        catch (ApiProblem p) when (p.Errors is not null)
        {
            var secret = p.Status == 422;
            throw new AgentRefusal(ToJson(new { submitted = false, secretRefused = secret, errors = p.Errors, message = secret
                    ? "Refused (secret): the title or comment looks like a secret. Write where it lives instead."
                    : "Fix the title or comment and call submit_draft again." }),
                secret ? "secret" : "invalid", secret ? AgentActivityKind.Refused : AgentActivityKind.Proposal,
                summary: secret ? $"Refused to submit \"{Clip(draft.Title, 80)}\": the {string.Join(", ", p.Errors.Keys)} looks like a secret." : "Submission refused: invalid title or comment.");
        }
        (draft.Title, draft.Comment, draft.Status, draft.SubmittedAt, draft.UpdatedAt) = (name, text ?? draft.Comment, ProposalStatus.Submitted, call.Now, call.Now);
        draft.Version++;
        await call.Db.SaveChangesAsync();
        var owners = Proposals.Owners(draft, call.Access.Facts);
        var first = draft.Changes.OrderBy(c => c.Position).First();
        call.Log(AgentActivityKind.Proposal, $"Submitted the proposal \"{Clip(name, 120)}\".", first.Kind, first.TargetId);
        return ToJson(new
        {
            submitted = true, proposalId = draft.Id, status = "submitted", title = name,
            validators = owners.Select(o => o is null ? "administrators" : call.Access.Facts.TeamName(o) ?? o),
            link = $"{call.BaseUrl}/review/{draft.Id}",
            next = "The owning team reviews it. Call get_proposal_status later to know the decision and, on a refusal, the reason.",
        });
    }

    public static async Task<string> GetProposalStatus(AgentCall call, string? proposalId)
    {
        call.Require(Scopes.Draft);
        var p = await OwnProposal(call, proposalId, tracked: false);
        var people = await People.Load(call.Db);
        var dto = await Proposals.Dto(call.Db, p, call.Access, people);
        call.Log(AgentActivityKind.Read, $"Checked the status of \"{Clip(p.Title, 120)}\": {Status(p.Status)}.");
        return ToJson(new
        {
            id = p.Id, p.Title, status = p.Status, dto.Outdated, p.SubmittedAt, p.DecidedAt,
            decidedBy = dto.DecidedBy?.DisplayName, refusalReason = p.RefusalReason,
            validators = dto.OwnerTeams.Select(t => new { t.Name, approved = t.ApprovedBy is not null }),
            requiresAdmin = dto.RequiresAdmin,
            hint = p.Status switch
            {
                ProposalStatus.Refused => "Refused: read refusalReason; prepare a new draft if it still makes sense.",
                ProposalStatus.Accepted => "Accepted and published.",
                ProposalStatus.Submitted when dto.Outdated => "The record changed since the proposal was written: the person can update it from the latest version.",
                ProposalStatus.Submitted => "Waiting for the owning team.",
                ProposalStatus.Draft => "Still a draft: call submit_draft when ready.",
                _ => "Withdrawn.",
            },
        });
    }

    private static async Task<Proposal> OwnProposal(AgentCall call, string? id, bool tracked)
    {
        if (!Guid.TryParse(id?.Trim(), out var guid)) throw new AgentRefusal("Give the proposal id returned by draft_change or list_my_drafts.", "invalid", AgentActivityKind.Read, summary: "Asked for a proposal with an invalid id.");
        var query = Proposals.Query(call.Db);
        var p = await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(x => x.Id == guid && x.AgentConnectionId == call.Connection.Id);
        return p ?? throw new AgentRefusal($"No proposal '{guid}' written by this agent.", "notFound", AgentActivityKind.Read, summary: "Asked for an unknown proposal.");
    }

    // ---------- Access log ----------

    /// <summary>Agents' reads of restricted records and protected notes go to the access log, under the person's name.</summary>
    public static async Task LogAccess(AgentCall call, RecordKind kind, string id, bool notesShown)
    {
        var facts = call.Access.Facts;
        await AccessLogs.RecordRead(call.Db, call.Access, kind, id, facts.Owner(kind, id), facts.Restricted(kind, id), notesShown, call.Now,
            $"AI agent: {call.Connection.ClientName}");
    }

    // ---------- Helpers ----------

    public static string Noun(RecordKind kind) => Records.Of(kind).Noun;

    private static RecordKind Kind(SearchKind kind) => kind switch
    {
        SearchKind.Application => RecordKind.Application,
        SearchKind.Flow => RecordKind.Flow,
        SearchKind.DataObject => RecordKind.DataObject,
        SearchKind.Team => RecordKind.Team,
        SearchKind.Domain => RecordKind.Domain,
        _ => RecordKind.Page,
    };

    private static string Status(ProposalStatus s) => s == ProposalStatus.Submitted ? "submitted" : s.ToString().ToLowerInvariant();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Scope(string? domain, string? team) =>
        (Blank(domain), Blank(team)) switch
        {
            (null, null) => "",
            ({ } d, null) => $" of domain {d}",
            (null, { } t) => $" of team {t}",
            var (d, t) => $" of domain {d} and team {t}",
        };

    public static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

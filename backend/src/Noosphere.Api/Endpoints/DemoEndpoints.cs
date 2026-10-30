using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Agents;
using Noosphere.Api.Auth;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Endpoints;

/// <summary>
/// The demo toolbox, mapped only in demo mode (otherwise every <c>/api/demo/*</c> route is an unknown route: 404). It
/// switches between the demo accounts and simulates what other people and agents do, through the same services as the
/// real features. Every response carries a <c>notice</c> for the interface's toast, in the signed-in user's language.
/// </summary>
public static class DemoEndpoints
{
    public const string AgentClientId = "noosphere-demo-assistant";
    public const string AgentName = "Demo assistant";

    private static readonly Dictionary<string, LocalizedTextDto> WhatToTry = new()
    {
        ["alice"] = new("Administrator: open the agents console, change the agent ceiling, try the emergency stop, and read the access log.",
            "Administratrice : ouvrez la console des agents, changez le plafond, essayez l'arrêt d'urgence et lisez le journal des accès."),
        ["bob"] = new("Team Finance and Team Payments: validate David's proposals on Billing, edit Billing while a colleague changes it, and connect an AI agent.",
            "Équipes Finance et Paiements : validez les propositions de David sur Facturation, modifiez Facturation pendant qu'un collègue la change, et connectez un agent IA."),
        ["claire"] = new("Guest: you see the customer portal in full because it is shared with you; everything else shows its outline only.",
            "Invitée : vous voyez le portail client en entier car il vous est partagé ; le reste n'apparaît qu'en aperçu."),
        ["david"] = new("Team CRM: propose a change to a Finance record, follow its validation, and let an agent draft a change for you.",
            "Équipe CRM : proposez une modification d'une fiche de Finance, suivez sa validation, et laissez un agent rédiger un brouillon pour vous."),
    };

    private static readonly DemoScenarioDto[] Scenarios =
    [
        new("edit", "/api/demo/simulate/edit", new("A colleague edits a record", "Un collègue modifie une fiche"),
            new("Someone of the owning team publishes a small change: your next save of the old version gets a conflict, and open proposals become outdated.",
                "Un membre de l'équipe propriétaire publie une petite modification : votre prochain enregistrement de l'ancienne version est en conflit, et les propositions ouvertes deviennent obsolètes.")),
        new("proposal", "/api/demo/simulate/proposal", new("Receive a proposal", "Recevoir une proposition"),
            new("A member of another team proposes a change to a record of your team.", "Un membre d'une autre équipe propose une modification d'une fiche de votre équipe.")),
        new("decision", "/api/demo/simulate/decision", new("Get a decision", "Obtenir une décision"),
            new("A validator accepts or refuses one of your proposals; a refusal comes with a reason.", "Un validateur accepte ou refuse une de vos propositions ; un refus est motivé.")),
        new("agent:draft", "/api/demo/simulate/agent", new("An agent drafts a change", "Un agent rédige un brouillon"),
            new("A demo AI agent connected for you searches, reads a record and writes a draft only you see.", "Un agent IA de démonstration connecté pour vous cherche, lit une fiche et écrit un brouillon que vous seul voyez.")),
        new("agent:submit", "/api/demo/simulate/agent", new("An agent submits a proposal", "Un agent soumet une proposition"),
            new("The demo agent drafts a change and submits it to the owning team.", "L'agent de démonstration rédige une modification et la soumet à l'équipe propriétaire.")),
        new("agent:refused", "/api/demo/simulate/agent", new("An agent is refused", "Un agent est refusé"),
            new("The demo agent tries to read a restricted record and to write a secret: both are refused, with the reason.", "L'agent de démonstration tente de lire une fiche restreinte et d'écrire un secret : les deux sont refusés, avec la raison.")),
        new("agent:quota", "/api/demo/simulate/agent", new("An agent hits its quota", "Un agent atteint son quota"),
            new("The demo agent calls too often: it is paused until the next hour (a small quota is used for the demo).", "L'agent de démonstration appelle trop souvent : il est mis en pause jusqu'à l'heure suivante (un petit quota est utilisé pour la démo).")),
        new("guest-read", "/api/demo/simulate/guest-read", new("The guest reads a record", "L'invitée lit une fiche"),
            new("Claire (guest) reads a shared or restricted record: the access log records it.", "Claire (invitée) lit une fiche partagée ou restreinte : le journal des accès l'enregistre.")),
        new("reset", "/api/demo/reset", new("Reset the demo", "Réinitialiser la démo"),
            new("Reloads the demo landscape, proposals and agents; the demo accounts stay.", "Recharge le paysage, les propositions et les agents de démonstration ; les comptes restent.")),
    ];

    public static void MapDemo(this RouteGroupBuilder api)
    {
        var demo = api.MapGroup("/demo").WithTags("Demo");

        demo.MapGet("/state", async (ClaimsPrincipal user, NoosphereDbContext db) =>
        {
            var current = user.Identity?.IsAuthenticated == true ? Viewer.From(user).Username : null;
            var teams = await db.TeamMembers.AsNoTracking().Join(db.Teams, m => m.TeamId, t => t.Id, (m, t) => new { m.UserId, t.Id, t.Name }).ToListAsync();
            var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Username);
            var personas = Bootstrap.DemoUsers.Where(u => users.ContainsKey(u.Username)).Select(u =>
            {
                var account = users[u.Username];
                return new DemoPersonaDto(u.Username, account.DisplayName, account.Role,
                    [.. teams.Where(t => t.UserId == account.Id).OrderBy(t => t.Name).Select(t => new TeamRefDto(t.Id, t.Name))],
                    WhatToTry.GetValueOrDefault(u.Username) ?? new("", ""), u.Username == current);
            }).ToList();
            var lang = current is not null && users.TryGetValue(current, out var me) ? me.Language : "en";
            return new DemoStateDto(personas, Scenarios, current,
                Notice(lang, "Demo mode", "Switch between the demo accounts and simulate what colleagues and agents do. Nothing here is real data.",
                    "Mode démo", "Passez d'un compte de démonstration à l'autre et simulez ce que font les collègues et les agents. Rien n'est réel ici."));
        }).AllowAnonymous();

        demo.MapPost("/switch", async (DemoSwitchInput input, HttpContext ctx, NoosphereDbContext db) =>
        {
            var name = Accounts.NormalizeUsername(input.Username);
            if (Bootstrap.DemoUsers.All(u => u.Username != name)) throw ApiProblem.NotFound($"No demo account named '{name}'.");
            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == name) ?? throw ApiProblem.NotFound($"No demo account named '{name}'.");
            await Accounts.SignIn(ctx, user);
            return new DemoSwitchResultDto(await Accounts.Me(db, user),
                Notice(user.Language, $"Signed in as {user.DisplayName}", WhatToTry[name].En, $"Connecté en tant que {user.DisplayName}", WhatToTry[name].Fr));
        }).AllowAnonymous().Produces<ProblemDto>(StatusCodes.Status404NotFound);

        demo.MapPost("/simulate/edit", SimulateEdit).Produces<ProblemDto>(StatusCodes.Status404NotFound);
        demo.MapPost("/simulate/proposal", SimulateProposal).Produces<ProblemDto>(StatusCodes.Status409Conflict);
        demo.MapPost("/simulate/decision", SimulateDecision).Produces<ProblemDto>(StatusCodes.Status409Conflict);
        demo.MapPost("/simulate/agent", SimulateAgent);
        demo.MapPost("/simulate/guest-read", SimulateGuestRead).Produces<ProblemDto>(StatusCodes.Status404NotFound);

        demo.MapPost("/reset", async (DemoResetInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock, Microsoft.AspNetCore.Identity.IPasswordHasher<User> hasher) =>
        {
            if (input.Confirm != true) throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["confirm"] = ["Confirm the reset with confirm: true."] });
            var now = clock.GetUtcNow();
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await db.AgentConnections.ExecuteDeleteAsync();
                await db.OAuthRequests.ExecuteDeleteAsync();
                await db.OAuthClients.ExecuteDeleteAsync();
                await db.AccessLogs.ExecuteDeleteAsync();
                await Bootstrap.ClearLandscape(db);
                // The accounts stay as they are (passwords and sessions included).
                await Bootstrap.EnsureDemoUsers(db, hasher, now);
                await Bootstrap.LoadDemo(db, now);
                await transaction.CommitAsync();
            }
            return new DemoResetResultDto(Notice(await Language(db, user), "Demo reset", "The demo landscape, proposals, agents and access log were reloaded.",
                "Démo réinitialisée", "Le paysage, les propositions, les agents et le journal des accès de démonstration ont été rechargés.", "/"));
        });
    }

    // ---------- A colleague edits ----------

    private static async Task<DemoEditResultDto> SimulateEdit(DemoRecordInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var (type, e) = await Target(db, input);
        var me = Viewer.From(user);
        var facts = await RecordFacts.Load(db);
        var owner = facts.Owner(type.Kind, e);
        var colleague = await Colleague(db, owner, me.Id)
            ?? throw ApiProblem.Conflict("No other demo account can publish this record.");
        var now = clock.GetUtcNow();
        var field = type.Kind == RecordKind.Page ? "body" : "description";
        var body = type.Snapshot(e);
        var text = (body[field] as JsonValue)?.GetValue<string>() ?? "";
        var marker = text.IndexOf("\n\n_Checked by ", StringComparison.Ordinal);
        if (marker >= 0) text = text[..marker];
        body[field] = text.TrimEnd() + $"\n\n_Checked by {colleague.DisplayName} on {now.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC._";
        new Publisher(db, now).Update(type, e, body, new Author(colleague.Id, colleague.Username), await References.Load(db), summary: "Checked during the demo");
        await db.SaveChangesAsync();
        var outdated = await db.ProposalChanges
            .Where(c => c.Kind == type.Kind && c.TargetId == e.Id && c.BaseVersion != null && c.BaseVersion != e.Version)
            .Join(db.Proposals, c => c.ProposalId, p => p.Id, (_, p) => p)
            .Where(p => p.Status == ProposalStatus.Draft || p.Status == ProposalStatus.Submitted)
            .Select(p => p.Id).Distinct().ToListAsync();
        var title = type.Title(e);
        var lang = await Language(db, user);
        return new DemoEditResultDto(type.Kind, e.Id, e.Version, new UserRefDto(colleague.Username, colleague.DisplayName), outdated,
            Notice(lang, $"{colleague.DisplayName} changed {title}",
                $"Version {e.Version} is published. Saving the version you opened now gets a conflict" + (outdated.Count > 0 ? $", and {outdated.Count} open proposal(s) became outdated." : "."),
                $"{colleague.DisplayName} a modifié {title}",
                $"La version {e.Version} est publiée. Enregistrer la version que vous avez ouverte provoque maintenant un conflit" + (outdated.Count > 0 ? $", et {outdated.Count} proposition(s) ouverte(s) sont devenues obsolètes." : "."),
                UiPath(type.Kind, e.Id)));
    }

    // ---------- Another team proposes ----------

    private static async Task<DemoProposalResultDto> SimulateProposal(DemoRecordInput? input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var me = Viewer.From(user);
        var facts = await RecordFacts.Load(db);
        var myTeams = await Memberships.TeamIds(db, me.Id);
        RecordType type;
        VersionedEntity e;
        if (!string.IsNullOrWhiteSpace(input?.Id)) (type, e) = await Target(db, input);
        else
        {
            if (me.Role == Role.Guest) throw ApiProblem.Conflict("Guests do not validate proposals: switch to bob or david first.");
            // A record of one of my teams (an administrator without a team gets one of Team Finance).
            var teams = myTeams.Count > 0 ? myTeams : new HashSet<string> { "team-finance" };
            var app = await db.Applications.AsNoTracking().Where(a => a.TeamId != null && teams.Contains(a.TeamId) && a.Visibility == Visibility.Everyone)
                .OrderBy(a => a.Name).FirstOrDefaultAsync() ?? throw ApiProblem.Conflict("None of your teams owns an application to propose a change to.");
            (type, e) = (Records.Application, app);
        }
        var owner = facts.Owner(type.Kind, e);
        var authors = await db.Users.AsNoTracking().Where(u => u.Role == Role.Member && u.Id != me.Id).OrderBy(u => u.Username).ToListAsync();
        User? author = null;
        foreach (var candidate in authors)
            if (owner is null || !(await Memberships.TeamIds(db, candidate.Id)).Contains(owner)) { author = candidate; break; }
        if (author is null) throw ApiProblem.Conflict("No demo member outside the owning team can propose this change.");
        var access = new Access(new Viewer(author.Id, author.Username, author.Role), await Memberships.TeamIds(db, author.Id), facts);
        var now = clock.GetUtcNow();
        var field = type.Kind == RecordKind.Page ? "body" : "description";
        var after = type.Snapshot(e);
        var text = (after[field] as JsonValue)?.GetValue<string>() ?? "";
        after[field] = text.TrimEnd() + $"\n\nAsked by {author.DisplayName}: who should we call when this is down at night?";
        var proposal = new Proposal
        {
            Title = $"Add the night contact to {type.Title(e)}", Comment = "Our team gets calls about it at night and does not know whom to call.",
            AuthorId = author.Id, AuthorName = author.Username, AuthorKind = AuthorKind.User,
            Status = ProposalStatus.Submitted, CreatedAt = now, UpdatedAt = now, SubmittedAt = now, Version = 1,
        };
        proposal.Changes = await Proposals.BuildChanges(db, proposal.Id, [new ProposalChangeInput(type.Kind, e.Id, ChangeOperation.Update, e.Version, after)], access);
        db.Proposals.Add(proposal);
        await db.SaveChangesAsync();
        var lang = await Language(db, user);
        return new DemoProposalResultDto(proposal.Id, type.Kind, e.Id, new UserRefDto(author.Username, author.DisplayName),
            Notice(lang, $"{author.DisplayName} sent a proposal", $"\"{proposal.Title}\" waits for {facts.TeamName(owner) ?? "an administrator"}.",
                $"{author.DisplayName} a envoyé une proposition", $"« {proposal.Title} » attend {facts.TeamName(owner) ?? "un administrateur"}.", $"/review/{proposal.Id}"));
    }

    // ---------- A validator decides ----------

    private static async Task<DemoDecisionResultDto> SimulateDecision(DemoDecisionInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var me = Viewer.From(user);
        var errors = new FieldErrors();
        if (input.ProposalId is null) errors.Add("proposalId", "This field is required.");
        var accept = input.Decision?.Trim().ToLowerInvariant() switch
        {
            "accept" => true,
            "refuse" => false,
            _ => Error(errors, "decision", "Expected accept or refuse."),
        };
        errors.ThrowIfAny();
        var proposal = await Proposals.Query(db).FirstOrDefaultAsync(p => p.Id == input.ProposalId && p.AuthorId == me.Id)
            ?? throw ApiProblem.NotFound("No proposal of yours with this id.");
        if (proposal.Status != ProposalStatus.Submitted) throw ApiProblem.Conflict("Only a proposal waiting for validation can be decided.");
        var facts = await RecordFacts.Load(db);
        var owners = Proposals.Owners(proposal, facts);
        var validators = new List<User>();
        foreach (var team in owners)
            if (team is not null && await Colleague(db, team, me.Id, teamOnly: true) is { } v) validators.Add(v);
            else
            {
                validators = [await db.Users.AsNoTracking().Where(u => u.Role == Role.Admin && u.Id != me.Id).OrderBy(u => u.Username).FirstOrDefaultAsync()
                    ?? throw ApiProblem.Conflict("No other demo account can validate this proposal.")];
                break;
            }
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync();
        string? reason = null;
        foreach (var v in validators.DistinctBy(v => v.Id))
        {
            var access = new Access(new Viewer(v.Id, v.Username, v.Role), await Memberships.TeamIds(db, v.Id), facts);
            if (accept)
            {
                if (proposal.Status == ProposalStatus.Submitted) await Proposals.Accept(db, proposal, access, now);
            }
            else
            {
                reason = string.IsNullOrWhiteSpace(input.Reason)
                    ? "Thanks, but this is already documented on the team page: link to it instead of copying it here."
                    : input.Reason.Trim();
                Proposals.Refuse(proposal, access, reason, now);
                break;
            }
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        var decider = proposal.DecidedById is { } id ? validators.First(v => v.Id == id) : validators[0];
        var lang = await Language(db, user);
        var done = proposal.Status == ProposalStatus.Accepted;
        return new DemoDecisionResultDto(proposal.Id, proposal.Status, new UserRefDto(decider.Username, decider.DisplayName), proposal.RefusalReason,
            accept
                ? Notice(lang, done ? $"{decider.DisplayName} accepted your proposal" : $"{decider.DisplayName} approved your proposal",
                    done ? $"\"{proposal.Title}\" is published." : $"\"{proposal.Title}\" still waits for another owning team.",
                    done ? $"{decider.DisplayName} a accepté votre proposition" : $"{decider.DisplayName} a approuvé votre proposition",
                    done ? $"« {proposal.Title} » est publiée." : $"« {proposal.Title} » attend encore une autre équipe propriétaire.", $"/review/{proposal.Id}")
                : Notice(lang, $"{decider.DisplayName} refused your proposal", $"Reason: {reason}", $"{decider.DisplayName} a refusé votre proposition", $"Motif : {reason}", $"/review/{proposal.Id}"));
    }

    // ---------- A demo agent ----------

    private static async Task<DemoAgentResultDto> SimulateAgent(DemoAgentInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock, HttpRequest request, Settings settings)
    {
        var scenario = input.Scenario?.Trim().ToLowerInvariant();
        if (scenario is not ("draft" or "submit" or "refused" or "quota"))
            throw ApiProblem.Invalid(new Dictionary<string, string[]> { ["scenario"] = ["Expected draft, submit, refused or quota."] });
        var me = Viewer.From(user);
        var now = clock.GetUtcNow();
        var agents = await AgentSettingsStore.Get(db);
        if (!agents.Enabled) throw ApiProblem.Conflict("AI agents are turned off in the agent settings.");
        var connection = await db.AgentConnections.FirstOrDefaultAsync(c => c.UserId == me.Id && c.Demo && c.RevokedAt == null);
        if (connection is null)
        {
            connection = new AgentConnection
            {
                ClientId = AgentClientId, ClientName = AgentName, UserId = me.Id, Scopes = [Scopes.Read, Scopes.Draft], CreatedAt = now,
                ApprovedAt = now, ApprovedBy = me.Username, Demo = true,
            };
            db.AgentConnections.Add(connection);
        }
        // Other scenarios run even after the quota one paused the demo agent.
        if (scenario != "quota") connection.PausedUntil = null;
        connection.PendingApproval = false;
        await db.SaveChangesAsync();
        var start = await db.AgentActivities.Where(a => a.ConnectionId == connection.Id).MaxAsync(a => (long?)a.Id) ?? 0;
        var baseUrl = PublicAddress.Of(request, settings);
        var scopes = connection.Scopes;
        Task<string> Call(string tool, Func<AgentCall, Task<string>> body, int? quota = null) => AgentRuntime.Run(db, connection.Id, scopes, tool, now, baseUrl, body, quota);
        async Task<string?> Try(string tool, Func<AgentCall, Task<string>> body, int? quota = null)
        {
            try
            {
                return await Call(tool, body, quota);
            }
            catch (AgentRefusal)
            {
                return null;
            }
        }

        var lang = await Language(db, user);
        Guid? proposalId = null;
        DemoNoticeDto notice;
        var facts = await RecordFacts.Load(db);
        switch (scenario)
        {
            case "draft" or "submit":
            {
                var access = await AgentAccess.For(db, await db.Users.AsNoTracking().FirstAsync(u => u.Id == me.Id), scopes, agents);
                var target = await db.Applications.AsNoTracking().OrderBy(a => a.Id == "billing" ? 0 : 1).ThenBy(a => a.Name).ToListAsync();
                var app = target.FirstOrDefault(a => access.Sight(RecordKind.Application, a) == Sight.Full)
                    ?? throw ApiProblem.Conflict("The agent sees no application.");
                await Try("search", c => AgentTools.Search(c, app.Name, null));
                await Try("get_record", c => AgentTools.GetRecord(c, "application", app.Id));
                var fields = new JsonObject { ["description"] = (app.Description ?? "").TrimEnd() + "\n\nIts nightly jobs are listed on the operations page: [[page:operations]]." };
                var result = await Try("draft_change", c => AgentTools.DraftChange(c, "application", app.Id, "update", fields, "The description does not say where the nightly jobs are documented."));
                proposalId = result is null ? null : Guid.Parse(JsonNode.Parse(result)!["draftId"]!.GetValue<string>());
                if (proposalId is { } draft && scenario == "submit")
                    await Try("submit_draft", c => AgentTools.SubmitDraft(c, draft.ToString(), $"{app.Name}: link the operations page", "Found while answering a question about the nightly jobs."));
                notice = proposalId is null
                    ? Notice(lang, "The agent could not write a draft", "Your role does not allow proposals: see its activity for the reason.",
                        "L'agent n'a pas pu écrire de brouillon", "Votre rôle ne permet pas de proposer : voyez son activité pour la raison.", "/my-agents")
                    : scenario == "draft"
                        ? Notice(lang, $"{AgentName} wrote a draft", $"A draft on {app.Name} waits for you in My agents. Nobody else sees it until it is submitted.",
                            $"{AgentName} a écrit un brouillon", $"Un brouillon sur {app.Name} vous attend dans Mes agents. Personne d'autre ne le voit avant sa soumission.", "/my-agents")
                        : Notice(lang, $"{AgentName} submitted a proposal", $"The proposal on {app.Name} now waits for {facts.TeamName(app.TeamId) ?? "an administrator"}.",
                            $"{AgentName} a soumis une proposition", $"La proposition sur {app.Name} attend maintenant {facts.TeamName(app.TeamId) ?? "un administrateur"}.", $"/review/{proposalId}");
                break;
            }
            case "refused":
            {
                var restricted = facts.RestrictedRecords().Where(r => r.Kind == RecordKind.Application).Select(r => r.Id).Order(StringComparer.Ordinal).FirstOrDefault();
                if (restricted is not null) await Try("get_record", c => AgentTools.GetRecord(c, "application", restricted));
                // A made-up token, built at run time: the secret scanner refuses it.
                var fake = "gh" + "p_" + new string('x', 36);
                var app = await db.Applications.AsNoTracking().Where(a => a.Visibility == Visibility.Everyone).OrderBy(a => a.Name).FirstAsync();
                await Try("draft_change", c => AgentTools.DraftChange(c, "application", app.Id, "update",
                    new JsonObject { ["description"] = $"Deploy with the token {fake}." }, null));
                notice = Notice(lang, $"{AgentName} was refused twice", "It tried to open a restricted record (above the ceiling) and to write a token (a secret). Both refusals are in its activity.",
                    $"{AgentName} a été refusé deux fois", "Il a tenté d'ouvrir une fiche restreinte (au-dessus du plafond) et d'écrire un jeton (un secret). Les deux refus sont dans son activité.", "/my-agents");
                break;
            }
            default:
            {
                var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
                var quota = await AgentRuntime.CallsSince(db, connection.Id, hour) + 3;
                for (var i = 0; i < 5; i++)
                {
                    try
                    {
                        await Call("search", c => AgentTools.Search(c, "billing", null), quota);
                    }
                    catch (AgentUnavailable)
                    {
                        break;
                    }
                }
                notice = Notice(lang, $"{AgentName} reached its quota", "It is paused until the next hour; a quota line is in its activity. (The demo uses a quota of 3 calls.)",
                    $"{AgentName} a atteint son quota", "Il est en pause jusqu'à l'heure suivante ; une ligne de quota est dans son activité. (La démo utilise un quota de 3 appels.)", "/my-agents");
                break;
            }
        }
        var lines = await db.AgentActivities.AsNoTracking().Where(a => a.ConnectionId == connection.Id && a.Id > start).OrderBy(a => a.Id).ToListAsync();
        return new DemoAgentResultDto(scenario, connection.Id, await AgentEndpoints.Activity(db, lines, await Access.Load(db, user)), proposalId, notice);
    }

    // ---------- The guest reads ----------

    private static async Task<DemoGuestReadResultDto> SimulateGuestRead(DemoRecordInput input, ClaimsPrincipal user, NoosphereDbContext db, TimeProvider clock)
    {
        var (type, e) = await Target(db, input);
        var guest = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == "claire" && u.Role == Role.Guest)
            ?? throw ApiProblem.Conflict("The demo guest (claire) does not exist.");
        var access = new Access(new Viewer(guest.Id, guest.Username, guest.Role), await Memberships.TeamIds(db, guest.Id), await RecordFacts.Load(db));
        var before = await db.AccessLogs.CountAsync();
        var sight = access.Sight(type.Kind, e);
        if (sight == Sight.Full)
            await AccessLogs.RecordRead(db, access, type.Kind, e, notesShown: false, clock.GetUtcNow(), "demo");
        var lines = await db.AccessLogs.CountAsync() - before;
        var title = type.Title(e);
        var lang = await Language(db, user);
        var notice = sight != Sight.Full
            ? Notice(lang, $"Claire cannot read {title}", sight == Sight.Hidden
                    ? "It is restricted and she is not on its access list: she only gets a stub, and nothing is logged. Add her to the access list to try again."
                    : "It is not shared with her: she sees its outline only, and nothing is logged. Share it with her to try again.",
                $"Claire ne peut pas lire {title}", sight == Sight.Hidden
                    ? "La fiche est restreinte et Claire n'est pas sur sa liste d'accès : elle n'en voit qu'un aperçu masqué, rien n'est journalisé. Ajoutez-la à la liste pour réessayer."
                    : "La fiche ne lui est pas partagée : elle n'en voit que l'aperçu, rien n'est journalisé. Partagez-la avec elle pour réessayer.", UiPath(type.Kind, e.Id))
            : lines > 0
                ? Notice(lang, $"Claire read {title}", $"{lines} line(s) added to the access log.", $"Claire a lu {title}", $"{lines} ligne(s) ajoutée(s) au journal des accès.", "/admin/access")
                : Notice(lang, $"Claire read {title}", "It is shared with her and not restricted: such reads are not logged.",
                    $"Claire a lu {title}", "La fiche lui est partagée et n'est pas restreinte : ces lectures ne sont pas journalisées.", UiPath(type.Kind, e.Id));
        return new DemoGuestReadResultDto(type.Kind, e.Id, sight == Sight.Full, lines, notice);
    }

    // ---------- Helpers ----------

    private static async Task<(RecordType Type, VersionedEntity Entity)> Target(NoosphereDbContext db, DemoRecordInput? input)
    {
        var errors = new FieldErrors();
        var type = Records.Parse(input?.Kind?.Trim());
        if (type is null || !type.Proposable) errors.Add("kind", "Expected application, flow, dataObject or page.");
        if (string.IsNullOrWhiteSpace(input?.Id)) errors.Add("id", "This field is required.");
        errors.ThrowIfAny();
        var e = await type!.Find(db, input!.Id!.Trim(), tracked: true) ?? throw CatalogEndpoints.NotFound(type, input.Id.Trim());
        return (type, e);
    }

    /// <summary>A member of the owning team other than <paramref name="me"/>; else (unless <paramref name="teamOnly"/>) an administrator, then any editor.</summary>
    private static async Task<User?> Colleague(NoosphereDbContext db, string? team, Guid me, bool teamOnly = false)
    {
        if (team is not null)
        {
            var member = await db.TeamMembers.Where(m => m.TeamId == team && m.UserId != me).Join(db.Users, m => m.UserId, u => u.Id, (_, u) => u)
                .Where(u => u.Role != Role.Guest).OrderBy(u => u.Username).FirstOrDefaultAsync();
            if (member is not null || teamOnly) return member;
        }
        return await db.Users.AsNoTracking().Where(u => u.Id != me && u.Role == Role.Admin).OrderBy(u => u.Username).FirstOrDefaultAsync()
            ?? await db.Users.AsNoTracking().Where(u => u.Id != me && u.Role == Role.Member).OrderBy(u => u.Username).FirstOrDefaultAsync();
    }

    private static string UiPath(RecordKind kind, string id) => $"/{Records.Of(kind).Plural}/{id}";

    private static async Task<string> Language(NoosphereDbContext db, ClaimsPrincipal user)
    {
        var id = Viewer.From(user).Id;
        return await db.Users.Where(u => u.Id == id).Select(u => u.Language).FirstOrDefaultAsync() ?? "en";
    }

    private static DemoNoticeDto Notice(string lang, string titleEn, string detailEn, string titleFr, string detailFr, string? link = null) =>
        lang == "fr" ? new(titleFr, detailFr, link) : new(titleEn, detailEn, link);

    private static bool Error(FieldErrors errors, string field, string message)
    {
        errors.Add(field, message);
        return false;
    }
}

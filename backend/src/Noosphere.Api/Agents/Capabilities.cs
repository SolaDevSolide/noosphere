using Noosphere.Api.Contracts;
using Noosphere.Api.Services;
using Noosphere.Domain;

namespace Noosphere.Api.Agents;

/// <summary>"What this agent can do", in plain words, from min(person's rights, ceiling, scopes).</summary>
public static class Capabilities
{
    /// <param name="access">The person's rights limited by the agent (see <see cref="AgentAccess.For"/>).</param>
    public static List<CapabilityDto> Of(Access access, AgentSettings settings, IReadOnlyCollection<string> scopes, string person)
    {
        var limits = access.Limits ?? new AgentLimits(settings.Ceiling, scopes.Contains(Scopes.ReadRestricted));
        var full = new Access(access.Viewer, access.Teams, access.Facts);
        var facts = access.Facts;
        var restricted = facts.RestrictedRecords().Count(r => full.Sight(r.Kind, r.Id) == Sight.Full);
        var notes = facts.ProtectedRecords().Count(r => full.ReadsProtected(r.Kind, r.Id));
        var items = new List<CapabilityDto>();
        var reads = scopes.Contains(Scopes.Read);
        items.Add(new("read", reads, !reads
            ? new("Cannot read records.", "Ne lit aucune fiche.")
            : settings.Ceiling == AgentCeiling.AsUser
                ? new($"Reads what {person} reads.", $"Lit ce que {person} lit.")
                : new("Reads every record visible to everyone.", "Lit toutes les fiches visibles par tous.")));
        var drafts = scopes.Contains(Scopes.Draft) && access.Viewer.CanEdit;
        items.Add(new("draft", drafts, drafts
            ? new("Writes drafts in its own workspace and submits them as proposals.", "Écrit des brouillons dans son espace et les soumet comme propositions.")
            : new("Cannot write drafts or proposals.", "N'écrit ni brouillon ni proposition.")));
        items.Add(new("publish", false, new("Cannot publish anything: the owning teams validate its proposals.", "Ne publie rien : les équipes propriétaires valident ses propositions.")));
        items.Add(limits.SeesRestricted && reads
            ? new("restricted", true, restricted == 0
                ? new("Could open restricted records, but none is visible to " + person + ".", "Pourrait ouvrir des fiches restreintes, mais " + person + " n'en voit aucune.")
                : new($"Opens the {restricted} restricted record(s) {person} can see.", $"Ouvre les {restricted} fiche(s) restreinte(s) que {person} voit."))
            : new("restricted", false, restricted == 0
                ? new("Cannot open restricted records.", "N'ouvre pas les fiches restreintes.")
                : new($"Cannot open the {restricted} restricted record(s) {person} can see.", $"N'ouvre pas les {restricted} fiche(s) restreinte(s) que {person} voit.")));
        items.Add(limits.ReadsProtectedNotes && reads && notes > 0
            ? new("protectedNotes", true, new($"Reads the protected notes of the {notes} record(s) {person} reads them on (outside restricted records unless allowed above).",
                $"Lit les notes protégées des {notes} fiche(s) où {person} les lit (hors fiches restreintes, sauf autorisation ci-dessus)."))
            : new("protectedNotes", false, new("Cannot read protected notes.", "Ne lit pas les notes protégées.")));
        items.Add(new("quota", true, new($"At most {settings.QuotaPerHour} calls an hour; beyond, it is paused until the next hour.",
            $"Au plus {settings.QuotaPerHour} appels par heure ; au-delà, il est mis en pause jusqu'à l'heure suivante.")));
        return items;
    }
}

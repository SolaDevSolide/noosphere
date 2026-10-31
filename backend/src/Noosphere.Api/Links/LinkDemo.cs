using System.Security.Cryptography;
using System.Text.Json;
using Noosphere.Api.Data;
using Noosphere.Domain;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>
/// The demo link: an active link with a fictional gestion-mep instance ("MEP Alizé"), its deployments and a few
/// exchanges. Data only: nothing is ever sent to it (outbound calls are logged as <c>demo</c>).
/// </summary>
public static class LinkDemo
{
    public const string PeerUrl = "https://mep.alize.example";

    public static async Task Seed(NoosphereDbContext db, LinkKeys keys, DateTimeOffset now, CancellationToken cancel = default)
    {
        var (ownSigning, ownEncryption) = (keys.NewSigningKey(), keys.NewEncryptionKey());
        // The fictional peer's public keys; its private keys are thrown away at once.
        using var peerSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var peerEncryptionKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var (peerSigning, peerEncryption) = (Jwk.From(peerSigningKey), Jwk.From(peerEncryptionKey));
        var paired = now.AddDays(-20);
        var link = new Link
        {
            Product = "gestion-mep", InstanceName = "MEP Alizé", Url = PeerUrl, PeerVersion = "1.11.0", State = LinkState.Active,
            ConfirmedHere = true, ConfirmedAt = paired.AddMinutes(4), ConfirmedBy = "alice", PeerConfirmed = true, PeerCheckedAt = now.AddHours(-1),
            Capabilities = [.. LinkProtocol.AllCapabilities],
            OwnSigningKey = ownSigning.Protected, OwnSigningKid = ownSigning.Kid, OwnEncryptionKey = ownEncryption.Protected, OwnEncryptionKid = ownEncryption.Kid,
            PeerSigningKey = peerSigning.Serialize(), PeerSigningKid = peerSigning.Kid(), PeerEncryptionKey = peerEncryption.Serialize(), PeerEncryptionKid = peerEncryption.Kid(),
            Fingerprint = Fingerprint([ownSigning.Public, ownEncryption.Public, peerSigning, peerEncryption]),
            PairedAt = paired, PairedBy = "alice", LastExchangeAt = now.AddMinutes(-12), Demo = true, Version = 2,
        };
        db.Links.Add(link);
        var code = NormalizeCode(RandomNumberGenerator.GetString(LinkAdmin.CodeAlphabet, LinkAdmin.CodeLength));
        db.LinkCodes.Add(new LinkCode
        {
            CodeId = code[..4], ProtectedCode = keys.Protect(code), CodeHash = LinkAdmin.Hash(code), Capabilities = [.. LinkProtocol.AllCapabilities],
            CreatedAt = paired.AddMinutes(-3), ExpiresAt = paired.AddMinutes(12), CreatedBy = "alice", UsedAt = paired, LinkId = link.Id,
        });

        static DateTimeOffset Evening(DateTimeOffset day) => new(day.UtcDateTime.Date.AddHours(20), TimeSpan.Zero);
        var production = new[] { new DeploymentEnvironment("Production", true) };
        var staging = new[] { new DeploymentEnvironment("Acceptance", false), new DeploymentEnvironment("Production", true) };
        Deployment Mep(string title, DeploymentPhase phase, DeploymentOutcome? outcome, DateTimeOffset start, double hours, string[] apps,
            DeploymentEnvironment[] environments, bool postponed = false) => new()
            {
                LinkId = link.Id, DeploymentId = Guid.NewGuid().ToString(), Title = title, Phase = phase, Outcome = outcome, Postponed = postponed,
                PlannedStart = start, PlannedEnd = start.AddHours(hours), Url = $"{PeerUrl}/meps/{Guid.NewGuid()}", ApplicationIds = [.. apps],
                Environments = JsonSerializer.Serialize(environments, JsonSerializerOptions.Web),
                UpdatedAt = phase is DeploymentPhase.Closed or DeploymentPhase.Cancelled ? start.AddHours(hours) : now.AddDays(-2),
                ReceivedAt = now.AddDays(-2),
                EndedAt = phase is DeploymentPhase.Closed or DeploymentPhase.Cancelled ? start.AddHours(hours) : null,
            };
        db.Deployments.AddRange(
            Mep("Billing 4.2", DeploymentPhase.Preparation, null, Evening(now.AddDays(12)), 3, ["billing"], staging),
            Mep("CRM spring release", DeploymentPhase.Acceptance, null, Evening(now.AddDays(5)), 4, ["crm", "portal"], staging, postponed: true),
            Mep("Payments SEPA instant", DeploymentPhase.Preparation, null, Evening(now.AddDays(26)), 2, ["payments", "billing"], production),
            Mep("Billing 4.1", DeploymentPhase.Closed, DeploymentOutcome.Success, Evening(now.AddDays(-18)), 3, ["billing"], production),
            Mep("Portal accessibility fixes", DeploymentPhase.Closed, DeploymentOutcome.SuccessWithIncidents, Evening(now.AddDays(-9)), 2, ["portal", "mobile"], production),
            Mep("Claims 7.0", DeploymentPhase.Cancelled, null, Evening(now.AddDays(-4)), 5, ["claims"], production));

        void Line(DateTimeOffset at, LinkDirection direction, string operation, LinkExchangeResult result, int? status, string detail, string? reason = null) =>
            db.LinkExchanges.Add(new LinkExchange { LinkId = link.Id, At = at, Direction = direction, Operation = operation, Result = result, Status = status, Detail = detail, Reason = reason });
        Line(paired, LinkDirection.In, "pair", LinkExchangeResult.Ok, 200, "Paired with MEP Alizé (gestion-mep); waiting for confirmation on both sides.");
        Line(paired.AddMinutes(4), LinkDirection.Out, "status", LinkExchangeResult.Demo, null, "Demo link: nothing was sent.");
        Line(paired.AddMinutes(6), LinkDirection.In, "catalog", LinkExchangeResult.Ok, 200, "First read of the catalogue.");
        Line(now.AddDays(-2), LinkDirection.In, "deployments", LinkExchangeResult.Ok, 200, "3 event(s) accepted.");
        Line(now.AddDays(-1), LinkDirection.In, "catalog", LinkExchangeResult.Refused, 401, "This nonce was already used in the last 10 minutes.", LinkProtocol.Reasons.Replay);
        Line(now.AddHours(-3), LinkDirection.Out, "notices", LinkExchangeResult.Demo, null, "Demo link: nothing was sent.");
        Line(now.AddMinutes(-12), LinkDirection.In, "catalog", LinkExchangeResult.Ok, 304, "Not modified.");
        await db.SaveChangesAsync(cancel);
    }
}

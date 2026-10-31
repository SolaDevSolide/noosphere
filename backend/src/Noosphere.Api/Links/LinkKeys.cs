using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Noosphere.Domain;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Api.Links;

/// <summary>A new key pair: the private key protected for storage, the public key as JWK, and its id.</summary>
public sealed record NewKey(string Protected, Jwk Public)
{
    public string Kid => Public.Kid();
}

/// <summary>
/// This side's link keys: generated per link, private keys stored as PKCS#8 protected with ASP.NET Core Data Protection
/// (the same key ring as the session cookies, KEYS_DIR). Also protects the pairing codes.
/// </summary>
public sealed class LinkKeys(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Noosphere.Links.v1");

    public string Protect(string text) => _protector.Protect(text);

    public string Unprotect(string text) => _protector.Unprotect(text);

    public NewKey NewSigningKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new NewKey(Protect(Convert.ToBase64String(key.ExportPkcs8PrivateKey())), Jwk.From(key));
    }

    public NewKey NewEncryptionKey()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new NewKey(Protect(Convert.ToBase64String(key.ExportPkcs8PrivateKey())), Jwk.From(key));
    }

    public ECDsa Signing(string protectedKey)
    {
        var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(Unprotect(protectedKey)), out _);
        return key;
    }

    public ECDiffieHellman Encryption(string protectedKey)
    {
        var key = ECDiffieHellman.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(Unprotect(protectedKey)), out _);
        return key;
    }

    /// <summary>This side's signing key of a link; null once the link is cut.</summary>
    public ECDsa? OwnSigning(Link link) => link.OwnSigningKey is { } k ? Signing(k) : null;

    /// <summary>This side's public keys of a link (signing, encryption).</summary>
    public (Jwk Signing, Jwk Encryption) OwnPublic(Link link)
    {
        using var s = Signing(link.OwnSigningKey ?? throw new InvalidOperationException("The link has no keys."));
        using var e = Encryption(link.OwnEncryptionKey ?? throw new InvalidOperationException("The link has no keys."));
        return (Jwk.From(s), Jwk.From(e));
    }

    /// <summary>The fingerprint of the four current public keys of a link.</summary>
    public string Fingerprint(Link link)
    {
        var (s, e) = OwnPublic(link);
        return LinkCrypto.Fingerprint([s, e, Jwk.Parse(link.PeerSigningKey!), Jwk.Parse(link.PeerEncryptionKey!)]);
    }

    /// <summary>The private key a JWE was encrypted to: the current encryption key, or the previous one during the renewal grace.</summary>
    public ECDiffieHellman? DecryptionKey(Link link, string? kid, DateTimeOffset now)
    {
        if (kid == link.OwnEncryptionKid && link.OwnEncryptionKey is { } current) return Encryption(current);
        if (kid is not null && kid == link.OwnPreviousEncryptionKid && link.OwnPreviousUntil > now && link.OwnPreviousEncryptionKey is { } previous)
            return Encryption(previous);
        return null;
    }

    /// <summary>Erases every key of a cut link (key ids stay, so that late requests are answered "link cut").</summary>
    public static void Erase(Link link)
    {
        link.OwnSigningKey = link.OwnEncryptionKey = link.OwnPreviousEncryptionKey = null;
        link.PeerSigningKey = link.PeerEncryptionKey = link.PeerPreviousSigningKey = null;
        link.OwnPreviousEncryptionKid = null;
        link.PeerPreviousSigningKid = null;
        link.OwnPreviousUntil = link.PeerPreviousUntil = null;
    }
}

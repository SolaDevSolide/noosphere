using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Noosphere.Api.Links;
using static Noosphere.Api.Links.LinkCrypto;

namespace Noosphere.Tests;

/// <summary>Cryptography of the link protocol, against the RFC test vectors where there are some.</summary>
public class LinkCryptoTests
{
    /// <summary>RFC 9421 Appendix B.1.3, test-key-ecc-p256: the public key only (verification).</summary>
    private static ECDsa RfcEccPublicKey()
    {
        var pem = "-----BEGIN PUBLIC " + "KEY-----\n"
            + "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqIVYZVLCrPZHGHjP17CTW0/+D9Lf\n"
            + "w0EkjqF7xB4FivAxzic30tMM4GF+hR6Dxh71Z50VGGdldkkDXZCnTNnoXQ==\n"
            + "-----END PUBLIC " + "KEY-----";
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    [Fact]
    public void Rfc9421_B_2_4_response_signature_verifies()
    {
        var parameters = "(\"@status\" \"content-type\" \"content-digest\" \"content-length\");created=1618884473;keyid=\"test-key-ecc-p256\"";
        var signatureBase = SignatureBase(
        [
            ("@status", "200"),
            ("content-type", "application/json"),
            ("content-digest", "sha-512=:mEWXIS7MaLRuGgxOBdODa3xqM1XdEvxoYhvlCFJ41QJgJc4GTsPp29l5oGX69wWdXymyU0rjJuahq4l5aGgfLQ==:"),
            ("content-length", "23"),
        ], parameters);
        Assert.Equal("\"@status\": 200\n\"content-type\": application/json\n"
            + "\"content-digest\": sha-512=:mEWXIS7MaLRuGgxOBdODa3xqM1XdEvxoYhvlCFJ41QJgJc4GTsPp29l5oGX69wWdXymyU0rjJuahq4l5aGgfLQ==:\n"
            + "\"content-length\": 23\n\"@signature-params\": " + parameters, signatureBase);

        var header = "sig-b24=:wNmSUAhwb5LxtOtOpNa6W5xj067m5hFrj0XQ4fvpaCLx0NKocgPquLgyahnzDnDAUy5eCdlYUEkLIj+32oiasw==:";
        var signature = ParseSignature(header, "sig-b24")!;
        var input = ParseSignatureInput("sig-b24=" + parameters, "sig-b24")!;
        Assert.Equal(["@status", "content-type", "content-digest", "content-length"], input.Components);
        Assert.Equal((1618884473L, "test-key-ecc-p256"), (input.Created, input.KeyId));
        using var key = RfcEccPublicKey();
        Assert.True(LinkCrypto.Verify(key, SignatureBase([("@status", "200"), ("content-type", "application/json"),
            ("content-digest", "sha-512=:mEWXIS7MaLRuGgxOBdODa3xqM1XdEvxoYhvlCFJ41QJgJc4GTsPp29l5oGX69wWdXymyU0rjJuahq4l5aGgfLQ==:"),
            ("content-length", "23")], input.Raw), signature));
        Assert.False(LinkCrypto.Verify(key, signatureBase.Replace("200", "201", StringComparison.Ordinal), signature));
    }

    [Fact]
    public void Rfc7518_appendix_C_key_agreement()
    {
        // Bob's private key and Alice's ephemeral public key (RFC 7518 Appendix C), built at runtime.
        var bob = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = FromBase64Url("VEmDZpDXXK8p8N0Cndsxs924" + "q6nS1RXFASRl6BfUqdw"),
            Q = new ECPoint { X = FromBase64Url("weNJy2HscCSM6AEDTDg04biOvhFhyyWvOHQfeF_PxMQ"), Y = FromBase64Url("e8lnCO-AlStT-NJVX-crhB7QRYhiix03illJOVAOyck") },
        });
        using var alice = new Jwk("EC", "P-256", "gI0GAILBdu7T53akrFmMyGcsF3n5dO7MmwNBHKW5SV0", "SLW_xSffzlPWrHEVI30DHM_4egVwt3NQqeUD7nMFpps").AgreementKey();
        var z = bob.DeriveRawSecretAgreement(alice.PublicKey);
        Assert.Equal(new byte[] { 158, 86, 217, 29, 129, 113, 53, 211, 114, 131, 66, 131, 191, 132, 38, 156, 251, 49, 110, 163, 218, 128, 106, 72, 246, 218,
            167, 121, 140, 254, 144, 196 }, z);
        var key = ConcatKdf(z, "A128GCM", 128, "Alice"u8, "Bob"u8);
        Assert.Equal("VqqN6vgjbSBcIijNcacQGg", Base64Url(key));
        // Longer keys take as many SHA-256 rounds as needed.
        Assert.Equal(64, ConcatKdf(z, "A256GCM", 512).Length);
        bob.Dispose();
    }

    [Fact]
    public void Jwe_round_trip_and_tampering()
    {
        using var recipient = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var jwk = Jwk.From(recipient);
        var payload = new JsonObject { ["events"] = new JsonArray(), ["title"] = "Billing 4.2 — été" };
        var compact = EncryptJson(payload, jwk);
        var parts = compact.Split('.');
        Assert.Equal(5, parts.Length);
        Assert.Empty(parts[1]);
        var header = JsonNode.Parse(FromBase64Url(parts[0]))!.AsObject();
        Assert.Equal(["alg", "enc", "kid", "epk", "cty"], header.Select(p => p.Key));
        Assert.Equal(("ECDH-ES", "A256GCM", jwk.Kid(), "application/json"),
            (header["alg"]!.GetValue<string>(), header["enc"]!.GetValue<string>(), header["kid"]!.GetValue<string>(), header["cty"]!.GetValue<string>()));
        Assert.Equal(jwk.Kid(), JweKid(compact));
        Assert.Equal(payload.ToJsonString(), JsonNode.Parse(Decrypt(compact, recipient))!.ToJsonString());

        // Another key, a changed header or a changed ciphertext do not decrypt.
        using var other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Assert.ThrowsAny<CryptographicException>(() => Decrypt(compact, other));
        var changed = parts[3][..^2] + (parts[3][^2] == 'A' ? 'B' : 'A') + parts[3][^1];
        Assert.ThrowsAny<CryptographicException>(() => Decrypt(string.Join('.', parts[0], "", parts[2], changed, parts[4]), recipient));
        Assert.ThrowsAny<CryptographicException>(() => Decrypt("a.b.c", recipient));
    }

    [Fact]
    public void Thumbprint_follows_rfc7638()
    {
        // RFC 7638 §3.1 uses an RSA key; for EC the members are crv, kty, x, y in this order.
        var jwk = new Jwk("EC", "P-256", "qIVYZVLCrPZHGHjP17CTW0_-D9Lfw0EkjqF7xB4FivA", "Mc4nN9LTDOBhfoUeg8Ye9WedFRhnZXZJA12Qp0zZ6F0");
        Assert.Equal("{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"qIVYZVLCrPZHGHjP17CTW0_-D9Lfw0EkjqF7xB4FivA\",\"y\":\"Mc4nN9LTDOBhfoUeg8Ye9WedFRhnZXZJA12Qp0zZ6F0\"}",
            jwk.ThumbprintInput());
        Assert.Equal(Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(jwk.ThumbprintInput()))), jwk.Kid());
        Assert.Equal(32, jwk.Thumbprint().Length);
        // The RFC 9421 test key in JWK form is the same point as its PEM form.
        using var pem = RfcEccPublicKey();
        Assert.Equal(jwk.Kid(), Jwk.From(pem).Kid());
        Assert.Throws<CryptographicException>(() => new Jwk("EC", "P-384", jwk.X, jwk.Y).Parameters());
        Assert.ThrowsAny<CryptographicException>(() => new Jwk("EC", "P-256", jwk.X, jwk.X).Parameters());
    }

    [Fact]
    public void Fingerprint_is_deterministic_and_order_free()
    {
        var keys = Enumerable.Range(0, 4).Select(_ =>
        {
            using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return Jwk.From(k);
        }).ToList();
        var fingerprint = Fingerprint(keys);
        Assert.Matches("^[a-z]+ [a-z]+ [0-9]{4} [a-z]+$", fingerprint);
        Assert.Equal(fingerprint, Fingerprint([keys[3], keys[1], keys[0], keys[2]]));
        using var fifth = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.NotEqual(fingerprint, Fingerprint([keys[0], keys[1], keys[2], Jwk.From(fifth)]));

        // A fixed vector shared with gestion-mep: the public keys of RFC 9421 (test-key-ecc-p256), RFC 7518 Appendix C
        // (Alice's and Bob's) and RFC 7515 Appendix A.3. Their thumbprints, then the fingerprint, computed independently.
        var fixedKeys = new[]
        {
            new Jwk("EC", "P-256", "qIVYZVLCrPZHGHjP17CTW0_-D9Lfw0EkjqF7xB4FivA", "Mc4nN9LTDOBhfoUeg8Ye9WedFRhnZXZJA12Qp0zZ6F0"),
            new Jwk("EC", "P-256", "gI0GAILBdu7T53akrFmMyGcsF3n5dO7MmwNBHKW5SV0", "SLW_xSffzlPWrHEVI30DHM_4egVwt3NQqeUD7nMFpps"),
            new Jwk("EC", "P-256", "weNJy2HscCSM6AEDTDg04biOvhFhyyWvOHQfeF_PxMQ", "e8lnCO-AlStT-NJVX-crhB7QRYhiix03illJOVAOyck"),
            new Jwk("EC", "P-256", "f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU", "x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0"),
        };
        Assert.Equal(["ydQXMtvbsOsZyFir-Y7A8t7fKEM1gbKPvyFkdpu4fvI", "_GK0r6GCoJt9zcssg9lay4obIxgCq05ntiRymRHADSU",
            "Vy57XrArUrW0NbpI12tEzDHABxMwrTh6HHXRenSpnCo", "oKIywvGUpTVTyxMQ3bwIIeQUudfr_CkLMjCE19ECD-U"], fixedKeys.Select(k => k.Kid()));
        const string expected = "cedar anchor 8451 arbor";
        Assert.Equal(expected, Fingerprint(fixedKeys));
        Assert.Equal(expected, Fingerprint(fixedKeys.Reverse()));
        Assert.Equal(256, Words.Count);
        Assert.Equal(256, Words.Distinct().Count());
        Assert.Equal(("abbey", "zinc", "lumen", "orbit", "cedar"), (Words[0], Words[255], Words[162], Words[191], Words[52]));
        Assert.Equal("orbit cedar 0712 lumen", NormalizeFingerprint("  Orbit  CEDAR 0712 lumen "));
    }

    [Fact]
    public void Content_digest_and_signature_headers()
    {
        var body = "{\"hello\": \"world\"}"u8.ToArray();
        var digest = ContentDigest(body);
        Assert.Equal("sha-256=:X48E9qOokqqrvdts8nOJRJN3OWDUoyWxBf7kbu9DBPE=:", digest);
        Assert.True(DigestMatches(digest, body));
        Assert.True(DigestMatches("sha-512=:abc=:, " + digest, body));
        Assert.False(DigestMatches(digest, [.. body, 0]));
        Assert.False(DigestMatches(null, body));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signed = LinkSignatures.SignRequest(key, "kid-1", "post", "/link/v1/deployments", "", body, 1700000000, "bm9uY2U");
        Assert.Equal("link=(\"@method\" \"@path\" \"@query\" \"content-digest\");created=1700000000;nonce=\"bm9uY2U\";keyid=\"kid-1\";"
            + "alg=\"ecdsa-p256-sha256\";tag=\"noosphere-link\"", signed.SignatureInput);
        var input = ParseSignatureInput(signed.SignatureInput)!;
        var expectedBase = "\"@method\": POST\n\"@path\": /link/v1/deployments\n\"@query\": ?\n\"content-digest\": " + digest + "\n"
            + "\"@signature-params\": (\"@method\" \"@path\" \"@query\" \"content-digest\");created=1700000000;nonce=\"bm9uY2U\";keyid=\"kid-1\";"
            + "alg=\"ecdsa-p256-sha256\";tag=\"noosphere-link\"";
        Assert.Equal(expectedBase, SignatureBase(LinkSignatures.RequestValues("POST", "/link/v1/deployments", "", digest), input.Raw));
        Assert.True(LinkCrypto.Verify(key, expectedBase, ParseSignature(signed.Signature)!));
        Assert.Equal(64, ParseSignature(signed.Signature)!.Length);

        // Without a body, content-digest is left out; an empty query is "?".
        var get = LinkSignatures.SignRequest(key, "kid-1", "GET", "/link/v1/catalog", null, null, 1700000000, "n");
        Assert.Null(get.ContentDigest);
        Assert.StartsWith("link=(\"@method\" \"@path\" \"@query\");", get.SignatureInput, StringComparison.Ordinal);
        Assert.Equal("?a=1", QueryComponent("?a=1"));
        Assert.Equal("?", QueryComponent("?"));

        var response = LinkSignatures.SignResponse(key, "kid-1", 204, null, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.StartsWith("link=(\"@status\");", response.SignatureInput, StringComparison.Ordinal);
        Assert.Null(LinkSignatures.CheckResponse([(key, "kid-1")], 204, response.SignatureInput, response.Signature, null, [], DateTimeOffset.UtcNow));
        Assert.NotNull(LinkSignatures.CheckResponse([(key, "kid-1")], 200, response.SignatureInput, response.Signature, null, [], DateTimeOffset.UtcNow));
        Assert.NotNull(LinkSignatures.CheckResponse([(key, "other")], 204, response.SignatureInput, response.Signature, null, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Canonical_json_and_pairing_mac()
    {
        var content = new JsonObject
        {
            ["peer"] = new JsonObject
            {
                ["url"] = "https://mep.example", ["instanceName"] = "MEP Alizé \"prod\"\n", ["signingKey"] = new JsonObject { ["y"] = "2", ["x"] = "1", ["kty"] = "EC" },
            },
            ["nonce"] = "abc/def",
            ["codeId"] = "7K4Q",
            ["capabilities"] = new JsonArray("catalog:read", "notices:send"),
        };
        Assert.Equal("{\"capabilities\":[\"catalog:read\",\"notices:send\"],\"codeId\":\"7K4Q\",\"nonce\":\"abc/def\","
            + "\"peer\":{\"instanceName\":\"MEP Alizé \\\"prod\\\"\\n\",\"signingKey\":{\"kty\":\"EC\",\"x\":\"1\",\"y\":\"2\"},\"url\":\"https://mep.example\"}}",
            Canonical(content));
        Assert.Equal("{\"a\":\"\\u0001\",\"b\":true,\"c\":null}", Canonical(new JsonObject { ["c"] = null, ["b"] = true, ["a"] = "\u0001" }));
        Assert.Throws<ArgumentException>(() => Canonical(new JsonObject { ["n"] = 1 }));

        Assert.Equal("7K4Q29XDM3PA", NormalizeCode("7k4q-29xd m3pa"));
        var mac = PairingMac("7K4Q-29XD-M3PA", content);
        Assert.Equal(Base64Url(HMACSHA256.HashData("7K4Q29XDM3PA"u8, Encoding.UTF8.GetBytes(Canonical(content)))), mac);
        Assert.Equal(mac, PairingMac("7k4q 29xd m3pa", content));
        Assert.NotEqual(mac, PairingMac("7K4Q-29XD-M3PB", content));
        Assert.True(MacMatches(mac, mac));
        Assert.False(MacMatches(null, mac));
    }
}

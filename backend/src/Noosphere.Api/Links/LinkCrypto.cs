using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Noosphere.Api.Links;

/// <summary>
/// Cryptography of the link protocol between Noosphere and gestion-mep (docs/en/links.md), with the .NET base library
/// only, so that the other end can mirror it line for line:
/// <list type="bullet">
/// <item>"Keys and cryptography": P-256 public keys as JWK, RFC 7638 thumbprints (key ids), the four-word fingerprint;</item>
/// <item>"Signed requests": RFC 9421 signature base, ecdsa-p256-sha256 signatures (64-byte r||s), RFC 9530 Content-Digest;</item>
/// <item>"Encrypted bodies": JWE compact serialization, ECDH-ES (direct key agreement) with A256GCM and the RFC 7518
/// §4.6.2 Concat KDF;</item>
/// <item>"Pairing": canonical JSON (RFC 8785 subset: objects, arrays and strings) and the HMAC-SHA256 pairing MAC.</item>
/// </list>
/// </summary>
public static class LinkCrypto
{
    public const string SignatureLabel = "link";
    public const string Algorithm = "ecdsa-p256-sha256";
    public const string RequestTag = "noosphere-link";
    public const string ResponseTag = "noosphere-link-response";
    public const string JoseContentType = "application/jose";

    // ---------- Base64url ----------

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url without padding (padding is tolerated). Throws <see cref="FormatException"/>.</summary>
    public static byte[] FromBase64Url(string text)
    {
        if (text.Contains('+', StringComparison.Ordinal) || text.Contains('/', StringComparison.Ordinal)) throw new FormatException("Not base64url.");
        var s = text.Replace('-', '+').Replace('_', '/').TrimEnd('=');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    /// <summary>16 random bytes, base64url: the nonce of a signature or of a pairing request.</summary>
    public static string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(16));

    // ---------- Keys (JWK, RFC 7638 thumbprint, fingerprint) ----------

    /// <summary>A P-256 public key as a JWK: <c>{kty:"EC", crv:"P-256", x, y}</c>, coordinates in base64url.</summary>
    public sealed record Jwk(string Kty, string Crv, string X, string Y)
    {
        public static Jwk From(ECParameters p) => new("EC", "P-256", Base64Url(p.Q.X), Base64Url(p.Q.Y));

        public static Jwk From(ECDsa key) => From(key.ExportParameters(false));

        public static Jwk From(ECDiffieHellman key) => From(key.ExportParameters(false));

        /// <summary>Checks the members and the point (on the curve). Throws <see cref="CryptographicException"/> or <see cref="FormatException"/>.</summary>
        public ECParameters Parameters()
        {
            if (Kty != "EC" || Crv != "P-256") throw new CryptographicException("Expected an EC P-256 key.");
            var (x, y) = (FromBase64Url(X), FromBase64Url(Y));
            if (x.Length != 32 || y.Length != 32) throw new CryptographicException("P-256 coordinates are 32 bytes.");
            var p = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } };
            p.Validate();
            // Importing checks that the point is on the curve.
            using (var check = ECDiffieHellman.Create()) check.ImportParameters(p);
            return p;
        }

        public ECDsa SigningKey()
        {
            var key = ECDsa.Create();
            key.ImportParameters(Parameters());
            return key;
        }

        public ECDiffieHellman AgreementKey()
        {
            var key = ECDiffieHellman.Create();
            key.ImportParameters(Parameters());
            return key;
        }

        /// <summary>The members required by RFC 7638 §3.2, in lexicographic order, without whitespace.</summary>
        public string ThumbprintInput() => $"{{\"crv\":\"{Crv}\",\"kty\":\"{Kty}\",\"x\":\"{X}\",\"y\":\"{Y}\"}}";

        /// <summary>RFC 7638 thumbprint, SHA-256, raw 32 bytes.</summary>
        public byte[] Thumbprint() => SHA256.HashData(Encoding.UTF8.GetBytes(ThumbprintInput()));

        /// <summary>The key id: the RFC 7638 thumbprint in base64url.</summary>
        public string Kid() => Base64Url(Thumbprint());

        public JsonObject ToJson() => new() { ["kty"] = Kty, ["crv"] = Crv, ["x"] = X, ["y"] = Y };

        /// <summary>Reads <c>{kty, crv, x, y}</c>; other members are ignored. Throws <see cref="FormatException"/>.</summary>
        public static Jwk FromJson(JsonNode? node)
        {
            if (node is not JsonObject o) throw new FormatException("A JWK object is expected.");
            static string Member(JsonObject o, string name) =>
                o[name] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : throw new FormatException($"JWK member '{name}' is missing.");
            return new Jwk(Member(o, "kty"), Member(o, "crv"), Member(o, "x"), Member(o, "y"));
        }

        public static Jwk Parse(string json) => FromJson(JsonNode.Parse(json));

        public string Serialize() => ToJson().ToJsonString();
    }

    /// <summary>Words of the fingerprint, index 0 to 255 (identical in both products).</summary>
    public static readonly IReadOnlyList<string> Words =
    [
        "abbey", "acorn", "adobe", "agate", "alder", "alloy", "amber", "anchor", "anvil", "apron", "arbor", "arch", "arrow", "aspen", "atlas", "attic",
        "badge", "bagel", "baker", "bamboo", "banjo", "barley", "basil", "beacon", "bear", "beech", "bell", "berry", "birch", "bison", "blaze", "bloom",
        "board", "bonsai", "boot", "brass", "bread", "brick", "brook", "broom", "bucket", "bugle", "cabin", "cable", "cactus", "camel", "candle", "canoe",
        "canyon", "cargo", "carpet", "castle", "cedar", "cello", "chalk", "charm", "cherry", "chess", "cider", "cinder", "citrus", "clay", "cliff", "clock",
        "clover", "cobalt", "cocoa", "comet", "copper", "coral", "cotton", "crane", "crater", "crayon", "creek", "crown", "crystal", "cumin", "dahlia",
        "daisy", "delta", "desert", "dingo", "dolphin", "dome", "dove", "dragon", "drum", "dune", "eagle", "easel", "echo", "eclipse", "elbow", "ember",
        "emerald", "engine", "fable", "falcon", "fennel", "fern", "ferry", "fiddle", "fig", "flame", "flask", "flint", "flute", "forest", "fossil",
        "fox", "frost", "gable", "garnet", "geyser", "ginger", "glacier", "globe", "goose", "granite", "grape", "gravel", "guitar", "gull", "hammer",
        "harbor", "harp", "hazel", "heron", "hickory", "hill", "honey", "horizon", "husky", "igloo", "indigo", "iris", "island", "ivory", "jade", "jasmine",
        "jelly", "jet", "juniper", "kayak", "kelp", "kettle", "kiwi", "koala", "lagoon", "lamp", "lantern", "larch", "lark", "lava", "lemon", "lilac",
        "lily", "linen", "lion", "llama", "lotus", "lumen", "lynx", "magnet", "mango", "maple", "marble", "meadow", "melon", "mesa", "meteor", "mint",
        "mirror", "mocha", "moose", "moss", "muffin", "nectar", "needle", "nest", "nickel", "nimbus", "nova", "nutmeg", "oak", "oasis", "ocean", "olive",
        "onyx", "opal", "orbit", "orchid", "otter", "oyster", "paddle", "palm", "panda", "paper", "parrot", "pebble", "pecan", "pepper", "pine", "pixel",
        "plum", "polar", "poppy", "prairie", "prism", "pumpkin", "quartz", "quill", "rabbit", "radar", "raven", "reef", "ribbon", "river", "robin",
        "rocket", "rose", "ruby", "saddle", "saffron", "sage", "salmon", "sand", "satin", "sequoia", "shell", "sierra", "silver", "sparrow", "spruce",
        "summit", "swan", "tango", "thistle", "thunder", "tiger", "timber", "topaz", "torch", "tulip", "tundra", "velvet", "violet", "walnut", "willow",
        "wren", "yarrow", "yew", "zebra", "zenith", "zinc",
    ];

    /// <summary>
    /// The four-word fingerprint of a link: the raw thumbprints of the four public keys (two per side) sorted in ascending
    /// byte order, concatenated and hashed (SHA-256, H); then <c>word(H[0]) word(H[1]) ((H[2]&lt;&lt;8 | H[3]) % 10000, 4 digits) word(H[4])</c>.
    /// </summary>
    public static string Fingerprint(IEnumerable<Jwk> keys)
    {
        var thumbprints = keys.Select(k => k.Thumbprint()).ToList();
        if (thumbprints.Count != 4) throw new ArgumentException("A link fingerprint covers four keys.", nameof(keys));
        thumbprints.Sort((a, b) => a.AsSpan().SequenceCompareTo(b));
        var h = SHA256.HashData(thumbprints.SelectMany(t => t).ToArray());
        var number = ((h[2] << 8) | h[3]) % 10000;
        return $"{Words[h[0]]} {Words[h[1]]} {number.ToString("D4", CultureInfo.InvariantCulture)} {Words[h[4]]}";
    }

    /// <summary>Fingerprints compare without case and extra spaces.</summary>
    public static string NormalizeFingerprint(string? text) =>
        string.Join(' ', (text ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // ---------- Content-Digest (RFC 9530) ----------

    /// <summary><c>sha-256=:&lt;base64&gt;:</c> of the body bytes as sent.</summary>
    public static string ContentDigest(ReadOnlySpan<byte> body) => $"sha-256=:{Convert.ToBase64String(SHA256.HashData(body))}:";

    /// <summary>True when the header holds a <c>sha-256</c> member equal to the digest of the body.</summary>
    public static bool DigestMatches(string? header, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        var expected = SHA256.HashData(body);
        foreach (var member in SplitTopLevel(header, ','))
        {
            var eq = member.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0 || member[..eq].Trim() != "sha-256") continue;
            var value = member[(eq + 1)..].Trim();
            if (value.Length < 2 || value[0] != ':' || value[^1] != ':') return false;
            try
            {
                return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(value[1..^1]), expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }
        return false;
    }

    // ---------- HTTP Message Signatures (RFC 9421) ----------

    /// <summary>Signature parameters of a link signature, serialized in this order: created, nonce, keyid, alg, tag.</summary>
    public sealed record SignatureParameters(IReadOnlyList<string> Components, long Created, string Nonce, string KeyId, string Alg, string Tag)
    {
        /// <summary>The Inner List value: <c>("@method" "@path");created=1;nonce="n";keyid="k";alg="a";tag="t"</c>.</summary>
        public string Serialize() =>
            "(" + string.Join(' ', Components.Select(c => $"\"{c}\"")) + ")"
            + $";created={Created.ToString(CultureInfo.InvariantCulture)};nonce=\"{Nonce}\";keyid=\"{KeyId}\";alg=\"{Alg}\";tag=\"{Tag}\"";
    }

    /// <summary>
    /// The signature base (RFC 9421 §2.5): one line <c>"name": value</c> per covered component, in order, separated by LF,
    /// then the <c>"@signature-params"</c> line with the serialized parameters. No trailing LF.
    /// </summary>
    public static string SignatureBase(IEnumerable<(string Name, string Value)> components, string serializedParameters)
    {
        var b = new StringBuilder();
        foreach (var (name, value) in components) b.Append('"').Append(name).Append("\": ").Append(value).Append('\n');
        return b.Append("\"@signature-params\": ").Append(serializedParameters).ToString();
    }

    /// <summary><c>@query</c>: the query with its leading <c>?</c>, or <c>?</c> alone when empty (RFC 9421 §2.2.7).</summary>
    public static string QueryComponent(string? query) => string.IsNullOrEmpty(query) || query == "?" ? "?" : query[0] == '?' ? query : "?" + query;

    /// <summary>ECDSA P-256 with SHA-256; the 64-byte r||s signature.</summary>
    public static byte[] Sign(ECDsa key, string signatureBase) =>
        key.SignData(Encoding.ASCII.GetBytes(signatureBase), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool Verify(ECDsa key, string signatureBase, byte[] signature) =>
        signature.Length == 64
        && key.VerifyData(Encoding.ASCII.GetBytes(signatureBase), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>A member of a <c>Signature-Input</c> field: its raw value (used as is in the signature base), components and parameters.</summary>
    public sealed record ParsedSignatureInput(string Raw, IReadOnlyList<string> Components, IReadOnlyDictionary<string, string> Parameters)
    {
        public long? Created => Parameters.TryGetValue("created", out var c) && long.TryParse(c, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;
        public string? Nonce => Parameters.GetValueOrDefault("nonce");
        public string? KeyId => Parameters.GetValueOrDefault("keyid");
        public string? Alg => Parameters.GetValueOrDefault("alg");
        public string? Tag => Parameters.GetValueOrDefault("tag");
    }

    /// <summary>Reads the member <paramref name="label"/> of a <c>Signature-Input</c> field; null when absent or malformed.</summary>
    public static ParsedSignatureInput? ParseSignatureInput(string? header, string label = SignatureLabel)
    {
        if (Member(header, label) is not { } raw || raw.Length == 0 || raw[0] != '(') return null;
        var close = raw.IndexOf(')', StringComparison.Ordinal);
        if (close < 0) return null;
        var components = new List<string>();
        foreach (var item in raw[1..close].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (item.Length < 2 || item[0] != '"' || item[^1] != '"') return null;
            components.Add(item[1..^1]);
        }
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in SplitTopLevel(raw[(close + 1)..], ';'))
        {
            var part = p.Trim();
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) return null;
            var value = part[(eq + 1)..];
            if (value.StartsWith('"'))
            {
                if (value.Length < 2 || !value.EndsWith('"')) return null;
                value = value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
            }
            parameters[part[..eq]] = value;
        }
        return new ParsedSignatureInput(raw, components, parameters);
    }

    /// <summary>Reads the byte sequence of member <paramref name="label"/> of a <c>Signature</c> field; null when absent or malformed.</summary>
    public static byte[]? ParseSignature(string? header, string label = SignatureLabel)
    {
        if (Member(header, label) is not { Length: >= 2 } raw || raw[0] != ':' || raw[^1] != ':') return null;
        try
        {
            return Convert.FromBase64String(raw[1..^1]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The raw value of a member of a structured field dictionary (<c>label=value, other=value</c>).</summary>
    private static string? Member(string? header, string label)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        foreach (var member in SplitTopLevel(header, ','))
        {
            var m = member.Trim();
            var eq = m.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && m[..eq] == label) return m[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>Splits on a separator outside quoted strings, parentheses and byte sequences.</summary>
    private static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var (depth, quoted, bytes, start) = (0, false, false, 0);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '\\') i++;
                else if (c == '"') quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == ':') bytes = !bytes;
            else if (bytes) continue;
            else if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == separator && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    // ---------- JWE: ECDH-ES + A256GCM, compact serialization ----------

    /// <summary>
    /// Concat KDF (RFC 7518 §4.6.2, NIST SP 800-56A), one SHA-256 round per 256 bits: H(counter || Z || AlgorithmID ||
    /// PartyUInfo || PartyVInfo || SuppPubInfo), where AlgorithmID, PartyUInfo and PartyVInfo are length-prefixed (32-bit
    /// big endian) and SuppPubInfo is the key length in bits (32-bit big endian).
    /// </summary>
    public static byte[] ConcatKdf(ReadOnlySpan<byte> z, string algorithmId, int keyDataLenBits, ReadOnlySpan<byte> partyUInfo = default, ReadOnlySpan<byte> partyVInfo = default)
    {
        var otherInfo = new List<byte>();
        void LengthPrefixed(ReadOnlySpan<byte> data)
        {
            otherInfo.AddRange(BigEndian((uint)data.Length));
            otherInfo.AddRange(data.ToArray());
        }
        LengthPrefixed(Encoding.ASCII.GetBytes(algorithmId));
        LengthPrefixed(partyUInfo);
        LengthPrefixed(partyVInfo);
        otherInfo.AddRange(BigEndian((uint)keyDataLenBits));

        var length = keyDataLenBits / 8;
        var output = new byte[length];
        var written = 0;
        for (uint counter = 1; written < length; counter++)
        {
            var round = SHA256.HashData([.. BigEndian(counter), .. z, .. otherInfo]);
            var take = Math.Min(round.Length, length - written);
            round.AsSpan(0, take).CopyTo(output.AsSpan(written));
            written += take;
        }
        return output;
    }

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>
    /// Encrypts a JSON payload to the recipient's encryption key. Protected header, in this order:
    /// <c>{"alg":"ECDH-ES","enc":"A256GCM","kid":…,"epk":{"kty":"EC","crv":"P-256","x":…,"y":…},"cty":"application/json"}</c>;
    /// empty encrypted key; 96-bit IV; AAD = ASCII of the base64url protected header.
    /// </summary>
    public static string Encrypt(ReadOnlySpan<byte> payload, Jwk recipient)
    {
        using var peer = recipient.AgreementKey();
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var epk = Jwk.From(ephemeral);
        var header = "{\"alg\":\"ECDH-ES\",\"enc\":\"A256GCM\",\"kid\":\"" + recipient.Kid() + "\",\"epk\":{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\""
            + epk.X + "\",\"y\":\"" + epk.Y + "\"},\"cty\":\"application/json\"}";
        var protectedHeader = Base64Url(Encoding.UTF8.GetBytes(header));
        var z = ephemeral.DeriveRawSecretAgreement(peer.PublicKey);
        var cek = ConcatKdf(z, "A256GCM", 256);
        CryptographicOperations.ZeroMemory(z);
        var iv = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16)) aes.Encrypt(iv, payload, ciphertext, tag, Encoding.ASCII.GetBytes(protectedHeader));
        CryptographicOperations.ZeroMemory(cek);
        return $"{protectedHeader}..{Base64Url(iv)}.{Base64Url(ciphertext)}.{Base64Url(tag)}";
    }

    public static string EncryptJson(JsonNode payload, Jwk recipient) => Encrypt(Encoding.UTF8.GetBytes(payload.ToJsonString()), recipient);

    /// <summary>The <c>kid</c> of a JWE protected header (the recipient key id); null when unreadable.</summary>
    public static string? JweKid(string compact)
    {
        try
        {
            var dot = compact.IndexOf('.', StringComparison.Ordinal);
            return dot <= 0 ? null : JsonNode.Parse(FromBase64Url(compact[..dot]))?["kid"]?.GetValue<string>();
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Decrypts a JWE compact serialization with the recipient's private key. Throws <see cref="CryptographicException"/>.</summary>
    public static byte[] Decrypt(string compact, ECDiffieHellman recipient)
    {
        var parts = compact.Trim().Split('.');
        if (parts.Length != 5) throw new CryptographicException("A JWE compact serialization has five parts.");
        if (parts[1].Length != 0) throw new CryptographicException("ECDH-ES uses direct key agreement: the encrypted key is empty.");
        try
        {
            var header = JsonNode.Parse(FromBase64Url(parts[0])) as JsonObject ?? throw new CryptographicException("Invalid JWE header.");
            if (Text(header["alg"]) != "ECDH-ES" || Text(header["enc"]) != "A256GCM") throw new CryptographicException("Expected alg ECDH-ES and enc A256GCM.");
            var epk = Jwk.FromJson(header["epk"]);
            using var ephemeral = epk.AgreementKey();
            var z = recipient.DeriveRawSecretAgreement(ephemeral.PublicKey);
            var apu = Text(header["apu"]) is { } u ? FromBase64Url(u) : [];
            var apv = Text(header["apv"]) is { } v ? FromBase64Url(v) : [];
            var cek = ConcatKdf(z, "A256GCM", 256, apu, apv);
            CryptographicOperations.ZeroMemory(z);
            var (iv, ciphertext, tag) = (FromBase64Url(parts[2]), FromBase64Url(parts[3]), FromBase64Url(parts[4]));
            if (iv.Length != 12 || tag.Length != 16) throw new CryptographicException("Invalid IV or tag length.");
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(cek, 16)) aes.Decrypt(iv, ciphertext, tag, plaintext, Encoding.ASCII.GetBytes(parts[0]));
            CryptographicOperations.ZeroMemory(cek);
            return plaintext;
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            throw new CryptographicException("Invalid JWE: " + e.Message, e);
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // ---------- Pairing: canonical JSON and MAC ----------

    /// <summary>
    /// Canonical JSON (RFC 8785 subset): object members sorted by UTF-16 code units, no whitespace, strings escaped as
    /// ECMAScript JSON.stringify does (only <c>"</c>, <c>\</c> and control characters; non-ASCII stays as UTF-8 text).
    /// Objects, arrays, strings, booleans and null are supported; numbers are refused.
    /// </summary>
    public static string Canonical(JsonNode? node)
    {
        var b = new StringBuilder();
        Write(b, node);
        return b.ToString();
    }

    private static void Write(StringBuilder b, JsonNode? node)
    {
        switch (node)
        {
            case null:
                b.Append("null");
                break;
            case JsonObject o:
                b.Append('{');
                var first = true;
                foreach (var (key, value) in o.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first) b.Append(',');
                    first = false;
                    WriteString(b, key);
                    b.Append(':');
                    Write(b, value);
                }
                b.Append('}');
                break;
            case JsonArray a:
                b.Append('[');
                for (var i = 0; i < a.Count; i++)
                {
                    if (i > 0) b.Append(',');
                    Write(b, a[i]);
                }
                b.Append(']');
                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                WriteString(b, s);
                break;
            case JsonValue v when v.TryGetValue<bool>(out var flag):
                b.Append(flag ? "true" : "false");
                break;
            default:
                throw new ArgumentException("Canonical JSON of the link protocol holds objects, arrays, strings, booleans and null only.", nameof(node));
        }
    }

    private static void WriteString(StringBuilder b, string s)
    {
        b.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                case < ' ': b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: b.Append(c); break;
            }
        }
        b.Append('"');
    }

    /// <summary>The pairing code as a MAC key: upper case, dashes and spaces removed.</summary>
    public static string NormalizeCode(string? code) =>
        new((code ?? "").Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());

    /// <summary>HMAC-SHA256 keyed with the UTF-8 of the normalized code, over the UTF-8 of the canonical JSON; base64url.</summary>
    public static string PairingMac(string code, JsonNode content) =>
        Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(NormalizeCode(code)), Encoding.UTF8.GetBytes(Canonical(content))));

    /// <summary>Constant-time comparison of a received MAC with the expected one.</summary>
    public static bool MacMatches(string? received, string expected) =>
        received is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(received), Encoding.ASCII.GetBytes(expected));
}

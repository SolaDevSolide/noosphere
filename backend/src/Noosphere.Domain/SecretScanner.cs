using System.Text.RegularExpressions;

namespace Noosphere.Domain;

/// <summary>
/// Finds secrets pasted into documentation: private keys, cloud and SaaS tokens, JWTs, passwords in URLs and connection
/// strings. Noosphere documents where secrets live; it never stores them. Placeholders (<c>&lt;password&gt;</c>,
/// <c>***</c>, <c>changeme</c>, <c>${VAR}</c>, <c>$VAR</c>, <c>%VAR%</c>, <c>{{var}}</c>) are allowed.
/// </summary>
public static partial class SecretScanner
{
    /// <summary>What was found; never the secret itself.</summary>
    /// <param name="Kind">Human-readable name of the kind of secret, e.g. "GitHub token".</param>
    public sealed record Match(string Kind);

    public const string PrivateKey = "private key";
    public const string AwsAccessKey = "AWS access key";
    public const string AwsSecretKey = "AWS secret access key";
    public const string GitHubToken = "GitHub token";
    public const string SlackToken = "Slack token";
    public const string StripeKey = "Stripe secret key";
    public const string GoogleApiKey = "Google API key";
    public const string AzureStorageKey = "Azure storage account key";
    public const string Jwt = "JSON Web Token";
    public const string UrlPassword = "password in a URL";
    public const string Password = "password";

    [GeneratedRegex(@"-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----", RegexOptions.CultureInvariant)]
    private static partial Regex PemPattern();

    [GeneratedRegex(@"(?<![A-Z0-9])(?:AKIA|ASIA)[A-Z0-9]{16}(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex AwsKeyIdPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9/+=])[A-Za-z0-9/+]{40}(?![A-Za-z0-9/+=])", RegexOptions.CultureInvariant)]
    private static partial Regex Base64SecretPattern();

    [GeneratedRegex(@"aws_?secret_?access_?key\W{0,3}[=:]\s*[""']?(?<value>[A-Za-z0-9/+]{40})(?![A-Za-z0-9/+=])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AwsSecretAssignmentPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{22,255})(?![A-Za-z0-9_])", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])xox[abposre]-[A-Za-z0-9-]{10,}", RegexOptions.CultureInvariant)]
    private static partial Regex SlackPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[sr]k_live_[A-Za-z0-9]{16,}", RegexOptions.CultureInvariant)]
    private static partial Regex StripePattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])AIza[0-9A-Za-z_-]{35}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant)]
    private static partial Regex GooglePattern();

    [GeneratedRegex(@"AccountKey\s*=\s*(?<value>[^;\s""']+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AzurePattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"\b[a-z][a-z0-9+.-]{1,30}://(?<user>[^/\s:@'""<>]+):(?<value>[^/\s@'""]+)@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:password|passwd|pwd|secret)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>\{\{[^{}]*\}\})|(?<value>[^;\s,&'""]+))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PasswordPattern();

    [GeneratedRegex(@"^(?:<[^<>]*>|\*{3,}|changeme|\$\{[^{}]+\}|\$[A-Za-z_][A-Za-z0-9_]*|%[^%\s]+%|\{\{[^{}]+\}\})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderPattern();

    /// <summary>A value that stands for a secret without being one: <c>&lt;password&gt;</c>, <c>***</c>, <c>changeme</c>, <c>${VAR}</c>, <c>$VAR</c>, <c>%VAR%</c>, <c>{{var}}</c>.</summary>
    public static bool IsPlaceholder(string value) => PlaceholderPattern().IsMatch(value.Trim());

    /// <summary>The first secret found in <paramref name="text"/>, or null.</summary>
    public static Match? Find(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (PemPattern().IsMatch(text)) return new(PrivateKey);
        if (AwsSecretAssignmentPattern().IsMatch(text)) return new(AwsSecretKey);
        foreach (System.Text.RegularExpressions.Match key in AwsKeyIdPattern().Matches(text))
        {
            // A 40-character base64 value next to the key id is its secret.
            var start = Math.Max(0, key.Index - 200);
            var around = text.Substring(start, Math.Min(text.Length, key.Index + key.Length + 200) - start);
            return new(Base64SecretPattern().Matches(around).Any(m => m.Value.Any(char.IsDigit) && m.Value.Any(char.IsLower) && m.Value.Any(char.IsUpper))
                ? AwsSecretKey : AwsAccessKey);
        }
        if (GitHubPattern().IsMatch(text)) return new(GitHubToken);
        if (SlackPattern().IsMatch(text)) return new(SlackToken);
        if (StripePattern().IsMatch(text)) return new(StripeKey);
        if (GooglePattern().IsMatch(text)) return new(GoogleApiKey);
        if (AzurePattern().Matches(text).Any(m => !IsPlaceholder(m.Groups["value"].Value))) return new(AzureStorageKey);
        if (JwtPattern().IsMatch(text)) return new(Jwt);
        if (UrlPattern().Matches(text).Any(m => !IsPlaceholder(m.Groups["value"].Value))) return new(UrlPassword);
        if (PasswordPattern().Matches(text).Any(m => m.Groups["value"].Value.Trim().Length > 0 && !IsPlaceholder(m.Groups["value"].Value))) return new(Password);
        return null;
    }

    /// <summary>Message returned for a field holding a secret (the value is never repeated).</summary>
    public static string Message(Match match) =>
        $"This looks like a secret ({match.Kind}). Noosphere is documentation, not a vault: store it in your secret manager and write where it lives instead.";
}

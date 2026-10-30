namespace Noosphere.Api;

/// <summary>
/// Configuration of an instance. Every key is read from an environment variable with the <c>NOOSPHERE_</c> prefix
/// (<c>NOOSPHERE_DEMO</c> sets <c>DEMO</c>) or from appsettings.json. See docs/en/configuration.md.
/// </summary>
public sealed record Settings(
    string Database,
    bool Demo,
    string AdminUsername,
    string? AdminPassword,
    int LoginAttemptsPerMinute,
    string? KeysDirectory,
    string? BackupsDirectory,
    string InstanceName,
    string? PublicUrl,
    string TrustedProxies,
    int AccessLogDays = 365)
{
    public const string EnvironmentPrefix = "NOOSPHERE_";

    public static Settings From(IConfiguration config) => new(
        Database: config["DATABASE"] is { Length: > 0 } db ? db : config.GetConnectionString("Noosphere") ?? "",
        Demo: config.GetValue("DEMO", false),
        AdminUsername: config["ADMIN_USERNAME"] is { Length: > 0 } u ? u.Trim().ToLowerInvariant() : "admin",
        AdminPassword: config["ADMIN_PASSWORD"] is { Length: > 0 } p ? p : null,
        LoginAttemptsPerMinute: Math.Max(1, config.GetValue("LOGIN_RATE_LIMIT", 10)),
        KeysDirectory: Blank(config["KEYS_DIR"]),
        BackupsDirectory: Blank(config["BACKUPS_DIR"]),
        InstanceName: Blank(config["INSTANCE_NAME"]) ?? "Noosphere",
        PublicUrl: Blank(config["PUBLIC_URL"])?.TrimEnd('/'),
        TrustedProxies: config["TRUSTED_PROXIES"] ?? "",
        AccessLogDays: Math.Max(1, config.GetValue("ACCESS_LOG_DAYS", 365)));

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Configuration sources shared by the web application and the command line.</summary>
    public static void AddSources(IConfigurationBuilder builder) => builder.AddEnvironmentVariables(EnvironmentPrefix);
}

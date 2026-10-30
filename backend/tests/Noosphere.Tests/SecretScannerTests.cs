using Noosphere.Domain;

namespace Noosphere.Tests;

/// <summary>Every kind of secret is found, every placeholder is allowed, and the message never repeats the value.</summary>
public class SecretScannerTests
{
    [Theory]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA\n-----END RSA PRIVATE KEY-----", SecretScanner.PrivateKey)]
    [InlineData("-----BEGIN PRIVATE KEY-----\nMIIEvQ", SecretScanner.PrivateKey)]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----", SecretScanner.PrivateKey)]
    [InlineData("-----BEGIN EC PRIVATE KEY-----", SecretScanner.PrivateKey)]
    [InlineData("Key id AKIAIOSFODNN7EXAMPLE for the backup bucket.", SecretScanner.AwsAccessKey)]
    [InlineData("AKIAIOSFODNN7EXAMPLE / wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", SecretScanner.AwsSecretKey)]
    [InlineData("aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", SecretScanner.AwsSecretKey)]
    [InlineData("token ghp_16C7e42F292c6912E7710c838347Ae178B4a in CI", SecretScanner.GitHubToken)]
    [InlineData("gho_16C7e42F292c6912E7710c838347Ae178B4a", SecretScanner.GitHubToken)]
    [InlineData("ghs_16C7e42F292c6912E7710c838347Ae178B4a", SecretScanner.GitHubToken)]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJ", SecretScanner.GitHubToken)]
    [InlineData("Bot token xox" + "b-123456789012-1234567890123-AbCdEfGhIjKlMnOpQrStUvWx", SecretScanner.SlackToken)]
    [InlineData("xox" + "p-123456789012-123456789012-abcdef", SecretScanner.SlackToken)]
    [InlineData("STRIPE_KEY=sk_" + "live_4eC39HqLyjWDarjtT1zdp7dc", SecretScanner.StripeKey)]
    [InlineData("maps key AIzaSyDdI0hCZtE6vySjMm-WEfRq3CPzqKqqsHI", SecretScanner.GoogleApiKey)]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=alize;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq==;EndpointSuffix=core.windows.net", SecretScanner.AzureStorageKey)]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U", SecretScanner.Jwt)]
    [InlineData("Replica at postgres://billing:S3cr3t!@db.internal:5432/billing", SecretScanner.UrlPassword)]
    [InlineData("https://deploy:hunter22@git.alize.example/billing.git", SecretScanner.UrlPassword)]
    [InlineData("Server=db;Database=billing;User Id=app;Password=Tr0ub4dor&3;", SecretScanner.Password)]
    [InlineData("Host=db;Username=app;pwd=hunter22", SecretScanner.Password)]
    [InlineData("client secret = 'p@ssw0rd-42'", SecretScanner.Password)]
    [InlineData("PASSWORD = \"very secret\"", SecretScanner.Password)]
    public void Finds_each_kind_of_secret(string text, string kind) => Assert.Equal(kind, SecretScanner.Find(text)?.Kind);

    [Theory]
    [InlineData("Server=db;User Id=app;Password=<password>;")]
    [InlineData("Server=db;User Id=app;Password=***;")]
    [InlineData("Server=db;User Id=app;Password=changeme;")]
    [InlineData("Server=db;User Id=app;Password=${BILLING_DB_PASSWORD};")]
    [InlineData("Server=db;User Id=app;Password=$BILLING_DB_PASSWORD;")]
    [InlineData("Server=db;User Id=app;Password=%BILLING_DB_PASSWORD%;")]
    [InlineData("Server=db;User Id=app;Password={{ billing_db_password }};")]
    [InlineData("postgres://billing:<password>@db.internal:5432/billing")]
    [InlineData("postgres://billing:***@db.internal/billing")]
    [InlineData("postgres://billing:${PG_PASSWORD}@db.internal/billing")]
    [InlineData("postgres://billing:%PG_PASSWORD%@db.internal/billing")]
    [InlineData("postgres://billing:{{pg_password}}@db.internal/billing")]
    [InlineData("postgres://billing:changeme@db.internal/billing")]
    [InlineData("AccountName=alize;AccountKey=<key>;")]
    [InlineData("AccountName=alize;AccountKey=${STORAGE_KEY};")]
    public void Allows_placeholders(string text) => Assert.Null(SecretScanner.Find(text));

    [Theory]
    [InlineData("The password is kept in the vault at finance/billing/run.")]
    [InlineData("Ask Team Platform for a token; never paste it here.")]
    [InlineData("https://billing.alize.example:8443/api/v4/invoices")]
    [InlineData("mailto:billing@alize.example")]
    [InlineData("Ticket ghp-1234 and AKIA in prose")]
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_documentation_alone(string? text) => Assert.Null(SecretScanner.Find(text));

    [Theory]
    [InlineData("<password>")]
    [InlineData("***")]
    [InlineData("******")]
    [InlineData("changeme")]
    [InlineData("CHANGEME")]
    [InlineData("${VAR}")]
    [InlineData("$VAR")]
    [InlineData("%VAR%")]
    [InlineData("{{var}}")]
    public void Recognises_each_placeholder(string value) => Assert.True(SecretScanner.IsPlaceholder(value));

    [Theory]
    [InlineData("hunter22")]
    [InlineData("**")]
    [InlineData("change-me-later")]
    public void Real_values_are_not_placeholders(string value) => Assert.False(SecretScanner.IsPlaceholder(value));

    [Fact]
    public void The_message_names_the_kind_and_never_the_value()
    {
        const string token = "ghp_16C7e42F292c6912E7710c838347Ae178B4a";
        var message = SecretScanner.Message(SecretScanner.Find(token)!);
        Assert.Equal("This looks like a secret (GitHub token). Noosphere is documentation, not a vault: store it in your secret manager and write where it lives instead.", message);
        Assert.DoesNotContain(token, message, StringComparison.Ordinal);
    }
}

using System.Net;
using Noosphere.Api.Agents;

namespace Noosphere.Tests;

/// <summary>Client ID Metadata Documents and redirect URIs: what is accepted and refused, without a server.</summary>
public class ClientMetadataTests
{
    [Theory]
    [InlineData("https://agent.example/client.json")]
    [InlineData("https://agent.example:8443/oauth/client")]
    public void Metadata_urls_are_https_with_a_path(string url) => Assert.Equal(url, ClientMetadata.CheckUrl(url).ToString());

    [Theory]
    [InlineData("http://agent.example/client.json", "https")]
    [InlineData("https://agent.example", "path")]
    [InlineData("https://agent.example/", "path")]
    [InlineData("https://user:pass@agent.example/client.json", "credentials")]
    [InlineData("https://agent.example/client.json#x", "fragment")]
    [InlineData("https://agent.example/a/../client.json", "dot segments")]
    [InlineData("https://localhost/client.json", "local")]
    [InlineData("https://app.localhost/client.json", "local")]
    [InlineData("https://127.0.0.1/client.json", "local")]
    [InlineData("https://10.1.2.3/client.json", "private")]
    [InlineData("https://192.168.1.10/client.json", "private")]
    [InlineData("https://169.254.169.254/latest/meta-data", "private")]
    [InlineData("https://[::1]/client.json", "local")]
    [InlineData("https://[fd00::1]/client.json", "private")]
    [InlineData("not a url", "valid URL")]
    public void Unsafe_metadata_urls_are_refused(string url, string reason)
    {
        var e = Assert.Throws<ClientMetadataException>(() => ClientMetadata.CheckUrl(url));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void Only_public_addresses_are_fetched(string address, bool expected) =>
        Assert.Equal(expected, ClientMetadata.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("https://app.example/callback", null)]
    [InlineData("http://localhost:3000/callback", null)]
    [InlineData("http://127.0.0.1/callback", null)]
    [InlineData("http://[::1]:9000/cb", null)]
    [InlineData("com.example.app:/oauth", null)]
    [InlineData("vscode://publisher.extension/callback", null)]
    [InlineData("http://app.example/callback", "loopback")]
    [InlineData("https://app.example/callback#x", "fragment")]
    [InlineData("javascript:alert(1)", "not allowed")]
    [InlineData("data:text/html,x", "not allowed")]
    [InlineData("/relative", "absolute")]
    public void Redirect_uris_are_https_loopback_or_native(string uri, string? problem)
    {
        var found = ClientMetadata.RedirectProblem(uri);
        if (problem is null) Assert.Null(found);
        else Assert.Contains(problem, found, StringComparison.Ordinal);
    }

    [Fact]
    public void Loopback_redirects_match_on_any_port_others_exactly()
    {
        Assert.True(ClientMetadata.RedirectMatches("http://127.0.0.1:51000/cb", ["http://127.0.0.1/cb"]));
        Assert.True(ClientMetadata.RedirectMatches("http://localhost:4000/cb", ["http://localhost:3000/cb"]));
        Assert.False(ClientMetadata.RedirectMatches("http://127.0.0.1:51000/other", ["http://127.0.0.1/cb"]));
        Assert.False(ClientMetadata.RedirectMatches("http://localhost:4000/cb", ["http://127.0.0.1:4000/cb"]));
        Assert.True(ClientMetadata.RedirectMatches("https://app.example/cb", ["https://app.example/cb"]));
        Assert.False(ClientMetadata.RedirectMatches("https://app.example:444/cb", ["https://app.example/cb"]));
        Assert.False(ClientMetadata.RedirectMatches("https://app.example/cb?x=1", ["https://app.example/cb"]));
    }

    private const string Id = "https://agent.example/client.json";

    [Fact]
    public void A_document_names_itself_the_client_and_its_redirects()
    {
        var client = ClientMetadata.Parse(Id, """{"client_id":"https://agent.example/client.json","client_name":" Agent ","redirect_uris":["http://127.0.0.1/cb"],"client_uri":"https://agent.example"}""");
        Assert.Equal(("Agent", "https://agent.example", true), (client.ClientName, client.ClientUri, client.MetadataDocument));
        Assert.Equal(["http://127.0.0.1/cb"], client.RedirectUris);
        // A non-https home page is dropped, not refused.
        Assert.Null(ClientMetadata.Parse(Id, """{"client_id":"https://agent.example/client.json","client_name":"A","redirect_uris":["https://a.example/cb"],"client_uri":"javascript:x"}""").ClientUri);
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "JSON object")]
    [InlineData("""{"client_id":"https://other.example/client.json","client_name":"A","redirect_uris":["https://a.example/cb"]}""", "equal its URL")]
    [InlineData("""{"client_id":"https://agent.example/client.json","redirect_uris":["https://a.example/cb"]}""", "client_name")]
    [InlineData("""{"client_id":"https://agent.example/client.json","client_name":"A"}""", "redirect_uris")]
    [InlineData("""{"client_id":"https://agent.example/client.json","client_name":"A","redirect_uris":[]}""", "redirect_uris")]
    [InlineData("""{"client_id":"https://agent.example/client.json","client_name":"A","redirect_uris":["http://a.example/cb"]}""", "loopback")]
    [InlineData("""{"client_id":"https://agent.example/client.json","client_name":"A","redirect_uris":["https://a.example/cb"],"token_endpoint_auth_method":"private_key_jwt"}""", "public clients")]
    public void Invalid_documents_are_refused(string json, string reason)
    {
        var e = Assert.Throws<ClientMetadataException>(() => ClientMetadata.Parse(Id, json));
        Assert.Contains(reason, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fetcher_never_connects_to_a_private_address()
    {
        using var fetcher = new SafeClientMetadataFetcher();
        // Names resolving to loopback are refused when connecting (DNS cannot be used to reach the local network).
        var e = await Assert.ThrowsAsync<ClientMetadataException>(() => fetcher.Fetch(new Uri("https://localhost:1/client.json")));
        Assert.Contains("private or local", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pkce_verifiers_match_their_s256_challenge_only()
    {
        var verifier = AgentKit.Verifier();
        var challenge = AgentKit.Challenge(verifier);
        Assert.True(OpaqueTokens.IsChallenge(challenge));
        Assert.True(OpaqueTokens.VerifyPkce(verifier, challenge));
        Assert.False(OpaqueTokens.VerifyPkce(AgentKit.Verifier(), challenge));
        Assert.False(OpaqueTokens.VerifyPkce("short", challenge));
        Assert.False(OpaqueTokens.VerifyPkce(verifier + "!", challenge));
        Assert.False(OpaqueTokens.IsChallenge(verifier));
    }
}

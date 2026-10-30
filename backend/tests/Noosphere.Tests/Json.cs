using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Noosphere.Tests;

internal static class Json
{
    public static async Task<JsonElement> Read(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    public static async Task<JsonElement> GetJson(this HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK) =>
        await (await client.GetAsync(url)).Read(expected);

    public static async Task<JsonElement> PostJson(this HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK) =>
        await (await client.PostAsJsonAsync(url, body)).Read(expected);

    public static async Task<JsonElement> PutJson(this HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK) =>
        await (await client.PutAsJsonAsync(url, body)).Read(expected);

    public static async Task<HttpStatusCode> Delete(this HttpClient client, string url) => (await client.DeleteAsync(url)).StatusCode;

    public static string Str(this JsonElement e, string property) => e.GetProperty(property).GetString()!;

    public static List<string> Ids(this JsonElement array) => [.. array.EnumerateArray().Select(x => x.Str("id"))];
}

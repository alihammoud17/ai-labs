using System.Net;
using System.Text.Json;
using AiLab.Rag.Tests.Fakes;

namespace AiLab.Rag.Tests;

// The OpenAPI document and Swagger UI exist in Development only, and the document describes what the endpoints return
public sealed class OpenApiTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly List<RagFactory> _factories = [];

    public void Dispose()
    {
        foreach (RagFactory factory in _factories)
            factory.Dispose();
    }

    [Fact]
    public async Task The_document_lists_the_endpoints_with_their_error_codes()
    {
        using HttpClient client = Client("Development");

        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Ct));
        JsonElement paths = document.RootElement.GetProperty("paths");

        Assert.Equal(["/health", "/search", "/dev/seed"], paths.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["200", "400", "409", "502", "503", "500"],
            paths.GetProperty("/search").GetProperty("get").GetProperty("responses").EnumerateObject().Select(r => r.Name));

        // This document lists the Rag codes, not the ticket API's (the record is shared)
        string code = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ErrorResponse").GetProperty("properties").GetProperty("code").GetProperty("description").GetString()!;
        Assert.Contains("collection_missing", code);
        Assert.DoesNotContain("not_a_ticket", code);
    }

    [Fact]
    public async Task Numbers_are_plain_numbers_in_the_schema()
    {
        using HttpClient client = Client("Development");

        using JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", Ct));
        JsonElement distance = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("SearchResponseHit").GetProperty("properties").GetProperty("distance");

        // Without strict number handling this would be ["number", "string"] with a pattern
        Assert.Equal("number", distance.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Swagger_UI_is_served_in_Development()
    {
        using HttpClient client = Client("Development");

        HttpResponseMessage response = await client.GetAsync("/swagger/index.html", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/swagger/index.html")]
    public async Task The_document_and_the_UI_are_not_served_outside_Development(string url)
    {
        using HttpClient client = Client("Testing");

        HttpResponseMessage response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient Client(string environment)
    {
        var factory = new RagFactory(new HashedEmbeddingGenerator(), environment);
        _factories.Add(factory);
        return factory.CreateClient();
    }
}

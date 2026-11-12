using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiLab.Infrastructure.Mcp;

// Gets the MCP token for the SaaS API in two steps and caches both tokens:
//   1. POST LoginUrl { email, password }          → the API token (audience MultiTenantSaasApi, 60 min)
//   2. POST McpTokenUrl, Bearer <API token>       → the MCP token (audience MultiTenantSaasApi/mcp, 15 min)
// The MCP route accepts only the second kind. Both calls go through the "saas-auth" HttpClient, which has no
// BearerTokenHandler: that handler calls this class, so going through it here would loop.
// Never logs a token, the password or a request body.
public class SaasApiTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SaasMcpSettings> options,
    TimeProvider timeProvider,
    ILogger<SaasApiTokenProvider> logger)
{
    public const string HttpClientName = "saas-auth";

    // A token this close to its expiry counts as expired, so it can't run out during a request
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);

    private readonly SaasMcpSettings _settings = options.Value;

    // One refresh at a time, so two requests at the same moment don't both log in
    private readonly SemaphoreSlim _lock = new(1, 1);

    private CachedToken? _apiToken;
    private CachedToken? _mcpToken;

    public async Task<string> GetMcpTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path without the lock
        if (_mcpToken is { } cached && IsFresh(cached))
            return cached.Token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed it while this one waited
            if (_mcpToken is { } current && IsFresh(current))
                return current.Token;

            _mcpToken = await ExchangeWithRetryAsync(cancellationToken);
            logger.LogInformation("SaaS MCP token issued, valid until {ExpiresAt:u}", _mcpToken.ExpiresAt);
            return _mcpToken.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    // The cached API token can be refused (signing key rotated, user logged out everywhere): log in once more and retry
    private async Task<CachedToken> ExchangeWithRetryAsync(CancellationToken cancellationToken)
    {
        string apiToken = await GetApiTokenAsync(cancellationToken);
        CachedToken? mcpToken = await ExchangeAsync(apiToken, cancellationToken);
        if (mcpToken is not null)
            return mcpToken;

        logger.LogInformation("SaaS API token refused by the MCP token exchange; logging in again");
        _apiToken = null;
        apiToken = await GetApiTokenAsync(cancellationToken);
        return await ExchangeAsync(apiToken, cancellationToken)
            ?? throw new InvalidOperationException($"SaaS MCP token exchange failed: 401 even right after logging in ({_settings.McpTokenUrl}).");
    }

    // Called inside the lock
    private async Task<string> GetApiTokenAsync(CancellationToken cancellationToken)
    {
        if (_apiToken is { } cached && IsFresh(cached))
            return cached.Token;

        if (string.IsNullOrWhiteSpace(_settings.Email) || string.IsNullOrWhiteSpace(_settings.Password))
            throw new InvalidOperationException("SaasMcp:Email and SaasMcp:Password must be set in user-secrets.");

        using HttpResponseMessage response = await SendAsync("login", cancellationToken, client =>
            client.PostAsJsonAsync(_settings.LoginUrl, new { email = _settings.Email, password = _settings.Password }, cancellationToken));

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "SaaS login failed: wrong SaasMcp:Email or SaasMcp:Password (401).",
                HttpStatusCode.Forbidden => "SaaS login failed: the tenant is suspended (403).",
                HttpStatusCode.TooManyRequests => "SaaS login failed: rate limited (429); wait a minute.",
                _ => $"SaaS login failed: {(int)response.StatusCode} {response.StatusCode} from {_settings.LoginUrl}.",
            });

        _apiToken = await ReadTokenAsync(response, "login", cancellationToken);
        logger.LogInformation("SaaS API login succeeded, API token valid until {ExpiresAt:u}", _apiToken.ExpiresAt);
        return _apiToken.Token;
    }

    // null on 401, so the caller can log in again once
    private async Task<CachedToken?> ExchangeAsync(string apiToken, CancellationToken cancellationToken)
    {
        // async lambda, so the request is disposed only after the send has finished
        using HttpResponseMessage response = await SendAsync("MCP token exchange", cancellationToken, async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.McpTokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            return await client.SendAsync(request, cancellationToken);
        });

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.Forbidden
                ? "SaaS MCP token exchange failed: the tenant is suspended (403)."
                : $"SaaS MCP token exchange failed: {(int)response.StatusCode} {response.StatusCode} from {_settings.McpTokenUrl}.");

        return await ReadTokenAsync(response, "MCP token exchange", cancellationToken);
    }

    // Turns "nothing is listening" into a message that says what to do
    private async Task<HttpResponseMessage> SendAsync(string step, CancellationToken cancellationToken, Func<HttpClient, Task<HttpResponseMessage>> send)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            return await send(client);
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"SaaS {step} failed: could not reach the SaaS API ({ex.HttpRequestError}). Is it running on the URL in SaasMcp:*?", ex);
        }
    }

    private static async Task<CachedToken> ReadTokenAsync(HttpResponseMessage response, string step, CancellationToken cancellationToken)
    {
        TokenReply? reply = await response.Content.ReadFromJsonAsync<TokenReply>(cancellationToken);
        if (reply is null || string.IsNullOrEmpty(reply.Token))
            throw new InvalidOperationException($"SaaS {step} returned no token.");

        // The server sends UTC with a Z (DateTime.UtcNow); a value without a zone is treated as UTC too,
        // never as local time, or the expiry check would be off by the timezone offset
        DateTime expiresAt = reply.ExpiresAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(reply.ExpiresAt, DateTimeKind.Utc)
            : reply.ExpiresAt.ToUniversalTime();

        return new CachedToken(reply.Token, new DateTimeOffset(expiresAt));
    }

    private bool IsFresh(CachedToken token) => timeProvider.GetUtcNow() < token.ExpiresAt - RefreshMargin;

    // Classes, not records: a record's ToString() would print the token
    private sealed class CachedToken(string token, DateTimeOffset expiresAt)
    {
        public string Token { get; } = token;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }

    // The fields both replies share (login's AuthResponse has more, which are ignored)
    private sealed class TokenReply
    {
        public string Token { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }
}

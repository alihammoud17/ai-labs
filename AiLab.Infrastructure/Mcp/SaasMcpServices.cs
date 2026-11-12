using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Infrastructure.Mcp;

public static class SaasMcpServices
{
    // The HttpClient name of the MCP transport (the one with BearerTokenHandler)
    public const string McpHttpClientName = "saas-mcp";

    // Separate from AddInfrastructure(): only the console calls it, so the API and Rag never register the SaaS client
    // (like AddVectorStore, which only Rag calls). Settings are bound from SaasMcp:*; Email and Password come from user-secrets.
    public static IServiceCollection AddSaasMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SaasMcpSettings>(configuration.GetSection(SaasMcpSettings.SectionName));

        // Singleton, so the cached tokens live as long as the app
        services.AddSingleton<SaasApiTokenProvider>();

        // IHttpClientFactory logs request headers at Trace. Serilog's default is Warning, but redacting every header value
        // keeps the Authorization header out of the log even when someone turns logging up.

        // Login and token exchange: no BearerTokenHandler, which calls the provider and would loop
        services.AddHttpClient(SaasApiTokenProvider.HttpClientName)
            .RedactLoggedHeaders(_ => true);

        // The MCP transport: every request gets the MCP token
        services.AddTransient<BearerTokenHandler>();
        services.AddHttpClient(McpHttpClientName)
            .AddHttpMessageHandler<BearerTokenHandler>()
            .RedactLoggedHeaders(_ => true);

        // One MCP client for the app's lifetime, opened by /saas on and closed by /saas off or at exit
        services.AddSingleton<SaasMcpToolSource>();

        return services;
    }
}

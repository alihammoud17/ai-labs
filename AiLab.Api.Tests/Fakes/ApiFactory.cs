using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Api.Tests.Fakes;

// The real API in memory, with only the provider client replaced by the script.
// Environment "Testing": no user-secrets, so no API key; no Swagger or dev endpoints.
public class ApiFactory(ScriptedChatClient script, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach ((string key, string? value) in settings ?? [])
            builder.UseSetting(key, value);

        // The last registration wins, so the pipeline in Program.cs resolves the script (and any other override)
        builder.ConfigureTestServices(s =>
        {
            s.AddKeyedSingleton<IChatClient>(Program.ProviderClientKey, script);
            services?.Invoke(s);
        });
    }
}

// A clock stopped at one moment, in UTC
public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

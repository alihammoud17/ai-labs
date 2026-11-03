using AiLab.Domain.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Evals.Hosting;

// The real Rag host in memory, as `dotnet run` would start it: Postgres store, Ollama embeddings, Development settings
// and user-secrets. Only two things differ:
// - the keyed "provider" chat client is a ScenarioChatClient, so each iteration chooses where /ask's generation goes;
// - Ask:PermitsPerMinute is raised, so 90 questions don't wait on the 10-per-minute limiter (eval host only).
public sealed class EvalRagFactory(ScenarioChatClient chat) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Ask:PermitsPerMinute", "1000");

        // The connection string and the Anthropic key are in the shared user-secrets (same UserSecretsId as AiLab.Rag).
        builder.ConfigureAppConfiguration(config => config.AddUserSecrets(typeof(Program).Assembly, optional: true));

        // The last registration wins, so Program.cs's UseLogging still wraps it, as in AiLab.Rag.Tests' RagFactory
        builder.ConfigureTestServices(s => s.AddKeyedSingleton<IChatClient>(ChatClientKeys.Provider, chat));
    }
}

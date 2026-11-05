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
// And with AILAB_EVAL_CHUNKER set, Ask:Chunker is that chunker. EvalFixture.Ask reads the host's AskSettings, so /ask,
// the judges' /search context and the retrieval runner all follow it.
public sealed class EvalRagFactory(ScenarioChatClient chat, string? chunker = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Ask:PermitsPerMinute", "1000");
        if (chunker is not null)
            builder.UseSetting("Ask:Chunker", chunker);

        // The connection string and the Anthropic key are in the shared user-secrets (same UserSecretsId as AiLab.Rag).
        builder.ConfigureAppConfiguration(config => config.AddUserSecrets(typeof(Program).Assembly, optional: true));

        // The last registration wins, so Program.cs's UseLogging still wraps it, as in AiLab.Rag.Tests' RagFactory
        builder.ConfigureTestServices(s => s.AddKeyedSingleton<IChatClient>(ChatClientKeys.Provider, chat));
    }
}

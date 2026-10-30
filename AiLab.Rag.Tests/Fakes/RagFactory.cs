using AiLab.Domain.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Rag.Tests.Fakes;

// The real Rag host in memory, with the in-memory vector store and the embedding provider replaced by the fake;
// for POST /ask also the chat provider, by a script.
// Development by default, so /dev/seed is mapped and ValidateOnBuild runs, like `dotnet run`.
public class RagFactory(HashedEmbeddingGenerator generator, string environment = "Development", Dictionary<string, string?>? settings = null,
    ScriptedChatClient? chat = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("VectorStore:Provider", "inmemory");
        foreach ((string key, string? value) in settings ?? [])
            builder.UseSetting(key, value);

        // The last registration wins, so the logging generator and chat client in Program.cs wrap the fakes
        builder.ConfigureTestServices(s =>
        {
            s.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider, generator);
            if (chat is not null)
                s.AddKeyedSingleton<IChatClient>(ChatClientKeys.Provider, chat);
        });
    }
}

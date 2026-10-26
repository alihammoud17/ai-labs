using AiLab.Application.Services;
using AiLab.Domain.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Application;

public static class DependencyInjection
{
    // Singletons, as before: none of them holds per-request state.
    // They need an IChatClient, which each host registers with its own middleware (AddChatClient in Program.cs).
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // POST /analyze-ticket and the ticket eval
        services.AddSingleton<TicketTools>();
        services.AddSingleton<ITicketAnalyzer, TicketAnalyzer>();
        services.AddSingleton<ITicketEval, TicketEval>();

        // The console: chat history, one session cost, /extract and /eval
        services.AddSingleton<SessionCostTracker>();
        services.AddSingleton<ChatConversation>();
        services.AddSingleton<JobExtractor>();
        services.AddSingleton<JobExtraction>();
        services.AddSingleton<JobEval>();

        // The console: /similarity. Needs an IEmbeddingGenerator, which only the console registers (AddEmbeddingGenerator in Program.cs).
        // A factory, not AddSingleton<SimilarityService>(): ASP.NET Core checks every type registration at startup in Development,
        // and the API has no IEmbeddingGenerator, so it wouldn't start. A factory is only checked when something asks for it.
        services.AddSingleton(sp => new SimilarityService(sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>()));
        return services;
    }
}

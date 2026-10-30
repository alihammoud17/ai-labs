using AiLab.Application.Chunking;
using AiLab.Application.Services;
using AiLab.Domain.Interfaces;
using AiLab.Domain.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // AiLab.Rag: /dev/seed and /search. Needs an ITextVectorStore (only Rag calls AddVectorStore) and an IEmbeddingGenerator,
        // so a factory, like SimilarityService: the API has neither, and a type registration would fail its startup check.
        services.AddSingleton<IVectorSearchService>(sp => new VectorSearchService(
            sp.GetRequiredService<ITextVectorStore>(),
            sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
            sp.GetRequiredService<ILogger<VectorSearchService>>(),
            // The Rag host registers VectorSearchSettings from Embedding:UseTaskPrefixes; without it, no prefixes
            sp.GetService<VectorSearchSettings>()?.UseTaskPrefixes ?? false));

        // AiLab.Rag: POST /ask. A factory for the same reason as VectorSearchService (the API has no vector store), and because
        // only the Rag host registers AskSettings, with Ask:PromptPath already made absolute.
        services.AddSingleton<IAskService>(sp => new AskService(
            sp.GetRequiredService<IVectorSearchService>(),
            sp.GetRequiredService<IChatClient>(),
            sp.GetRequiredService<IJobFileStore>(),
            sp.GetRequiredService<IProviderErrorClassifier>(),
            sp.GetRequiredService<AskSettings>(),
            sp.GetRequiredService<ILogger<AskService>>()));

        // AiLab.Rag: POST /dev/ingest picks one by Name. Plain code, no dependencies, default sizes (ChunkingOptions.Default).
        services.AddSingleton<IChunker>(_ => new FixedSizeChunker());
        services.AddSingleton<IChunker>(_ => new HeadingChunker());

        return services;
    }
}

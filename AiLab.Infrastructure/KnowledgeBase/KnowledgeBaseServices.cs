using AiLab.Domain.Interfaces;
using AiLab.Infrastructure.Providers;
using Amazon.BedrockAgentRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Infrastructure.KnowledgeBase;

public static class KnowledgeBaseServices
{
    // Separate from AddInfrastructure(), which has no configuration at registration time, like AddVectorStore(config).
    // Registers nothing unless KnowledgeBase:Id is set, so a host without a knowledge base is unchanged. Today only the
    // evals call it (KnowledgeBaseFixture); AiLab.Rag, the API and the console don't.
    // The client: Bedrock:Region / ServiceUrl / EmulatorCaPath, the same values the chat and embedding clients read, no
    // resilience values (the SDK's own timeout and retries). No credentials: the AWS chain finds them.
    public static IServiceCollection AddKnowledgeBase(this IServiceCollection services, IConfiguration configuration)
    {
        KnowledgeBaseSettings? settings = KnowledgeBaseSettings.From(configuration);
        if (settings is null)
            return services;

        var options = new BedrockClientOptions(
            Region: configuration["Bedrock:Region"],
            ServiceUrl: configuration["Bedrock:ServiceUrl"],
            EmulatorCaPath: configuration["Bedrock:EmulatorCaPath"]);

        services.AddSingleton(settings);
        services.AddSingleton<IAmazonBedrockAgentRuntime>(_ => BedrockAgentRuntimeClientFactory.Create(options));
        services.AddSingleton<IKnowledgeBaseRetriever, BedrockKnowledgeBaseRetriever>();

        // RetrieveAndGenerate needs a model: registered only with KnowledgeBase:ModelArn set
        if (settings.ModelArn is not null)
            services.AddSingleton<IKnowledgeBaseAsk, BedrockKnowledgeBaseAsk>();
        return services;
    }
}

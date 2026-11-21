using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Providers;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Api.Tests;

// Offline: creating an OllamaApiClient makes no call, and the metadata comes from its constructor arguments
public class EmbeddingGeneratorFactoryTests
{
    [Theory]
    [InlineData("nomic-embed-text")]
    [InlineData("all-minilm")]
    public void Ollama_config_gives_a_generator_for_that_model(string model)
    {
        IEmbeddingGenerator<string, Embedding<float>> generator =
            EmbeddingGeneratorFactory.Create("ollama", model, ollamaUrl: "http://localhost:11434");

        EmbeddingGeneratorMetadata? metadata = generator.GetService<EmbeddingGeneratorMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal(model, metadata.DefaultModelId);
        Assert.Equal(new Uri("http://localhost:11434"), metadata.ProviderUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_model_names_the_key_to_set(string? model)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => EmbeddingGeneratorFactory.Create("ollama", model));

        Assert.Contains("Embedding:Model", error.Message);
    }

    [Fact]
    public void Missing_provider_names_the_key_to_set()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => EmbeddingGeneratorFactory.Create(null, "nomic-embed-text"));

        Assert.Contains("Embedding:Provider", error.Message);
    }

    [Fact]
    public void Unknown_provider_is_refused()
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => EmbeddingGeneratorFactory.Create("openai", "text-embedding-3-small"));

        Assert.Contains("Embedding:Provider 'openai'", error.Message);
    }

    [Fact]
    public void Anthropic_is_refused_because_it_has_no_embeddings_api()
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => EmbeddingGeneratorFactory.Create("anthropic", "claude-haiku-5-5"));

        Assert.Contains("no embeddings API", error.Message);
    }

    [Fact]
    public void AddInfrastructure_builds_without_an_Embedding_section_and_fails_only_on_resolve()
    {
        using ServiceProvider services = BuildServices(new Dictionary<string, string?>());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => services.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider));
        Assert.Contains("Embedding:Provider", error.Message);
    }

    [Fact]
    public void AddInfrastructure_resolves_the_keyed_generator_from_config()
    {
        using ServiceProvider services = BuildServices(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "ollama",
            ["Embedding:Model"] = "nomic-embed-text",
        });

        IEmbeddingGenerator<string, Embedding<float>> generator =
            services.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider);

        Assert.Equal("nomic-embed-text", generator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId);
    }

    // Week 7 (Block 5): the bedrock case. Creating the AWS client makes no call and resolves no credentials, so these stay offline.
    [Fact]
    public void Bedrock_without_dimensions_names_the_key()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => EmbeddingGeneratorFactory.Create("bedrock", "amazon.titan-embed-text-v2:0"));

        Assert.Contains("Embedding:Dimensions", error.Message);
        Assert.Contains("amazon.titan-embed-text-v2:0", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Bedrock_with_a_size_that_is_not_positive_is_refused(int dimensions)
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => EmbeddingGeneratorFactory.Create("bedrock", "amazon.titan-embed-text-v2:0", dimensions: dimensions));

        Assert.Contains("Embedding:Dimensions must be positive", error.Message);
    }

    [Fact]
    public void Bedrock_config_gives_a_generator_for_that_model_and_size()
    {
        IEmbeddingGenerator<string, Embedding<float>> generator =
            EmbeddingGeneratorFactory.Create("bedrock", "amazon.titan-embed-text-v2:0", dimensions: 1024);

        EmbeddingGeneratorMetadata? metadata = generator.GetService<EmbeddingGeneratorMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("aws.bedrock", metadata.ProviderName);
        Assert.Equal("amazon.titan-embed-text-v2:0", metadata.DefaultModelId);
        Assert.Equal(1024, metadata.DefaultModelDimensions);
    }

    // R1: the embedding client is built by the same code as the chat client, so the emulator rules hold for it too
    [Fact]
    public void Bedrock_options_reach_the_sdk_client()
    {
        IEmbeddingGenerator<string, Embedding<float>> generator = EmbeddingGeneratorFactory.Create("bedrock", "amazon.titan-embed-text-v2:0",
            dimensions: 1024, bedrock: new(Region: "us-west-2", ServiceUrl: "https://127.0.0.1:9297", AttemptTimeout: TimeSpan.FromSeconds(30), MaxRetries: 1));

        IClientConfig config = generator.GetService<IAmazonBedrockRuntime>()!.Config;
        Assert.StartsWith("https://127.0.0.1:9297", config.ServiceURL);
        Assert.Null(config.RegionEndpoint);
        Assert.Equal("us-west-2", config.AuthenticationRegion);
        Assert.Equal(TimeSpan.FromSeconds(30), config.Timeout);
        Assert.Equal(1, config.MaxErrorRetry);
    }

    [Fact]
    public void Bedrock_emulator_ca_without_a_service_url_is_refused_for_embeddings_too()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => EmbeddingGeneratorFactory.Create("bedrock",
            "amazon.titan-embed-text-v2:0", dimensions: 1024, bedrock: new(EmulatorCaPath: "/nowhere/ca.pem")));

        Assert.Contains("Bedrock:EmulatorCaPath", error.Message);
    }

    // Ollama's model decides the size, so Embedding:Dimensions is ignored there (the store checks it instead)
    [Fact]
    public void Ollama_ignores_dimensions()
    {
        IEmbeddingGenerator<string, Embedding<float>> generator =
            EmbeddingGeneratorFactory.Create("ollama", "nomic-embed-text", ollamaUrl: "http://localhost:11434", dimensions: 1024);

        Assert.Equal("nomic-embed-text", generator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void Unknown_provider_message_lists_both_providers()
    {
        NotSupportedException error = Assert.Throws<NotSupportedException>(
            () => EmbeddingGeneratorFactory.Create("openai", "text-embedding-3-small"));

        Assert.Contains("\"ollama\" or \"bedrock\"", error.Message);
    }

    // 2.3: AddInfrastructure passes Embedding:Dimensions and the shared Bedrock:* section to the factory
    [Fact]
    public void AddInfrastructure_resolves_a_bedrock_generator_from_config()
    {
        using ServiceProvider services = BuildServices(new Dictionary<string, string?>
        {
            ["Embedding:Provider"] = "bedrock",
            ["Embedding:Model"] = "amazon.titan-embed-text-v2:0",
            ["Embedding:Dimensions"] = "1024",
            ["Bedrock:Region"] = "us-east-1",
            ["Bedrock:ServiceUrl"] = "https://127.0.0.1:9297",
        });

        IEmbeddingGenerator<string, Embedding<float>> generator =
            services.GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>(EmbeddingGeneratorKeys.Provider);

        Assert.Equal(1024, generator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelDimensions);
        Assert.StartsWith("https://127.0.0.1:9297", generator.GetService<IAmazonBedrockRuntime>()!.Config.ServiceURL);
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton(config)
            .AddInfrastructure()
            .BuildServiceProvider();
    }
}

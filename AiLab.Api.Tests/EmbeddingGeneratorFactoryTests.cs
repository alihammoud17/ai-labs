using AiLab.Domain.Constants;
using AiLab.Infrastructure;
using AiLab.Infrastructure.Providers;
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

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton(config)
            .AddInfrastructure()
            .BuildServiceProvider();
    }
}

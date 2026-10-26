using AiLab.Api.Tests.Fakes;
using AiLab.Application;
using AiLab.Application.Services;
using AiLab.Domain.Outputs;
using AiLab.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiLab.Api.Tests;

// Offline: a fake generator returns fixed vectors, so the scores are known in advance
public class SimilarityServiceTests
{
    [Fact]
    public async Task Diagonal_is_one_and_the_grid_is_symmetric()
    {
        SimilarityMatrix matrix = await Compute([[1, 2, 3], [3, 1, 0], [-1, 0, 2]]);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(1f, matrix.Scores[i, i], 5);
            for (int j = 0; j < 3; j++)
                Assert.Equal(matrix.Scores[i, j], matrix.Scores[j, i]);
        }
    }

    [Fact]
    public async Task Known_vectors_give_known_scores()
    {
        // [1,0] vs [0,1]: at right angles → 0; [1,0] vs [1,0]: same direction → 1; [1,1] vs [1,0]: 45° → cos 45° = √2/2
        SimilarityMatrix matrix = await Compute([[1, 0], [0, 1], [1, 0], [1, 1]]);

        Assert.Equal(0f, matrix.Scores[0, 1], 5);
        Assert.Equal(1f, matrix.Scores[0, 2], 5);
        Assert.Equal(0.7071f, matrix.Scores[3, 0], 4);
    }

    [Fact]
    public async Task Dimensions_is_the_vector_length_not_the_sentence_count()
    {
        SimilarityMatrix matrix = await Compute([[1, 0, 0, 0, 0], [0, 1, 0, 0, 0]]);

        Assert.Equal(5, matrix.Dimensions);
        Assert.Equal(2, matrix.Sentences.Count);
    }

    [Fact]
    public async Task InputTokens_comes_from_usage()
    {
        SimilarityMatrix matrix = await Compute([[1, 0], [0, 1]], new UsageDetails { InputTokenCount = 42 });

        Assert.Equal(42, matrix.InputTokens);
    }

    [Fact]
    public async Task InputTokens_is_null_without_usage()
    {
        SimilarityMatrix matrix = await Compute([[1, 0], [0, 1]], usage: null);

        Assert.Null(matrix.InputTokens);
    }

    [Fact]
    public async Task Empty_list_is_refused()
    {
        SimilarityService service = new(new FixedEmbeddingGenerator([]));

        await Assert.ThrowsAsync<ArgumentException>(() => service.ComputeSimilarityMatrixAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Wrong_embedding_count_is_refused()
    {
        SimilarityService service = new(new FixedEmbeddingGenerator([[1, 0]]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ComputeSimilarityMatrixAsync(["one", "two"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Mixed_vector_lengths_are_refused()
    {
        SimilarityService service = new(new FixedEmbeddingGenerator([[1, 0], [1, 0, 0]]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ComputeSimilarityMatrixAsync(["one", "two"], TestContext.Current.CancellationToken));
    }

    // The API registers AddApplication() but no IEmbeddingGenerator. In Development the host checks every registration at
    // startup (ValidateOnBuild); ApiFactory runs as "Testing", which skips that, so this test does the check itself.
    [Fact]
    public void AddApplication_passes_startup_validation_without_an_embedding_generator()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddSingleton(config).AddLogging().AddInfrastructure().AddApplication();
        services.AddSingleton<IChatClient>(new ScriptedChatClient());

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    // One sentence per vector, so the counts always match
    private static Task<SimilarityMatrix> Compute(float[][] vectors, UsageDetails? usage = null)
    {
        SimilarityService service = new(new FixedEmbeddingGenerator(vectors, usage));
        string[] sentences = vectors.Select((_, i) => $"sentence {i + 1}").ToArray();
        return service.ComputeSimilarityMatrixAsync(sentences, TestContext.Current.CancellationToken);
    }
}

using AiLab.Domain.Interfaces;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Npgsql;
using Pgvector.Npgsql;
// The namespace AiLab.Infrastructure.VectorStore hides the VectorData type of the same name
using VectorStoreBase = Microsoft.Extensions.VectorData.VectorStore;

namespace AiLab.Infrastructure.VectorStore;

public static class VectorStoreServices
{
    // Separate from AddInfrastructure(): only AiLab.Rag calls it, so the API and the console never need a database.
    // VectorStore:Provider is read here, because it decides what to register. The connection string is read on first resolve,
    // like the chat and embedding settings, so a host that never asks for the store needs no database.
    // The vector size (Embedding:Dimensions, default 768) is read here too, so a bad value stops the host at startup.
    public static IServiceCollection AddVectorStore(this IServiceCollection services, IConfiguration configuration)
    {
        string? provider = configuration["VectorStore:Provider"];
        VectorStoreSettings settings = Settings(configuration);
        services.AddSingleton(settings);

        switch (provider)
        {
            case "inmemory":
                services.AddSingleton<VectorStoreBase>(_ => new InMemoryVectorStore());
                services.AddSingleton<IKeywordSearch>(sp => new InMemoryKeywordSearch(sp.GetRequiredService<VectorStoreCollection<string, TextChunkRecord>>(), settings));
                break;

            case "postgres":
                // The connector builds the NpgsqlDataSource itself and applies UseVector(), so pgvector's type is mapped
                services.AddPostgresVectorStore(sp => ConnectionString(sp.GetRequiredService<IConfiguration>()), lifetime: ServiceLifetime.Singleton);

                // Keyword search runs its own SQL, so it has its own data source, with pgvector's type mapped too
                services.AddSingleton<IKeywordSearch>(sp =>
                {
                    var builder = new NpgsqlDataSourceBuilder(ConnectionString(sp.GetRequiredService<IConfiguration>()));
                    builder.UseVector();
                    return new PostgresKeywordSearch(builder.Build(), settings, sp.GetRequiredService<ILogger<PostgresKeywordSearch>>());
                });
                break;

            case null or "":
                throw new InvalidOperationException("VectorStore:Provider is missing. Set it in appsettings.json to \"inmemory\" or \"postgres\".");

            default:
                throw new NotSupportedException($"VectorStore:Provider '{provider}' is not supported. Set it to \"inmemory\" or \"postgres\".");
        }

        // Factories, so nothing is created (and no connection string read) until something asks for the store.
        // The definition sets the vector size from config; everything else comes from TextChunkRecord's attributes.
        services.AddSingleton(sp => sp.GetRequiredService<VectorStoreBase>()
            .GetCollection<string, TextChunkRecord>(settings.Collection, TextChunkRecord.Definition(settings.Dimensions)));
        services.AddSingleton<ITextVectorStore, TextVectorStore>();
        return services;
    }

    private static VectorStoreSettings Settings(IConfiguration configuration)
    {
        string collection = configuration["VectorStore:Collection"] is { Length: > 0 } name ? name : VectorStoreSettings.DefaultCollection;
        int dimensions = configuration.GetValue<int?>("Embedding:Dimensions") ?? TextChunkRecord.DefaultDimensions;
        if (dimensions <= 0)
            throw new InvalidOperationException($"Embedding:Dimensions must be positive, but is {dimensions}.");
        return new VectorStoreSettings(collection, dimensions);
    }

    private static string ConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("Vectors") is { Length: > 0 } connectionString
            ? connectionString
            : throw new InvalidOperationException(
                "ConnectionStrings:Vectors is missing. Set it with: dotnet user-secrets set \"ConnectionStrings:Vectors\" " +
                "\"Host=localhost;Port=5433;Database=ailab;Username=ailab;Password=ailab\" --project AiLab.Rag");
}

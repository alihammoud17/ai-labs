using AiLab.Domain.Interfaces;
using CommunityToolkit.VectorData.InMemory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
// The namespace AiLab.Infrastructure.VectorStore hides the VectorData type of the same name
using VectorStoreBase = Microsoft.Extensions.VectorData.VectorStore;

namespace AiLab.Infrastructure.VectorStore;

public static class VectorStoreServices
{
    // Separate from AddInfrastructure(): only AiLab.Rag calls it, so the API and the console never need a database.
    // VectorStore:Provider is read here, because it decides what to register. The connection string is read on first resolve,
    // like the chat and embedding settings, so a host that never asks for the store needs no database.
    public static IServiceCollection AddVectorStore(this IServiceCollection services, IConfiguration configuration)
    {
        string? provider = configuration["VectorStore:Provider"];
        string collection = configuration["VectorStore:Collection"] is { Length: > 0 } name ? name : "chunks";

        switch (provider)
        {
            case "inmemory":
                services.AddSingleton<VectorStoreBase>(_ => new InMemoryVectorStore());
                break;

            case "postgres":
                // The connector builds the NpgsqlDataSource itself and applies UseVector(), so pgvector's type is mapped
                services.AddPostgresVectorStore(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Vectors") is { Length: > 0 } connectionString
                    ? connectionString
                    : throw new InvalidOperationException(
                        "ConnectionStrings:Vectors is missing. Set it with: dotnet user-secrets set \"ConnectionStrings:Vectors\" " +
                        "\"Host=localhost;Port=5433;Database=ailab;Username=ailab;Password=ailab\" --project AiLab.Rag"),
                    lifetime: ServiceLifetime.Singleton);
                break;

            case null or "":
                throw new InvalidOperationException("VectorStore:Provider is missing. Set it in appsettings.json to \"inmemory\" or \"postgres\".");

            default:
                throw new NotSupportedException($"VectorStore:Provider '{provider}' is not supported. Set it to \"inmemory\" or \"postgres\".");
        }

        // Factories, so nothing is created (and no connection string read) until something asks for the store
        services.AddSingleton(sp => sp.GetRequiredService<VectorStoreBase>().GetCollection<string, TextChunkRecord>(collection));
        services.AddSingleton<ITextVectorStore, TextVectorStore>();
        return services;
    }
}

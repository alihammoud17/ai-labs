namespace AiLab.Rag;

// ASPNETCORE_ENVIRONMENT values this host knows besides the framework's own (Development, Production).
// Bedrock: appsettings.Bedrock.json on top of appsettings.json (chat + embeddings on Bedrock, their own collection);
// the launch profile "bedrock" sets it. It's a developer environment like Development: user-secrets and /dev/* too.
public static class RagEnvironments
{
    public const string Bedrock = "Bedrock";

    public static bool IsDeveloperEnvironment(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(Bedrock);
}

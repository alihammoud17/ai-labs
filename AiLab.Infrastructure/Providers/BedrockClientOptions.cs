namespace AiLab.Infrastructure.Providers;

// Everything needed to build a Bedrock client (runtime or Knowledge Base agent runtime), filled once from Bedrock:* (and, in the API, the resilience
// settings). Shared by the chat and the embedding factory, so both talk to the same endpoint with the same TLS and timeouts.
// Every value is optional: new() means real AWS in us-east-1 with the SDK's own timeout and retries.
// No credentials: the AWS SDK's credential chain finds them (emulator: ai-lab-kb.env; real account: AWS_PROFILE=ai-lab-dev).
public sealed record BedrockClientOptions(
    string? Region = null,          // Bedrock:Region, default us-east-1
    string? ServiceUrl = null,      // Bedrock:ServiceUrl, emulator only
    string? EmulatorCaPath = null,  // Bedrock:EmulatorCaPath, emulator only, refused without ServiceUrl
    TimeSpan? AttemptTimeout = null, // null keeps the SDK default
    int? MaxRetries = null);         // null keeps the SDK default

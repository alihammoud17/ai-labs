using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AiLab.Infrastructure.Providers;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Microsoft.Extensions.AI;

namespace AiLab.Api.Tests;

// Offline: creating the AWS SDK client makes no call and resolves no credentials (they're looked up on the first request),
// so these run without the emulator, an AWS account or any AWS_* variable. The SDK client's Config is read back through
// GetService<IAmazonBedrockRuntime>(), which the Bedrock IChatClient hands out.
public sealed class ChatClientFactoryTests : IDisposable
{
    private const string Emulator = "https://127.0.0.1:9297";
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("ailab-ca-");

    public void Dispose() => _temp.Delete(recursive: true);

    [Theory]
    [InlineData("us.amazon.nova-lite-v1:0")]
    [InlineData("amazon.nova-lite-v1:0")]
    [InlineData("us.anthropic.claude-haiku-5-5")]
    public void Bedrock_gives_a_client_for_that_model(string model)
    {
        IChatClient client = ChatClientFactory.Create("bedrock", model);

        ChatClientMetadata? metadata = client.GetService<ChatClientMetadata>();
        Assert.NotNull(metadata);
        Assert.Equal("aws.bedrock", metadata.ProviderName);
        Assert.Equal(model, metadata.DefaultModelId); // what SessionCostTracker prices by
    }

    [Fact]
    public void Without_a_service_url_it_uses_the_region_endpoint()
    {
        IClientConfig config = BedrockConfig(ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0", bedrock: new(Region: "us-west-2")));

        Assert.Equal(RegionEndpoint.USWest2, config.RegionEndpoint);

        // The SDK copies AWS_ENDPOINT_URL into ServiceURL when the client is built (found 2026-11-20 by running this test
        // in a shell that had sourced ai-lab-kb.env). So a terminal set up for the emulator always reaches the emulator,
        // even without Bedrock:ServiceUrl: the safe direction. Asserted only where that variable isn't set.
        if (Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL") is null)
            Assert.Null(config.ServiceURL);
        else
            Assert.StartsWith(Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL")!, config.ServiceURL);
    }

    [Fact]
    public void Region_defaults_to_us_east_1()
    {
        IClientConfig config = BedrockConfig(ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0"));

        Assert.Equal(RegionEndpoint.USEast1, config.RegionEndpoint);
    }

    // The R4 trap: in the AWS SDK, setting RegionEndpoint clears ServiceURL (and the other way round)
    [Fact]
    public void With_a_service_url_it_keeps_the_url_and_signs_for_the_region()
    {
        IClientConfig config = BedrockConfig(ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0",
            bedrock: new(Region: "us-east-1", ServiceUrl: Emulator)));

        Assert.StartsWith(Emulator, config.ServiceURL);
        Assert.Null(config.RegionEndpoint);
        Assert.Equal("us-east-1", config.AuthenticationRegion);
    }

    [Fact]
    public void Attempt_timeout_and_retries_reach_the_sdk_config()
    {
        IClientConfig config = BedrockConfig(ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0",
            bedrock: new(AttemptTimeout: TimeSpan.FromSeconds(60), MaxRetries: 2)));

        Assert.Equal(TimeSpan.FromSeconds(60), config.Timeout);
        Assert.Equal(2, config.MaxErrorRetry);
    }

    // The console passes nothing: the SDK's own defaults stay, whatever they are in this SDK version
    [Fact]
    public void No_timeout_or_retries_keeps_the_sdk_defaults()
    {
        var defaults = new AmazonBedrockRuntimeConfig();
        IClientConfig config = BedrockConfig(ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0"));

        Assert.Equal(defaults.Timeout, config.Timeout);
        Assert.Equal(defaults.MaxErrorRetry, config.MaxErrorRetry);
        Assert.Null(config.HttpClientFactory); // no emulator CA: the system trust store
    }

    [Fact]
    public void Emulator_ca_path_sets_an_http_client_factory_that_copies_the_timeout()
    {
        string caPath = WriteCaPem();

        IClientConfig config = BedrockConfig(ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0",
            bedrock: new(ServiceUrl: Emulator, AttemptTimeout: TimeSpan.FromSeconds(42), EmulatorCaPath: caPath)));

        Assert.NotNull(config.HttpClientFactory);
        // With a custom factory the SDK doesn't apply its Timeout itself (measured in Block 3), so the factory must
        using HttpClient http = config.HttpClientFactory.CreateHttpClient(config);
        Assert.Equal(TimeSpan.FromSeconds(42), http.Timeout);
    }

    [Fact]
    public void Emulator_ca_path_without_a_service_url_is_refused()
    {
        string caPath = WriteCaPem();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0", bedrock: new(EmulatorCaPath: caPath)));

        Assert.Contains("Bedrock:EmulatorCaPath", error.Message);
        Assert.Contains("Bedrock:ServiceUrl", error.Message);
    }

    [Fact]
    public void Missing_ca_file_names_the_key_to_set()
    {
        string missing = Path.Combine(_temp.FullName, "nope.pem");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0", bedrock: new(ServiceUrl: Emulator, EmulatorCaPath: missing)));

        Assert.Contains("Bedrock:EmulatorCaPath", error.Message);
    }

    [Fact]
    public void Ca_file_without_a_certificate_is_refused()
    {
        string empty = Path.Combine(_temp.FullName, "empty.pem");
        File.WriteAllText(empty, "not a certificate");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ChatClientFactory.Create("bedrock", "us.amazon.nova-lite-v1:0", bedrock: new(ServiceUrl: Emulator, EmulatorCaPath: empty)));

        Assert.Contains("No certificate", error.Message);
    }

    [Fact]
    public void Unknown_provider_is_refused()
    {
        Assert.Throws<NotSupportedException>(() => ChatClientFactory.Create("openai", "gpt-x"));
    }

    private static IClientConfig BedrockConfig(IChatClient client)
    {
        IAmazonBedrockRuntime? runtime = client.GetService<IAmazonBedrockRuntime>();
        Assert.NotNull(runtime);
        return runtime.Config;
    }

    // A throwaway self-signed CA in PEM, like ~/.aws/emulator/<port>/ca.pem (certificate block only, no key)
    private string WriteCaPem()
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest("CN=ailab-test-ca", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, false, 0, critical: true));
        using X509Certificate2 ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        string path = Path.Combine(_temp.FullName, "ca.pem");
        File.WriteAllText(path, ca.ExportCertificatePem());
        return path;
    }
}

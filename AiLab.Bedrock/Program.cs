using System.Security.Cryptography.X509Certificates;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

var kbId     = Environment.GetEnvironmentVariable("KB_ID") ?? throw new("KB_ID not set");
var modelArn = Environment.GetEnvironmentVariable("GEN_MODEL_ARN") ?? throw new("GEN_MODEL_ARN not set");
var port     = Environment.GetEnvironmentVariable("EMULATOR_PORT") ?? "9297";
var question = args.Length > 0 ? string.Join(' ', args) : "What is this documentation about?";

var config = new AmazonBedrockAgentRuntimeConfig
{
    // The emulator serves HTTPS only, and its certificate is issued for 127.0.0.1 (not localhost).
    ServiceURL = $"https://127.0.0.1:{port}",
    AuthenticationRegion = "us-east-1",
    HttpClientFactory = new EmulatorHttpClientFactory(port)
};

using var client = new AmazonBedrockAgentRuntimeClient(config);

Console.WriteLine($"Q: {question}\n");

// 1) Retrieve: only what the Knowledge Base found (chunks + relevance scores, higher is better).
var retrieved = await client.RetrieveAsync(new RetrieveRequest
{
    KnowledgeBaseId = kbId,
    RetrievalQuery = new KnowledgeBaseQuery { Text = question },
    RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
    {
        VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration { NumberOfResults = 3 }
    }
});

Console.WriteLine("── Retrieve ──");
foreach (var r in retrieved.RetrievalResults ?? [])
    Console.WriteLine($"  [{r.Score:F3}] {r.Location?.S3Location?.Uri}");

// 2) RetrieveAndGenerate: retrieval + the model's answer + citations in one call.
var res = await client.RetrieveAndGenerateAsync(new RetrieveAndGenerateRequest
{
    Input = new RetrieveAndGenerateInput { Text = question },
    RetrieveAndGenerateConfiguration = new RetrieveAndGenerateConfiguration
    {
        Type = RetrieveAndGenerateType.KNOWLEDGE_BASE,
        KnowledgeBaseConfiguration = new KnowledgeBaseRetrieveAndGenerateConfiguration
        {
            KnowledgeBaseId = kbId,
            ModelArn = modelArn
        }
    }
});

Console.WriteLine($"\n── RetrieveAndGenerate (session {res.SessionId}) ──\n{res.Output?.Text}\n");
Console.WriteLine("── Citations ──");
foreach (var c in res.Citations ?? [])
    foreach (var r in c.RetrievedReferences ?? [])
    {
        var text = r.Content?.Text ?? "";
        Console.WriteLine($"  ↳ {r.Location?.S3Location?.Uri}: {text[..Math.Min(100, text.Length)].ReplaceLineEndings(" ")}…");
    }

sealed class EmulatorHttpClientFactory(string port) : Amazon.Runtime.HttpClientFactory
{
    public override HttpClient CreateHttpClient(Amazon.Runtime.IClientConfig clientConfig)
    {
        var caPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                  ".aws", "emulator", port, "ca.pem");
        var trusted = LoadPemCertificates(caPath);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                if (cert is null || chain is null) return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(trusted);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build(cert);
            }
        };
        return new HttpClient(handler);
    }

    // Certificate blocks only: CreateFromPemFile would also demand a private key, which a CA bundle doesn't have.
    static X509Certificate2Collection LoadPemCertificates(string path)
    {
        var certs = new X509Certificate2Collection();
        var rest = File.ReadAllText(path);
        while (System.Security.Cryptography.PemEncoding.TryFind(rest, out var pem))
        {
            if (rest[pem.Label] == "CERTIFICATE")
                certs.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(rest[pem.Base64Data])));
            rest = rest[pem.Location.End..];
        }
        if (certs.Count == 0) throw new InvalidOperationException($"No certificate found in {path}");
        return certs;
    }
}

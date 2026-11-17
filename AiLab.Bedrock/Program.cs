using System.Security.Cryptography.X509Certificates;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;
using Amazon.Runtime;

var kbId     = Environment.GetEnvironmentVariable("KB_ID") ?? throw new("KB_ID not set");
var modelArn = Environment.GetEnvironmentVariable("GEN_MODEL_ARN") ?? throw new("GEN_MODEL_ARN not set");
var port     = Environment.GetEnvironmentVariable("EMULATOR_PORT") ?? "9297";
// Usage: dotnet run --project AiLab.Bedrock -- [--no-followup] "question"
// --no-followup skips the session checks (3.3), for plain eval runs (3.4).
var noFollowUp = args.Contains("--no-followup");
var question = string.Join(' ', args.Where(a => a != "--no-followup")) is { Length: > 0 } q
    ? q
    : "What is this documentation about?";
const string FollowUp = "And how is it versioned?";

var config = new AmazonBedrockAgentRuntimeConfig
{
    // The emulator serves HTTPS only, and its certificate is issued for 127.0.0.1 (not localhost).
    ServiceURL = $"https://127.0.0.1:{port}",
    AuthenticationRegion = "us-east-1",
    HttpClientFactory = new EmulatorHttpClientFactory(port)
};

using var client = new AmazonBedrockAgentRuntimeClient(config);

Console.WriteLine($"Q: {question}\n");

// 1) Retrieve: only what the Knowledge Base found (passages + relevance scores, higher is better).
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
    Console.WriteLine($"  [{r.Score:F3}] {r.Location?.S3Location?.Uri} ({r.Content?.Text?.Length ?? 0} chars)");

// 2) RetrieveAndGenerate: retrieval + the model's answer + citations in one call.
var first = await AskAsync(question);
PrintAnswer("RetrieveAndGenerate", first);

if (noFollowUp) return;

// 3) Follow-up turn (3.3): send the service's SessionId back so this turn continues the conversation.
var second = await AskAsync(FollowUp, first.SessionId);
PrintAnswer($"Follow-up: {FollowUp}", second);
Console.WriteLine(second.SessionId == first.SessionId
    ? $"Same session: yes ({second.SessionId})"
    : $"Same session: NO (first {first.SessionId}, second {second.SessionId})");

// 4) An invented session ID: the service creates session IDs, so it should refuse one it never issued.
var invented = Guid.NewGuid().ToString();
try
{
    var res = await AskAsync(FollowUp, invented);
    Console.WriteLine($"\nInvented session {invented}: accepted, session {res.SessionId} (expected an error)");
}
catch (AmazonServiceException ex)
{
    Console.WriteLine($"\nInvented session {invented}: {ex.GetType().Name} (code {ex.ErrorCode}, HTTP {(int)ex.StatusCode})");
    Console.WriteLine($"  {ex.Message}");
}

Task<RetrieveAndGenerateResponse> AskAsync(string text, string? sessionId = null) =>
    client.RetrieveAndGenerateAsync(new RetrieveAndGenerateRequest
    {
        Input = new RetrieveAndGenerateInput { Text = text },
        SessionId = sessionId, // null = start a new session
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

static void PrintAnswer(string title, RetrieveAndGenerateResponse res)
{
    Console.WriteLine($"\n── {title} (session {res.SessionId}) ──\n{res.Output?.Text}\n");
    Console.WriteLine("── Citations ──");
    foreach (var c in res.Citations ?? [])
    {
        // Bedrock cites a span of the answer text, then the passages that support it.
        var span = c.GeneratedResponsePart?.TextResponsePart?.Span;
        Console.WriteLine($"  answer chars {span?.Start}–{span?.End}:");
        foreach (var r in c.RetrievedReferences ?? [])
        {
            var text = r.Content?.Text ?? "";
            var tail = text[Math.Max(0, text.Length - 60)..].ReplaceLineEndings(" ");
            Console.WriteLine($"    ↳ {r.Location?.S3Location?.Uri} ({text.Length} chars, ends: …{tail})");
        }
    }
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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Amazon.Runtime;

namespace AiLab.Infrastructure.Providers;

// HttpClients for the AWS SDK that trust exactly one CA: aws-emulator's self-signed one (~/.aws/emulator/<port>/ca.pem).
// Used only when Bedrock:EmulatorCaPath is set, so the real-account path keeps the system trust store.
// Not "accept any certificate": the chain must build to this CA. macOS .NET ignores SSL_CERT_FILE, hence the code.
// Duplicate of the EmulatorHttpClientFactory at the bottom of AiLab.Bedrock/Program.cs (which takes the port, not the path):
// AiLab.Bedrock references no project, and Infrastructure would pull in every provider SDK for these 40 lines.
// BedrockClientConfig applies it to both the runtime client and the Knowledge Base client (week 8 Tuesday); AiLab.Bedrock could too, or be retired.
internal sealed class EmulatorHttpClientFactory : HttpClientFactory
{
    private readonly X509Certificate2Collection _trusted;

    public EmulatorHttpClientFactory(string caPath)
    {
        if (!File.Exists(caPath))
            throw new InvalidOperationException(
                $"Bedrock:EmulatorCaPath points to '{caPath}', which doesn't exist. Is aws-emulator running? (README: aws-emulator: start, check, stop)");
        _trusted = LoadPemCertificates(caPath);
    }

    public override HttpClient CreateHttpClient(IClientConfig clientConfig)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, chain, _) =>
            {
                if (cert is null || chain is null)
                    return false;
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(_trusted);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // a local CA has no revocation list
                return chain.Build(cert);
            },
        };
        var client = new HttpClient(handler);

        // With a custom factory the SDK doesn't apply its Timeout itself (checked 2026-11-20: a 1 ms Timeout was ignored
        // until copied here), so the API's attempt timeout would silently not apply on this path.
        if (clientConfig.Timeout is TimeSpan timeout)
            client.Timeout = timeout;

        return client;
    }

    // Certificate blocks only: X509Certificate2.CreateFromPemFile would also demand a private key, which a CA bundle doesn't have
    private static X509Certificate2Collection LoadPemCertificates(string path)
    {
        var certificates = new X509Certificate2Collection();
        ReadOnlySpan<char> rest = File.ReadAllText(path);
        while (PemEncoding.TryFind(rest, out PemFields pem))
        {
            if (rest[pem.Label].SequenceEqual("CERTIFICATE"))
                certificates.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(rest[pem.Base64Data].ToString())));
            rest = rest[pem.Location.End..];
        }

        if (certificates.Count == 0)
            throw new InvalidOperationException($"No certificate found in '{path}' (Bedrock:EmulatorCaPath).");
        return certificates;
    }
}

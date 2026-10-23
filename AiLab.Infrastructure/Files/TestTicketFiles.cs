using AiLab.Domain.Interfaces;
using System.Text.Json;

namespace AiLab.Infrastructure.Files;

// The test tickets A–D, one JSON request body per file in TestTickets/ (copied next to the app).
// Read by the Swagger examples and the ticket eval; analyze-ticket.http points at the same files.
public class TestTicketFiles : ITestTicketStore
{
    public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestTickets");

    // "ticket-a.json" → "A", in file-name order
    public IReadOnlyList<(string Letter, string Json)> ReadAll() =>
        Directory.GetFiles(Folder, "ticket-*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileNameWithoutExtension(path)["ticket-".Length..].ToUpperInvariant(), File.ReadAllText(path)))
            .ToList();

    // expected.json: ticket letter → the version that explains it
    public Dictionary<string, string?> ReadExpected() =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(Path.Combine(Folder, "expected.json")))!;
}

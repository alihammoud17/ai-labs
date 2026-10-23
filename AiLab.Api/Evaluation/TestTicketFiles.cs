namespace AiLab.Api.Evaluation;

// The test tickets A–D, one JSON request body per file in TestTickets/ (copied next to the app).
// Read by the Swagger examples and the ticket eval; analyze-ticket.http points at the same files.
public static class TestTicketFiles
{
    public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "TestTickets");

    // "ticket-a.json" → "A", in file-name order
    public static IReadOnlyList<(string Letter, string Json)> ReadAll() =>
        Directory.GetFiles(Folder, "ticket-*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileNameWithoutExtension(path)["ticket-".Length..].ToUpperInvariant(), File.ReadAllText(path)))
            .ToList();
}

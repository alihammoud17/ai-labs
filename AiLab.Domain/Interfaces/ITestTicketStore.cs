namespace AiLab.Domain.Interfaces;

// The test tickets A–D and the version that explains each one
public interface ITestTicketStore
{
    // "ticket-a.json" → ("A", its JSON request body), in file-name order
    IReadOnlyList<(string Letter, string Json)> ReadAll();

    // Ticket letter → the version that explains it (null: no change explains it)
    Dictionary<string, string?> ReadExpected();
}

namespace AiLab.Domain.Interfaces;

// Runs every test ticket N times and scores each probable cause
public interface ITicketEval
{
    // The report as text; maxCost stops the eval once the summed cost passes it
    Task<string> RunAsync(int runs, decimal? maxCost, CancellationToken cancellationToken);
}

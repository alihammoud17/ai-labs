namespace AiLab.Console.Chat;

// Ctrl+C while a model call is running (a streamed reply, /extract, /eval) cancels just that call.
// Ctrl+C at the prompt still exits the app as usual.
public class TurnCancellation
{
    // Set only while a call is running
    private CancellationTokenSource? _cts;

    public bool IsActive => _cts is not null;

    public CancellationToken Begin()
    {
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    public void Cancel() => _cts?.Cancel();

    public void End()
    {
        _cts?.Dispose();
        _cts = null;
    }
}

using AiLab.Console.Extraction;
using AiLab.Console.Pricing;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace AiLab.Console.Chat;

// Inside AiLab.Console.*, a bare "Console" would mean the AiLab.Console namespace
using Console = System.Console;

// The chat loop. It only knows IChatClient, never which provider is behind it.
public class ChatSession
{
    // Crude guard against silently overflowing the model's context window.
    private const int MaxHistoryMessages = 20;

    // Format-forcing system message (step 4.4). Set to null for normal chat.
    // It's the first message in history, and /reset keeps it.
    private static readonly string? JsonSystemMessage = null; // """You are a JSON generator. You will always respond with valid JSON in the shape: {"answer": string, "confidence": number between 0 and 1}. No other text."""

    private readonly IChatClient _client;
    private readonly JobExtractor _extractor;

    // Provider and model as the client reports them, so the session doesn't need config to price a turn
    private readonly ChatClientMetadata? _metadata;

    // The conversation history. Created once, so it keeps growing with every turn.
    private readonly List<ChatMessage> _history = [];

    // Settings sent with every request. Change the values here between experiment runs (step 4);
    // an empty ChatOptions means "use the provider's defaults".
    private readonly ChatOptions _options = new()
    {
        // Temperature = 1.0f,
        // MaxOutputTokens = 10,
    };

    // Running cost of this session, across all turns
    private decimal _sessionTotal;

    // Set only while a reply is streaming, so Ctrl+C can cancel just that reply
    private CancellationTokenSource? _turnCts;

    public ChatSession(IChatClient client, JobExtractor extractor)
    {
        _client = client;
        _extractor = extractor;
        _metadata = client.GetService<ChatClientMetadata>();

        if (JsonSystemMessage is not null)
            _history.Add(new ChatMessage(ChatRole.System, JsonSystemMessage));
    }

    public async Task RunAsync()
    {
        // Ctrl+C while a reply is streaming cancels just that reply.
        // Ctrl+C at the prompt still exits the app as usual.
        Console.CancelKeyPress += (_, e) =>
        {
            if (_turnCts is null)
                return;

            e.Cancel = true;
            _turnCts.Cancel();
        };

        Console.WriteLine($"Chat started with {_metadata?.ProviderName ?? "unknown provider"}/{_metadata?.DefaultModelId ?? "unknown model"}. Type /extract [v1|v2] <path> to extract a job posting, /reset for a fresh conversation, /exit to quit. Ctrl+C stops a reply.");

        while (true)
        {
            Console.Write("\nYou: ");
            string? input = Console.ReadLine();

            // null means input has ended (Ctrl+D / Ctrl+Z or piped input ran out)
            if (input is null)
                break;

            input = input.Trim();

            if (input.Length == 0)
                continue;

            if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            // Start a fresh conversation; system messages stay so a format instruction survives (step 4.4).
            // The session cost keeps counting, since that money is already spent.
            if (input.Equals("/reset", StringComparison.OrdinalIgnoreCase))
            {
                _history.RemoveAll(m => m.Role != ChatRole.System);
                Console.WriteLine("[conversation reset]");
                continue;
            }

            // Typed extraction (deliverable 3); separate from the chat, so history is untouched
            if (input.Equals("/extract", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/extract ", StringComparison.OrdinalIgnoreCase))
            {
                await ExtractAsync(input["/extract".Length..].Trim());
                continue;
            }

            await RunTurnAsync(input);
        }
    }

    private async Task ExtractAsync(string arguments)
    {
        // "/extract v2 inputs/job-1.txt" picks the prompt; "/extract inputs/job-1.txt" means v1
        string[] parts = arguments.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string version = "v1";
        if (parts.Length == 2 && (parts[0].Equals("v1", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("v2", StringComparison.OrdinalIgnoreCase)))
        {
            version = parts[0].ToLowerInvariant();
            parts = parts[1..];
        }

        string path = parts.Length == 1 ? parts[0] : "";
        if (path.Length == 0 || path.Equals("v1", StringComparison.OrdinalIgnoreCase) || path.Equals("v2", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: /extract [v1|v2] <path>   e.g. /extract v2 inputs/job-1.txt");
            return;
        }

        // Prompts are read on every call, so they can be edited between runs without recompiling
        string promptPath = Path.Combine("prompts", $"extract-{version}.txt");
        if (!File.Exists(promptPath))
        {
            Console.WriteLine($"[prompt not found: {Path.GetFullPath(promptPath)}]");
            return;
        }

        // Relative paths start from where the app was started (the repo root for inputs/...)
        if (!File.Exists(path))
        {
            Console.WriteLine($"[file not found: {Path.GetFullPath(path)}]");
            return;
        }

        Console.WriteLine($"[extracting with prompt {version}...]");
        ChatResponse<JobPosting> response;
        _turnCts = new CancellationTokenSource();
        try
        {
            response = await _extractor.ExtractAsync(await File.ReadAllTextAsync(promptPath), await File.ReadAllTextAsync(path), _turnCts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[extraction cancelled]");
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[error: {ex.Message}]");
            return;
        }
        finally
        {
            _turnCts.Dispose();
            _turnCts = null;
        }

        // TryGetResult (inside JobParser) instead of .Result: a reply that isn't valid JSON for JobPosting prints a message instead of throwing
        if (JobParser.TryParse(response, out JobPosting? job, out bool strippedFence) && job is not null)
        {
            if (strippedFence)
                Console.WriteLine("[parsed after stripping a ```json fence around the reply]");
            Console.WriteLine($"Title:             {job.Title}");
            Console.WriteLine($"Company:           {job.Company ?? "null"}");
            Console.WriteLine($"Location:          {job.Location ?? "null"}");
            Console.WriteLine($"Work mode:         {job.WorkMode?.ToString() ?? "null"}");
            Console.WriteLine($"Required skills:   {string.Join(", ", job.RequiredSkills)} ({job.RequiredSkills.Count})");
            Console.WriteLine($"Years experience:  {job.YearsOfExperience?.ToString() ?? "null"}");
        }
        else
        {
            Console.WriteLine("❌ Could not parse the reply into a JobPosting. Raw reply:");
            Console.WriteLine(response.Text);
        }

        PrintUsage(response);
        if (response.FinishReason == ChatFinishReason.Length)
            Console.WriteLine("⚠ Answer was cut off (output token limit reached)");

        string expectedPath = JobScorer.ExpectedPathFor(path);
        if (job is null || !File.Exists(expectedPath))
            return;

        JobPosting? expected = JobScorer.LoadExpected(expectedPath, out string? error);
        if (expected is null)
        {
            Console.WriteLine($"[can't score: {expectedPath} doesn't fit JobPosting: {error}]");
            return;
        }

        Console.WriteLine($"Score vs {expectedPath}:");
        JobScorer.PrintScore(job, expected);
    }

    private async Task RunTurnAsync(string input)
    {
        // 1. Add your message to the history
        _history.Add(new ChatMessage(ChatRole.User, input));

        // 2. Send the WHOLE history, not just the last message
        // 3. Stream and print the answer, keeping every update (they carry usage and finish reason too)
        Console.Write("AI: ");
        List<ChatResponseUpdate> updates = [];
        _turnCts = new CancellationTokenSource();
        try
        {
            await foreach (ChatResponseUpdate update in _client.GetStreamingResponseAsync(_history, _options, _turnCts.Token))
            {
                Console.Write(update.Text);
                updates.Add(update);
            }
            Console.WriteLine();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[reply cancelled, turn discarded]");
            _history.RemoveAt(_history.Count - 1);
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[error: {ex.Message}; turn discarded]");
            _history.RemoveAt(_history.Count - 1);
            return;
        }
        finally
        {
            _turnCts.Dispose();
            _turnCts = null;
        }

        // Rebuild the full response from the updates
        ChatResponse response = updates.ToChatResponse();

        PrintUsage(response);

        // A cut-off answer looks like normal text that just ends; only the finish reason says it's incomplete.
        // On a thinking model (Haiku 5.5) the limit can be used up by thinking, leaving no text at all.
        if (response.FinishReason == ChatFinishReason.Length)
            Console.WriteLine("⚠ Answer was cut off (output token limit reached)");

        // An empty assistant message would break later turns (Anthropic rejects it), so drop the turn
        if (string.IsNullOrWhiteSpace(response.Text))
        {
            Console.WriteLine($"[empty reply (finish reason: {response.FinishReason}), turn discarded]");
            _history.RemoveAt(_history.Count - 1);
            return;
        }

        // Strict check on purpose: a reply wrapped in ```json fences counts as invalid, like it would for a real parser
        if (JsonSystemMessage is not null)
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(response.Text);
                Console.WriteLine("✅ valid JSON");
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"❌ invalid JSON: {ex.Message}");
            }
        }

        // 4. Save the AI's reply so it remembers it next turn
        _history.AddMessages(response);

        // Drop the oldest user/assistant pair so the conversation keeps starting with a user message.
        // System messages are never trimmed or counted, so a format instruction (step 4.4) stays in place.
        while (_history.Count(m => m.Role != ChatRole.System) > MaxHistoryMessages)
        {
            int oldest = _history.FindIndex(m => m.Role != ChatRole.System);
            _history.RemoveRange(oldest, 2);
        }
    }

    // Usage arrives inside the updates; it's billed even if the reply turns out empty
    private void PrintUsage(ChatResponse response)
    {
        if (response.Usage?.InputTokenCount is not long inputTokens || response.Usage.OutputTokenCount is not long outputTokens)
        {
            Console.WriteLine("Tokens: unknown");
            return;
        }

        // Price by the model the client was created with
        string? model = _metadata?.DefaultModelId;
        decimal? turnCost = model is null ? null : ModelPrices.Cost(model, inputTokens, outputTokens);
        if (turnCost is null)
        {
            Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Finish Reason: {response.FinishReason} | Turn: unknown price for '{model}'");
            return;
        }

        _sessionTotal += turnCost.Value;
        Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Finish Reason: {response.FinishReason} | Turn: ${turnCost:0.000000} | Session: ${_sessionTotal:0.000000}");
    }
}

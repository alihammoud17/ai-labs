using AiLab.Console.Extraction;
using AiLab.Core.Parsing;
using AiLab.Core.Pricing;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

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

        Console.WriteLine($"Chat started with {_metadata?.ProviderName ?? "unknown provider"}/{_metadata?.DefaultModelId ?? "unknown model"}. Type /extract [v1|v2] <path> to extract a job posting, /eval <v1|v2> [runs] to score a prompt on all inputs, /reset for a fresh conversation, /exit to quit. Ctrl+C stops a reply.");

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

            // Repeated scored runs over every input with a golden answer; also separate from the chat
            if (input.Equals("/eval", StringComparison.OrdinalIgnoreCase) || input.StartsWith("/eval ", StringComparison.OrdinalIgnoreCase))
            {
                await EvalAsync(input["/eval".Length..].Trim());
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

        // TryGetResult (inside StructuredReply) instead of .Result: a reply that isn't valid JSON for JobPosting prints a message instead of throwing
        if (StructuredReply.TryParse(response, out JobPosting? job, out bool strippedFence) && job is not null)
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

    // One extraction inside /eval. Score is null when the reply didn't parse; it then counts as 0 fields and 0 skills.
    private record EvalRun(string Input, int Run, JobScore? Score, int SkillsTotal, long? InputTokens, long? OutputTokens, decimal? Cost);

    // "/eval v2 3": every inputs/job-N.txt with a golden answer, run 3 times each with prompts/extract-v2.txt
    private async Task EvalAsync(string arguments)
    {
        string[] parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int runs = 3;
        if (parts.Length is < 1 or > 2
            || !(parts[0].Equals("v1", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("v2", StringComparison.OrdinalIgnoreCase))
            || (parts.Length == 2 && (!int.TryParse(parts[1], out runs) || runs < 1)))
        {
            Console.WriteLine("Usage: /eval <v1|v2> [runs]   e.g. /eval v2 3 (runs defaults to 3)");
            return;
        }
        string version = parts[0].ToLowerInvariant();

        // Read once, so every run of this eval uses the same prompt text
        string promptPath = Path.Combine("prompts", $"extract-{version}.txt");
        if (!File.Exists(promptPath))
        {
            Console.WriteLine($"[prompt not found: {Path.GetFullPath(promptPath)}]");
            return;
        }
        string instruction = await File.ReadAllTextAsync(promptPath);

        // job-N.txt with a job-N.expected.json next to it, sorted by N so job-10 comes after job-9
        List<(string Name, string Path, JobPosting Expected)> inputs = [];
        IEnumerable<(int Number, string Path)> candidates = Directory.Exists("inputs")
            ? Directory.GetFiles("inputs", "job-*.txt")
                .Select(path => (Match: Regex.Match(Path.GetFileName(path), @"^job-(\d+)\.txt$"), Path: path))
                .Where(c => c.Match.Success)
                .Select(c => (int.Parse(c.Match.Groups[1].Value), c.Path))
                .OrderBy(c => c.Item1)
            : [];
        foreach ((int _, string path) in candidates)
        {
            string expectedPath = JobScorer.ExpectedPathFor(path);
            if (!File.Exists(expectedPath))
                continue;

            JobPosting? expected = JobScorer.LoadExpected(expectedPath, out string? error);
            if (expected is null)
            {
                Console.WriteLine($"[skipping {path}: {expectedPath} doesn't fit JobPosting: {error}]");
                continue;
            }
            inputs.Add((Path.GetFileNameWithoutExtension(path), path, expected));
        }

        if (inputs.Count == 0)
        {
            Console.WriteLine($"[no inputs with a golden answer in {Path.GetFullPath("inputs")}]");
            return;
        }

        // One folder per eval, so the replies can be re-scored later without new model calls
        string model = _metadata?.DefaultModelId ?? "unknown-model";
        string runFolder = Path.Combine("eval-runs", $"{DateTime.Now:yyyyMMdd-HHmmss}_{model}_{version}");
        Directory.CreateDirectory(runFolder);

        Console.WriteLine($"[eval {version}: {inputs.Count} inputs × {runs} run{(runs == 1 ? "" : "s")} on {model}, replies saved to {runFolder}/]");

        List<EvalRun> results = [];
        string? stoppedBecause = null;

        // Ctrl+C cancels the call in flight (see RunAsync); the loop then stops and still prints the totals so far
        _turnCts = new CancellationTokenSource();
        try
        {
            foreach ((string name, string path, JobPosting expected) in inputs)
            {
                string postingText = await File.ReadAllTextAsync(path);
                for (int run = 1; run <= runs && stoppedBecause is null; run++)
                {
                    ChatResponse<JobPosting> response;
                    try
                    {
                        response = await _extractor.ExtractAsync(instruction, postingText, _turnCts.Token);
                    }
                    // OllamaSharp can report a cancelled call as "500 (Internal Server Error)" instead of
                    // OperationCanceledException, so the token decides whether Ctrl+C caused the failure
                    catch (Exception) when (_turnCts.IsCancellationRequested)
                    {
                        stoppedBecause = "cancelled";
                        break;
                    }
                    catch (Exception ex)
                    {
                        // A provider error (server down, rate limit) would most likely repeat on every remaining run
                        stoppedBecause = $"error on {name} run {run}: {ex.Message}";
                        break;
                    }

                    await File.WriteAllTextAsync(Path.Combine(runFolder, $"{name}_run-{run}.txt"), response.Text);

                    bool parsed = StructuredReply.TryParse(response, out JobPosting? job, out bool strippedFence) && job is not null;
                    JobScore? score = parsed ? JobScorer.Score(job!, expected) : null;
                    EvalRun result = new(name, run, score, expected.RequiredSkills.Count,
                        response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, AddToSessionTotal(response));
                    results.Add(result);

                    PrintEvalRun(result, strippedFence, response.FinishReason == ChatFinishReason.Length);

                    // Ctrl+C can land just as a call finishes; that run is kept, but nothing new starts
                    if (_turnCts.IsCancellationRequested)
                        stoppedBecause = "cancelled";
                }

                if (stoppedBecause is not null)
                    break;
            }
        }
        finally
        {
            _turnCts.Dispose();
            _turnCts = null;
        }

        if (stoppedBecause is not null)
            Console.WriteLine($"[eval stopped ({stoppedBecause}) after {results.Count} of {inputs.Count * runs} runs]");

        PrintEvalTotals(version, results, inputs.Count, runs);
    }

    private static void PrintEvalRun(EvalRun result, bool strippedFence, bool cutOff)
    {
        string parsed = result.Score is null ? "❌ not parsed" : strippedFence ? "✅ parsed (fence)" : "✅ parsed";
        string fields = result.Score is null ? "fields 0/5" : $"fields {result.Score.FieldsCorrect}/5";
        if (result.Score is not null && result.Score.FieldsCorrect < 5)
            fields += $" (wrong: {string.Join(", ", result.Score.WrongFields)})";
        string skills = $"skills {result.Score?.SkillsMatched ?? 0} of {result.SkillsTotal}";
        string tokens = $"in={result.InputTokens?.ToString() ?? "?"} out={result.OutputTokens?.ToString() ?? "?"}";
        string cost = result.Cost is null ? "$?" : $"${result.Cost:0.000000}";

        Console.WriteLine($"{result.Input} run {result.Run}  {parsed}  {fields}  {skills}  {tokens}  {cost}{(cutOff ? "  ⚠ cut off" : "")}");
    }

    // A pass is one run number over all inputs (run 1 of every input, then run 2, ...).
    // Average, lowest and highest are taken over passes, so the spread shows how much the same prompt moves between runs.
    private void PrintEvalTotals(string version, List<EvalRun> results, int inputCount, int runs)
    {
        Console.WriteLine($"\nTotals for prompt {version}:");
        if (results.Count == 0)
        {
            Console.WriteLine("  no completed runs");
            return;
        }

        int parseFailures = results.Count(r => r.Score is null);
        Console.WriteLine($"  Parse failures: {parseFailures} of {results.Count} runs");

        var passes = results
            .GroupBy(r => r.Run)
            .OrderBy(g => g.Key)
            .Select(g => (
                Run: g.Key,
                Inputs: g.Count(),
                Fields: g.Sum(r => r.Score?.FieldsCorrect ?? 0),
                Skills: g.Sum(r => r.Score?.SkillsMatched ?? 0),
                SkillsTotal: g.Sum(r => r.SkillsTotal)))
            .ToList();

        foreach (var pass in passes)
        {
            string partial = pass.Inputs < inputCount ? $" (only {pass.Inputs} of {inputCount} inputs)" : "";
            Console.WriteLine($"  Pass {pass.Run}: fields {pass.Fields}/{pass.Inputs * 5}, skills {pass.Skills} of {pass.SkillsTotal}{partial}");
        }

        if (passes.Any(p => p.Inputs < inputCount))
            Console.WriteLine("  ⚠ The eval didn't finish, so the passes cover different inputs and aren't comparable.");

        // From the passes, not the input list, so a cancelled eval shows "of 5" for one finished input instead of "of 25"
        int fieldsTotal = passes.Max(p => p.Inputs) * 5;
        int skillsTotal = passes.Max(p => p.SkillsTotal);
        Console.WriteLine($"  Fields correct: avg {passes.Average(p => p.Fields):0.0} of {fieldsTotal}, lowest {passes.Min(p => p.Fields)}, highest {passes.Max(p => p.Fields)} (over {passes.Count} of {runs} passes)");
        Console.WriteLine($"  Skills recall:  avg {passes.Average(p => p.Skills):0.0} of {skillsTotal}, lowest {passes.Min(p => p.Skills)}, highest {passes.Max(p => p.Skills)}");

        long inputTokens = results.Sum(r => r.InputTokens ?? 0);
        long outputTokens = results.Sum(r => r.OutputTokens ?? 0);
        string unknownUsage = results.Any(r => r.InputTokens is null || r.OutputTokens is null) ? " (some runs reported no usage)" : "";
        string cost = results.Any(r => r.Cost is null) ? "unknown" : $"${results.Sum(r => r.Cost!.Value):0.000000}";
        Console.WriteLine($"  Tokens: in={inputTokens}, out={outputTokens}{unknownUsage} | Cost: {cost} | Session: ${_sessionTotal:0.000000}");
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

        decimal? turnCost = AddToSessionTotal(response);
        if (turnCost is null)
        {
            Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Finish Reason: {response.FinishReason} | Turn: unknown price for '{_metadata?.DefaultModelId}'");
            return;
        }

        Console.WriteLine($"Tokens: in={inputTokens}, out={outputTokens} | Finish Reason: {response.FinishReason} | Turn: ${turnCost:0.000000} | Session: ${_sessionTotal:0.000000}");
    }

    // Prices one response by the model the client was created with and adds it to the session total.
    // Null when the tokens or the price are unknown, so an unknown price never shows up as $0.
    private decimal? AddToSessionTotal(ChatResponse response)
    {
        if (response.Usage?.InputTokenCount is not long inputTokens || response.Usage.OutputTokenCount is not long outputTokens)
            return null;

        string? model = _metadata?.DefaultModelId;
        decimal? cost = model is null ? null : ModelPrices.Cost(model, inputTokens, outputTokens);
        if (cost is not null)
            _sessionTotal += cost.Value;
        return cost;
    }
}

using Microsoft.Extensions.AI;

namespace AiLab.Console.Extraction;

// One model call that turns a posting into a JobPosting. It only knows IChatClient, like ChatSession.
public class JobExtractor(IChatClient client)
{
    // Prompt v1: one plain line. Deliverable 4 compares it with a longer prompt.
    private const string Instruction = "Extract the job details from this posting.";

    // Haiku thinks by default and stopped at 1024 tokens with no limit set (experiments.md, section 3).
    // A cut-off JSON can't be parsed, so leave room for thinking plus the answer.
    private readonly ChatOptions _options = new() { MaxOutputTokens = 2000 };

    public Task<ChatResponse<JobPosting>> ExtractAsync(string postingText, CancellationToken cancellationToken = default)
    {
        // Its own message list: nothing here goes into the chat history
        List<ChatMessage> messages =
        [
            new(ChatRole.User, $"{Instruction}\n\n{postingText}"),
        ];

        // Sends the JobPosting JSON schema (with the [Description] rules) and parses the reply into it.
        // useJsonSchemaResponseFormat: false puts the schema into the prompt instead of the provider's native
        // structured-output setting: Haiku ignored the native one (answered in markdown), see experiments.md.
        return client.GetResponseAsync<JobPosting>(messages, _options, useJsonSchemaResponseFormat: false, cancellationToken: cancellationToken);
    }
}

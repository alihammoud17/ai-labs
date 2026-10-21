using Microsoft.Extensions.AI;

namespace AiLab.Console.Extraction;

// One model call that turns a posting into a JobPosting. It only knows IChatClient, like ChatSession.
public class JobExtractor(IChatClient client)
{
    // Haiku thinks by default and stopped at 1024 tokens with no limit set (experiments.md, section 3).
    // A cut-off JSON can't be parsed, so leave room for thinking plus the answer.
    private readonly ChatOptions _options = new() { MaxOutputTokens = 2000 };

    // The instruction comes from a prompt file (prompts/extract-v1.txt or -v2.txt), so prompts change without recompiling
    public Task<ChatResponse<JobPosting>> ExtractAsync(string instruction, string postingText, CancellationToken cancellationToken = default)
    {
        // Its own message list: nothing here goes into the chat history
        List<ChatMessage> messages =
        [
            new(ChatRole.User, $"{instruction.Trim()}\n\n{postingText}"),
        ];

        // Sends the JobPosting JSON schema (with the [Description] rules) and parses the reply into it.
        // useJsonSchemaResponseFormat: false puts the schema into the prompt instead of the provider's native
        // structured-output setting: Haiku ignored the native one (answered in markdown), see experiments.md.
        return client.GetResponseAsync<JobPosting>(messages, _options, useJsonSchemaResponseFormat: false, cancellationToken: cancellationToken);
    }
}

using AiLab.Console.Providers;
using Microsoft.Extensions.AI;

IChatClient client = ChatClientFactory.Create("ollama", "llama3.2");

ChatResponse response = await client.GetResponseAsync(
    "Explain what a token is in one sentence.");

Console.WriteLine(response.Text);
Console.WriteLine($"Tokens: in={response.Usage?.InputTokenCount}, out={response.Usage?.OutputTokenCount}");
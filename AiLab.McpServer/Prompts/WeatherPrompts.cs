using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// Prompts: templates the user picks (in Claude Code: /mcp__ailab-mcp__weather_report).
/// The returned string becomes a user message; the model then decides what to do with it.
/// </summary>
[McpServerPromptType]
internal class WeatherPrompts
{
    [McpServerPrompt(Name = "weather_report")]
    [Description("Asks for a short weather report for a city, using the get_city_weather tool.")]
    public static string WeatherReport(
        [Description("Name of the city to report on")] string city) =>
        $"Call the get_city_weather tool for {city}, then write a two-sentence weather report for {city} " +
        "with one practical tip (what to wear or bring). Use only what the tool returned; don't invent temperatures.";
}

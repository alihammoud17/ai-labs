using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

/// <summary>
/// Sample MCP tools for demonstration purposes.
/// These tools can be invoked by MCP clients to perform various operations.
/// </summary>
internal class RandomNumberTools(ILogger<RandomNumberTools> logger)
{
    [McpServerTool]
    [Description("Generates a random number between the specified minimum and maximum values.")]
    public int GetRandomNumber(
        [Description("Minimum value (inclusive)")] int min = 0,
        [Description("Maximum value (exclusive)")] int max = 100)
    {
        // Never write to stdout in a stdio server: it carries the JSON-RPC messages.
        // ILogger goes to stderr (see LogToStandardErrorThreshold in Program.cs).
        logger.LogInformation("Generating random number between {Min} and {Max}", min, max);
        return Random.Shared.Next(min, max);
    }

    [McpServerTool]
    [Description("Describes random weather in the provided city.")]
    public string GetCityWeather(
        [Description("Name of the city to return weather for")] string city)
    {
        // WEATHER_CHOICES, read during tool execution (see WeatherChoices)
        var weatherChoices = WeatherChoices.Get();

        var selectedWeatherIndex = Random.Shared.Next(0, weatherChoices.Length);

        return $"The weather in {city} is {weatherChoices[selectedWeatherIndex]}.";
    }
}

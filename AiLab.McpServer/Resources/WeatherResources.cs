using System.ComponentModel;
using ModelContextProtocol.Server;

/// <summary>
/// Resources: read-only context that the app or the user attaches (in Claude Code: @ailab-mcp:...).
/// The model doesn't call them the way it calls tools.
/// </summary>
[McpServerResourceType]
internal class WeatherResources
{
    [McpServerResource(UriTemplate = "ailab://weather/choices", Name = "weather_choices", MimeType = "text/plain")]
    [Description("The weather values get_city_weather picks from (WEATHER_CHOICES, or the default list when it's empty).")]
    public static string GetWeatherChoices() => string.Join(Environment.NewLine, WeatherChoices.Get());
}

/// <summary>
/// Reads the weather choices from the WEATHER_CHOICES env var. Shared by the
/// get_city_weather tool and the weather-choices resource, so both see the same list.
/// </summary>
internal static class WeatherChoices
{
    public static readonly string[] Default = ["balmy", "rainy", "stormy"];

    public static string[] Get()
    {
        // Read on every call (the env var is set once, when the host starts the server)
        var weather = Environment.GetEnvironmentVariable("WEATHER_CHOICES");

        // Trim spaces and drop empty items, so "sunny, humid", "sunny,,humid" and "," all work
        var choices = weather?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return choices.Length == 0 ? Default : choices;
    }
}

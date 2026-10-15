using System.Globalization;
using System.Text.Json;

namespace RaceDay.Api.Services;

public record WeatherResult(DateTime ForecastDate, double TempC, string Conditions, double WindSpeed);

public interface IWeatherService
{
    /// <summary>Returns the forecast for a location on a date, or null if it isn't available.</summary>
    Task<WeatherResult?> GetForecastAsync(string location, DateTime date, CancellationToken ct = default);
}

/// <summary>Uses the free Open-Meteo geocoding + forecast APIs (no API key needed).</summary>
public class WeatherService : IWeatherService
{
    private readonly HttpClient _http;
    public WeatherService(HttpClient http) => _http = http;

    public async Task<WeatherResult?> GetForecastAsync(string location, DateTime date, CancellationToken ct = default)
    {
        try
        {
            // "Pretoria, Gauteng" -> geocode on "Pretoria"
            var place = location.Split(',')[0].Trim();

            using var geoStream = await _http.GetStreamAsync(
                $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(place)}&count=1", ct);
            using var geo = await JsonDocument.ParseAsync(geoStream, cancellationToken: ct);

            if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return null;

            var lat = results[0].GetProperty("latitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            var lon = results[0].GetProperty("longitude").GetDouble().ToString(CultureInfo.InvariantCulture);
            var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            using var fcStream = await _http.GetStreamAsync(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                $"&daily=temperature_2m_max,weather_code,wind_speed_10m_max" +
                $"&start_date={day}&end_date={day}&timezone=auto", ct);
            using var fc = await JsonDocument.ParseAsync(fcStream, cancellationToken: ct);

            var daily = fc.RootElement.GetProperty("daily");
            return new WeatherResult(
                date.Date,
                daily.GetProperty("temperature_2m_max")[0].GetDouble(),
                Describe(daily.GetProperty("weather_code")[0].GetInt32()),
                daily.GetProperty("wind_speed_10m_max")[0].GetDouble());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                       or InvalidOperationException or TaskCanceledException)
        {
            // The API rejects dates outside its forecast window (roughly the next 16 days).
            return null;
        }
    }

    // WMO weather interpretation codes -> readable text
    private static string Describe(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 => "Mostly clear",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Rain showers",
        85 or 86 => "Snow showers",
        >= 95 => "Thunderstorm",
        _ => "Unknown"
    };
}

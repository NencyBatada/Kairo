using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Kairo.Modules.Triggers.Regions;

namespace Kairo.Modules.Triggers.Weather;

internal interface IWeatherToken
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

internal sealed class ApplicationDefaultWeatherToken : IWeatherToken
{
    public const string Scope = "https://www.googleapis.com/auth/cloud-platform";

    private GoogleCredential? _credential;

    public async Task<string> GetAsync(CancellationToken cancellationToken)
    {
        _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken)).CreateScoped(Scope);
        return await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
    }
}

internal sealed class WeatherUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

internal sealed class WeatherClient(IHttpClientFactory http, IWeatherToken token, TriggerSettings settings)
{
    public const string ClientName = "kairo-triggers-weather";
    public const string LookupUrl = "https://weather.googleapis.com/v1/forecast/days:lookup";
    public const int Days = 7;

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<List<ForecastDay>> DaysAsync(Region city, CancellationToken cancellationToken)
    {
        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{LookupUrl}?location.latitude={city.Latitude}&location.longitude={city.Longitude}&days={Days}&pageSize={Days}&unitsSystem=METRIC");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token.GetAsync(cancellationToken));
            if (settings.WeatherProject is { } project)
            {
                request.Headers.Add("X-Goog-User-Project", project);
            }

            using var response = await http.CreateClient(ClientName).SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherUnavailableException($"The Weather API answered {(int)response.StatusCode} for {city.Name}.");
            }

            using var document = JsonDocument.Parse(text);
            return Parse(document.RootElement);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new WeatherUnavailableException($"Kairo couldn't read the forecast for {city.Name}.", exception);
        }
    }

    public static List<ForecastDay> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("forecastDays", out var days) || days.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<ForecastDay>();
        foreach (var day in days.EnumerateArray())
        {
            if (!day.TryGetProperty("displayDate", out var date)
                || Number(date, "year") is not { } year
                || Number(date, "month") is not { } month
                || Number(date, "day") is not { } dayOfMonth)
            {
                continue;
            }

            parsed.Add(new ForecastDay
            {
                Date = new DateOnly((int)year, (int)month, (int)dayOfMonth),
                RainMillimetres = Math.Round(Rain(day, "daytimeForecast") + Rain(day, "nighttimeForecast"), 1),
                MaxCelsius = day.TryGetProperty("maxTemperature", out var max) ? Number(max, "degrees") : null,
            });
        }

        return parsed;
    }

    private static double Rain(JsonElement day, string part) =>
        day.TryGetProperty(part, out var forecast)
        && forecast.TryGetProperty("precipitation", out var precipitation)
        && precipitation.TryGetProperty("qpf", out var qpf)
            ? Number(qpf, "quantity") ?? 0
            : 0;

    private static double? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}

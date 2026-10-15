using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Kairo.Modules.Triggers.Regions;
using Microsoft.AspNetCore.WebUtilities;

namespace Kairo.IntegrationTests.Triggers;

public sealed class FakeWeather
{
    public DateOnly From { get; set; } = new(2026, 10, 14);

    public ConcurrentDictionary<string, double[]> Rain { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<string, double[]> Heat { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Failing { get; } = [];

    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentQueue<string?> Projects { get; } = new();

    public HttpResponseMessage Handle(HttpRequestMessage request)
    {
        var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
        var latitude = double.Parse(query["location.latitude"].ToString(), CultureInfo.InvariantCulture);
        var longitude = double.Parse(query["location.longitude"].ToString(), CultureInfo.InvariantCulture);
        var days = int.Parse(query["days"].ToString(), CultureInfo.InvariantCulture);
        var city = IndiaRegions.All.Single(region => region.Latitude == latitude && region.Longitude == longitude);
        Calls.Enqueue(city.Id);
        Projects.Enqueue(request.Headers.TryGetValues("X-Goog-User-Project", out var values) ? values.Single() : null);
        if (Failing.Contains(city.Id))
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var rain = Rain.GetValueOrDefault(city.Id) ?? [];
        var heat = Heat.GetValueOrDefault(city.Id) ?? [];
        var forecast = Enumerable.Range(0, days).Select(index =>
        {
            var date = From.AddDays(index);
            var mm = index < rain.Length ? rain[index] : 0;
            return new
            {
                displayDate = new { year = date.Year, month = date.Month, day = date.Day },
                daytimeForecast = new { precipitation = new { qpf = new { quantity = mm / 2, unit = "MILLIMETERS" } } },
                nighttimeForecast = new { precipitation = new { qpf = new { quantity = mm / 2, unit = "MILLIMETERS" } } },
                maxTemperature = new { degrees = index < heat.Length ? heat[index] : 31.0, unit = "CELSIUS" },
            };
        }).ToArray();
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { forecastDays = forecast, timeZone = new { id = "Asia/Kolkata" } }) };
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VNotch.Models;

namespace VNotch.Services;

/// <summary>
/// The weather tab's full forecast: now, the next hours, seven days and a quarter-hour rain nowcast. Open-Meteo
/// first; when it is down or overloaded, MET Norway (yr.no) fills in for a while.
/// </summary>
public sealed partial class WeatherService
{
    private const int MaxForecastResponseBytes = 1024 * 1024;
    private static readonly TimeSpan PlaceCacheAge = TimeSpan.FromHours(3);
    private static readonly TimeSpan OpenMeteoBackoff = TimeSpan.FromMinutes(20);

    private sealed record PlaceFix(string Key, double Lat, double Lon, string City, DateTime At);
    private PlaceFix? _forecastPlace;
    private DateTime _openMeteoBackoffUntil = DateTime.MinValue;

    public async Task<WeatherForecast?> GetForecastAsync(string? manualCity, CancellationToken cancellationToken = default)
    {
        try
        {
            var place = await ResolveForecastPlaceAsync(manualCity, cancellationToken).ConfigureAwait(false);
            if (place == null) return null;

            WeatherForecast? forecast = null;
            if (DateTime.UtcNow >= _openMeteoBackoffUntil)
            {
                forecast = await TryOpenMeteoForecastAsync(place, cancellationToken).ConfigureAwait(false);
                if (forecast == null) _openMeteoBackoffUntil = DateTime.UtcNow + OpenMeteoBackoff;
            }
            forecast ??= await TryMetNorwayForecastAsync(place, cancellationToken).ConfigureAwait(false);
            if (forecast != null)
                RuntimeLog.Log(LogCategory, $"Forecast for {forecast.Place} from {forecast.Source}: {forecast.Hours.Count} steps, {forecast.Days.Count} days");
            return forecast;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            RuntimeLog.Log(LogCategory, $"Forecast failed: {ex.Message}");
            return null;
        }
    }

    private async Task<PlaceFix?> ResolveForecastPlaceAsync(string? manualCity, CancellationToken token)
    {
        string key = CleanCity(manualCity) ?? "";
        var cached = _forecastPlace;
        if (cached != null && cached.Key == key && DateTime.UtcNow - cached.At < PlaceCacheAge) return cached;

        var resolved = await ResolveLocationAsync(manualCity, token).ConfigureAwait(false);
        if (resolved is not { } r)
            // Offline for a moment: the last place is still right.
            return cached != null && cached.Key == key ? cached : null;
        _forecastPlace = new PlaceFix(key, r.lat, r.lon, r.city, DateTime.UtcNow);
        return _forecastPlace;
    }

    private async Task<WeatherForecast?> TryOpenMeteoForecastAsync(PlaceFix place, CancellationToken token)
    {
        string timezone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out string? iana) && !string.IsNullOrEmpty(iana)
            ? iana
            : "auto";
        string url =
            "https://api.open-meteo.com/v1/forecast" +
            $"?latitude={place.Lat.ToString("0.####", CultureInfo.InvariantCulture)}" +
            $"&longitude={place.Lon.ToString("0.####", CultureInfo.InvariantCulture)}" +
            "&current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code," +
            "wind_speed_10m,wind_direction_10m,wind_gusts_10m,uv_index" +
            "&hourly=temperature_2m,precipitation_probability,precipitation,weather_code,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,uv_index_max" +
            "&minutely_15=precipitation&past_minutely_15=1&forecast_minutely_15=9" +
            $"&timezone={Uri.EscapeDataString(timezone)}&forecast_days=7";
        try
        {
            using var requestCts = CreateRequestCancellation(token);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                RuntimeLog.Log(LogCategory, $"Open-Meteo forecast HTTP {(int)response.StatusCode}");
                return null;
            }
            using var doc = await ReadLimitedJsonAsync(response, MaxForecastResponseBytes, requestCts.Token).ConfigureAwait(false);
            return doc == null ? null : ParseOpenMeteo(doc.RootElement, place.City, place.Lat, place.Lon, DateTime.Now);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            RuntimeLog.Log(LogCategory, "Open-Meteo forecast timed out.");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            RuntimeLog.Log(LogCategory, $"Open-Meteo forecast failed: {ex.Message}");
            return null;
        }
    }

    private async Task<WeatherForecast?> TryMetNorwayForecastAsync(PlaceFix place, CancellationToken token)
    {
        // MET asks for at most four decimals.
        string url =
            "https://api.met.no/weatherapi/locationforecast/2.0/complete" +
            $"?lat={place.Lat.ToString("0.####", CultureInfo.InvariantCulture)}" +
            $"&lon={place.Lon.ToString("0.####", CultureInfo.InvariantCulture)}";
        try
        {
            using var requestCts = CreateRequestCancellation(token);
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, requestCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                RuntimeLog.Log(LogCategory, $"MET Norway HTTP {(int)response.StatusCode}");
                return null;
            }
            using var doc = await ReadLimitedJsonAsync(response, MaxForecastResponseBytes, requestCts.Token).ConfigureAwait(false);
            return doc == null ? null : ParseMetNorway(doc.RootElement, place.City, place.Lat, place.Lon, DateTime.Now);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            RuntimeLog.Log(LogCategory, "MET Norway timed out.");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            RuntimeLog.Log(LogCategory, $"MET Norway failed: {ex.Message}");
            return null;
        }
    }

    #region Open-Meteo

    internal static WeatherForecast? ParseOpenMeteo(JsonElement root, string place, double lat, double lon, DateTime fetchedAt)
    {
        if (!root.TryGetProperty("current", out var cur) || !root.TryGetProperty("hourly", out var hourly) ||
            !root.TryGetProperty("daily", out var daily))
            return null;

        double temp = Num(cur, "temperature_2m");
        if (double.IsNaN(temp)) return null;
        double gust = Num(cur, "wind_gusts_10m");
        double feels = Num(cur, "apparent_temperature");
        var now = new WeatherNow(
            temp,
            double.IsNaN(feels) ? temp : feels,
            (int)Math.Round(Or(Num(cur, "relative_humidity_2m"), 0)),
            (int)Or(Num(cur, "weather_code"), 0),
            Or(Num(cur, "is_day"), 1) >= 0.5,
            Or(Num(cur, "wind_speed_10m"), 0),
            Or(Num(cur, "wind_direction_10m"), 0),
            double.IsNaN(gust) ? null : gust,
            Math.Max(0, Or(Num(cur, "uv_index"), 0)));

        var times = Times(hourly, "time");
        var temps = Numbers(hourly, "temperature_2m", times.Length);
        var chances = Numbers(hourly, "precipitation_probability", times.Length);
        var amounts = Numbers(hourly, "precipitation", times.Length);
        var codes = Numbers(hourly, "weather_code", times.Length);
        var days = Numbers(hourly, "is_day", times.Length);
        var hours = new List<WeatherHour>(times.Length);
        for (int i = 0; i < times.Length; i++)
        {
            if (double.IsNaN(temps[i])) continue;
            hours.Add(new WeatherHour(times[i], temps[i],
                double.IsNaN(chances[i]) ? -1 : (int)Math.Round(chances[i]),
                Math.Max(0, Or(amounts[i], 0)),
                (int)Or(codes[i], 0),
                double.IsNaN(days[i]) ? SunTimes.IsDay(times[i], lat, lon) : days[i] >= 0.5));
        }

        var dates = Times(daily, "time");
        var dayCodes = Numbers(daily, "weather_code", dates.Length);
        var highs = Numbers(daily, "temperature_2m_max", dates.Length);
        var lows = Numbers(daily, "temperature_2m_min", dates.Length);
        var dayChances = Numbers(daily, "precipitation_probability_max", dates.Length);
        var sums = Numbers(daily, "precipitation_sum", dates.Length);
        var uvs = Numbers(daily, "uv_index_max", dates.Length);
        var dayList = new List<WeatherDay>(dates.Length);
        for (int i = 0; i < dates.Length; i++)
        {
            if (double.IsNaN(highs[i]) || double.IsNaN(lows[i])) continue;
            dayList.Add(new WeatherDay(dates[i].Date, (int)Or(dayCodes[i], 0), lows[i], highs[i],
                double.IsNaN(dayChances[i]) ? -1 : (int)Math.Round(dayChances[i]),
                Math.Max(0, Or(sums[i], 0)), Math.Max(0, Or(uvs[i], 0))));
        }

        var quarters = new List<WeatherQuarter>();
        if (root.TryGetProperty("minutely_15", out var minutely))
        {
            var qTimes = Times(minutely, "time");
            var qAmounts = Numbers(minutely, "precipitation", qTimes.Length);
            for (int i = 0; i < qTimes.Length; i++)
                if (!double.IsNaN(qAmounts[i])) quarters.Add(new WeatherQuarter(qTimes[i], Math.Max(0, qAmounts[i])));
        }

        if (hours.Count == 0 || dayList.Count == 0) return null;
        return new WeatherForecast
        {
            Place = place,
            Latitude = lat,
            Longitude = lon,
            FetchedAt = fetchedAt,
            Source = "Open-Meteo",
            Now = now,
            Hours = hours,
            Days = dayList,
            Quarters = quarters
        };
    }

    private static double Num(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.NaN;

    private static double Or(double value, double fallback) => double.IsNaN(value) ? fallback : value;

    private static double[] Numbers(JsonElement obj, string name, int count)
    {
        var result = new double[count];
        Array.Fill(result, double.NaN);
        if (!obj.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return result;
        int i = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (i >= count) break;
            if (item.ValueKind == JsonValueKind.Number) result[i] = item.GetDouble();
            i++;
        }
        return result;
    }

    private static DateTime[] Times(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return Array.Empty<DateTime>();
        var result = new List<DateTime>(array.GetArrayLength());
        foreach (var item in array.EnumerateArray())
        {
            // Local times in the requested zone, e.g. "2026-11-02T15:00" or "2026-11-02".
            if (DateTime.TryParse(item.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                result.Add(DateTime.SpecifyKind(time, DateTimeKind.Local));
            else
                return Array.Empty<DateTime>();
        }
        return result.ToArray();
    }

    #endregion

    #region MET Norway

    internal static WeatherForecast? ParseMetNorway(JsonElement root, string place, double lat, double lon, DateTime fetchedAt)
    {
        if (!root.TryGetProperty("properties", out var props) || !props.TryGetProperty("timeseries", out var series) ||
            series.ValueKind != JsonValueKind.Array)
            return null;

        var hours = new List<WeatherHour>();
        var spans = new List<double>();
        var uvs = new List<double>();
        var extremes = new List<(DateTime From, double Min, double Max)>();
        WeatherNow? now = null;
        foreach (var step in series.EnumerateArray())
        {
            if (!step.TryGetProperty("time", out var timeProp) ||
                !DateTime.TryParse(timeProp.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc) ||
                !step.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("instant", out var instant) ||
                !instant.TryGetProperty("details", out var details))
                continue;
            double temp = Num(details, "air_temperature");
            if (double.IsNaN(temp)) continue;
            DateTime local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

            double span = 1;
            if (!data.TryGetProperty("next_1_hours", out var next))
            {
                if (!data.TryGetProperty("next_6_hours", out next)) continue; // the very last step has neither
                span = 6;
            }
            string? symbol = next.TryGetProperty("summary", out var summary) && summary.TryGetProperty("symbol_code", out var sym)
                ? sym.GetString()
                : null;
            int code = MetSymbolToWmo(symbol, out bool? dayFromSymbol);
            bool isDay = dayFromSymbol ?? SunTimes.IsDay(local, lat, lon);
            double amount = 0, chance = double.NaN;
            if (next.TryGetProperty("details", out var nextDetails))
            {
                amount = Math.Max(0, Or(Num(nextDetails, "precipitation_amount"), 0));
                chance = Num(nextDetails, "probability_of_precipitation");
            }
            if (data.TryGetProperty("next_6_hours", out var six) && six.TryGetProperty("details", out var sixDetails))
            {
                double min = Num(sixDetails, "air_temperature_min"), max = Num(sixDetails, "air_temperature_max");
                if (!double.IsNaN(min) && !double.IsNaN(max)) extremes.Add((local, min, max));
            }

            hours.Add(new WeatherHour(local, temp, double.IsNaN(chance) ? -1 : (int)Math.Round(chance), amount / span, code, isDay));
            spans.Add(span);
            double uv = Math.Max(0, Or(Num(details, "ultraviolet_index_clear_sky"), 0));
            uvs.Add(uv);

            if (now == null)
            {
                double feels = Num(details, "apparent_air_temperature");
                double gust = Num(details, "wind_speed_of_gust");
                now = new WeatherNow(
                    temp,
                    double.IsNaN(feels) ? ApparentTemperature(temp, Or(Num(details, "relative_humidity"), 70), Or(Num(details, "wind_speed"), 0)) : feels,
                    (int)Math.Round(Or(Num(details, "relative_humidity"), 0)),
                    code,
                    SunTimes.IsDay(DateTime.Now, lat, lon),
                    Or(Num(details, "wind_speed"), 0) * 3.6,
                    Or(Num(details, "wind_from_direction"), 0),
                    double.IsNaN(gust) ? null : gust * 3.6,
                    uv);
            }
        }
        if (now == null || hours.Count == 0) return null;

        var days = new List<WeatherDay>();
        foreach (var group in hours.Select((hour, index) => (hour, index)).GroupBy(x => x.hour.Time.Date).OrderBy(g => g.Key))
        {
            if (days.Count == 7) break;
            var items = group.ToList();
            double low = items.Min(x => x.hour.Temperature), high = items.Max(x => x.hour.Temperature);
            foreach (var (from, min, max) in extremes)
            {
                if (from.Date != group.Key) continue;
                low = Math.Min(low, min);
                high = Math.Max(high, max);
            }
            // The day's icon is its roughest weather while people are up.
            var daytime = items.Where(x => x.hour.Time.Hour is >= 7 and <= 20).ToList();
            var pool = daytime.Count > 0 ? daytime : items;
            int dayCode = pool.OrderByDescending(x => Severity(x.hour.Code)).First().hour.Code;
            int chance = items.Any(x => x.hour.PrecipitationChance >= 0) ? items.Max(x => x.hour.PrecipitationChance) : -1;
            double sum = items.Sum(x => x.hour.Precipitation * spans[x.index]);
            double uvMax = items.Max(x => uvs[x.index]);
            days.Add(new WeatherDay(group.Key, dayCode, low, high, chance, sum, uvMax));
        }

        return new WeatherForecast
        {
            Place = place,
            Latitude = lat,
            Longitude = lon,
            FetchedAt = fetchedAt,
            Source = "MET Norway",
            Now = now,
            Hours = hours,
            Days = days
        };
    }

    /// <summary>MET's symbol codes ("lightrainshowers_day") as WMO weather codes, and whether the symbol says day or night.</summary>
    internal static int MetSymbolToWmo(string? symbol, out bool? isDay)
    {
        isDay = null;
        if (string.IsNullOrEmpty(symbol)) return 3;
        string s = symbol;
        int underscore = s.IndexOf('_');
        if (underscore > 0)
        {
            string suffix = s[(underscore + 1)..];
            isDay = suffix switch { "day" or "polartwilight" => true, "night" => false, _ => null };
            s = s[..underscore];
        }
        if (s.Contains("thunder", StringComparison.Ordinal)) return 95;
        return s switch
        {
            "clearsky" => 0,
            "fair" => 1,
            "partlycloudy" => 2,
            "cloudy" => 3,
            "fog" => 45,
            "lightrain" => 61,
            "rain" => 63,
            "heavyrain" => 65,
            "lightrainshowers" => 80,
            "rainshowers" => 81,
            "heavyrainshowers" => 82,
            "lightsleet" or "lightsleetshowers" => 66,
            "sleet" or "heavysleet" or "sleetshowers" or "heavysleetshowers" => 67,
            "lightsnow" => 71,
            "snow" => 73,
            "heavysnow" => 75,
            "lightsnowshowers" or "snowshowers" => 85,
            "heavysnowshowers" => 86,
            _ => 3
        };
    }

    /// <summary>How rough a WMO code is, for picking a day's icon.</summary>
    internal static int Severity(int code) => code switch
    {
        >= 95 => 9,
        >= 71 and <= 77 or 85 or 86 => 8,
        56 or 57 or 66 or 67 => 7,
        63 or 65 or 81 or 82 => 6,
        61 or 80 => 5,
        >= 51 and <= 55 => 4,
        45 or 48 => 3,
        3 => 2,
        2 => 1,
        _ => 0
    };

    /// <summary>Steadman's apparent temperature (Australian BoM), wind in m/s.</summary>
    internal static double ApparentTemperature(double celsius, double humidity, double windMs)
    {
        double vapour = humidity / 100.0 * 6.105 * Math.Exp(17.27 * celsius / (237.7 + celsius));
        return celsius + 0.33 * vapour - 0.70 * windMs - 4.00;
    }

    #endregion
}

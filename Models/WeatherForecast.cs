using System;
using System.Collections.Generic;

namespace VNotch.Models;

/// <summary>Conditions right now. Temperatures in °C, wind in km/h, <see cref="WindFrom"/> in degrees (0 = from north).</summary>
public sealed record WeatherNow(
    double Temperature,
    double FeelsLike,
    int Humidity,
    int Code,
    bool IsDay,
    double WindKmh,
    double WindFrom,
    double? GustKmh,
    double Uv);

/// <summary>One forecast step (an hour, or six hours further out). <see cref="PrecipitationChance"/> is -1 when the source has none.</summary>
public sealed record WeatherHour(DateTime Time, double Temperature, int PrecipitationChance, double Precipitation, int Code, bool IsDay);

/// <summary>A forecast day. <see cref="PrecipitationChance"/> is -1 when the source has none.</summary>
public sealed record WeatherDay(DateTime Date, int Code, double Low, double High, int PrecipitationChance, double PrecipitationSum, double UvMax);

/// <summary>Precipitation in one quarter hour from <see cref="Start"/>, for "rain in 20 min".</summary>
public sealed record WeatherQuarter(DateTime Start, double Precipitation);

/// <summary>Everything the weather tab shows, in the PC's local time and WMO weather codes, whatever the source.</summary>
public sealed class WeatherForecast
{
    public required string Place { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required DateTime FetchedAt { get; init; }
    public required string Source { get; init; }
    public required WeatherNow Now { get; init; }
    /// <summary>Ordered by time; hourly near term, possibly sparser later.</summary>
    public required IReadOnlyList<WeatherHour> Hours { get; init; }
    public required IReadOnlyList<WeatherDay> Days { get; init; }
    /// <summary>Empty when the source has no quarter-hour nowcast.</summary>
    public IReadOnlyList<WeatherQuarter> Quarters { get; init; } = Array.Empty<WeatherQuarter>();
}

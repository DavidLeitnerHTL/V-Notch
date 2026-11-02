using System;

namespace VNotch.Services;

/// <summary>
/// Sunrise and sunset from the sunrise equation (about a minute off), so the weather tab needs no extra
/// request and works with every forecast source.
/// </summary>
public static class SunTimes
{
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Local sunrise and sunset on <paramref name="localDate"/>; null on polar days and nights.</summary>
    public static (DateTime Rise, DateTime Set)? For(DateTime localDate, double latitude, double longitude)
    {
        int n = (localDate.Date - J2000.Date).Days;
        double jStar = n - longitude / 360.0;
        double m = Normalize(357.5291 + 0.98560028 * jStar);
        double mRad = Rad(m);
        double c = 1.9148 * Math.Sin(mRad) + 0.0200 * Math.Sin(2 * mRad) + 0.0003 * Math.Sin(3 * mRad);
        double lambda = Rad(Normalize(m + c + 180 + 102.9372));
        double transit = jStar + 0.0053 * Math.Sin(mRad) - 0.0069 * Math.Sin(2 * lambda);
        double sinDecl = Math.Sin(lambda) * Math.Sin(Rad(23.4397));
        double cosDecl = Math.Cos(Math.Asin(sinDecl));
        double phi = Rad(latitude);
        double cosOmega = (Math.Sin(Rad(-0.833)) - Math.Sin(phi) * sinDecl) / (Math.Cos(phi) * cosDecl);
        if (cosOmega < -1 || cosOmega > 1) return null;
        double omega = Math.Acos(cosOmega) * 180 / Math.PI;
        DateTime rise = J2000.AddDays(transit - omega / 360.0).ToLocalTime();
        DateTime set = J2000.AddDays(transit + omega / 360.0).ToLocalTime();
        return (rise, set);
    }

    /// <summary>True between sunrise and sunset at that local time (polar summer counts as day).</summary>
    public static bool IsDay(DateTime localTime, double latitude, double longitude)
    {
        var sun = For(localTime, latitude, longitude);
        if (sun is not { } s)
        {
            // No sunrise: midsummer in the north and midwinter in the south are day, the rest night.
            bool northernSummer = localTime.Month is >= 4 and <= 9;
            return latitude >= 0 ? northernSummer : !northernSummer;
        }
        return localTime >= s.Rise && localTime < s.Set;
    }

    private static double Rad(double degrees) => degrees * Math.PI / 180.0;

    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }
}

using System;
using System.Runtime.InteropServices;

namespace DHSIntegrationAgent.Application.Helpers;

public static class DateTimeHelper
{
    public static TimeZoneInfo GetKsaTimeZone()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }

        // Fallback for unexpected platforms
        return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
    }

    public static DateTime GetKSADateTime()
    {
        TimeZoneInfo ksaTimeZone = GetKsaTimeZone();
        DateTimeOffset original = DateTimeOffset.UtcNow;
        DateTimeOffset ksaTime = TimeZoneInfo.ConvertTime(original, ksaTimeZone);
        return ksaTime.DateTime;
    }

    public static DateTime ConvertToKsaTime(DateTime utcDateTime)
    {
        // Ensure the input is treated as UTC
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
        {
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        }
        else if (utcDateTime.Kind == DateTimeKind.Local)
        {
            utcDateTime = utcDateTime.ToUniversalTime();
        }

        TimeZoneInfo ksaTimeZone = GetKsaTimeZone();
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, ksaTimeZone);
    }
}

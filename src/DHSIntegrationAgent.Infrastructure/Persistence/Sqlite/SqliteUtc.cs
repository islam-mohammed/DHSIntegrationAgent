namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal static class SqliteUtc
{
    public static string ToIso(DateTimeOffset utc) => utc.UtcDateTime.ToString("O");

    public static DateTimeOffset FromIso(string iso)
        => DateTimeOffset.Parse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind);
}

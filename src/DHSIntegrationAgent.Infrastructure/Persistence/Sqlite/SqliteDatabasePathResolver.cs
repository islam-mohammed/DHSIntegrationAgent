using System.IO;
using DHSIntegrationAgent.Application.Configuration;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal static class SqliteDatabasePathResolver
{
    /// <summary>
    /// If AppOptions.DatabasePath is empty, use LocalAppData\DHSIntegrationAgent\agent.db
    /// </summary>
    public static string Resolve(AppOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            // Allow relative paths in dev.
            return Path.GetFullPath(options.DatabasePath.Trim());
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "DHSIntegrationAgent", "agent.db");
    }
}

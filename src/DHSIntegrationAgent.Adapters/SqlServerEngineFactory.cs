using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace DHSIntegrationAgent.Adapters;

public sealed class SqlServerEngineFactory : IDbEngineFactory
{
    public bool SupportsEngine(string engineName)
    {
        return engineName.Equals("sqlserver", StringComparison.OrdinalIgnoreCase)
               || engineName.Equals("mssql", StringComparison.OrdinalIgnoreCase)
               || engineName.Equals("sql_server", StringComparison.OrdinalIgnoreCase)
               || engineName.Equals("microsoft.sqlserver", StringComparison.OrdinalIgnoreCase);
    }

    public DbConnection CreateConnection(string connectionString)
    {
        // Enforce secure defaults without breaking provider-specific settings.
        var builder = new SqlConnectionStringBuilder(connectionString);

        // Security hardening defaults (can be overridden explicitly in stored connection string).
        if (!builder.ContainsKey("Encrypt"))
            builder.Encrypt = true;
        if (!builder.ContainsKey("TrustServerCertificate"))
            builder.TrustServerCertificate = false;

        // Add an app name for SQL auditing/diagnostics (no PHI).
        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
            builder.ApplicationName = "DHSIntegrationAgent";

        // Defensive: make sure pooling stays enabled by default.
        if (!builder.ContainsKey("Pooling"))
            builder.Pooling = true;

        // NOTE: Do NOT log builder.ConnectionString (contains secrets).
        return new SqlConnection(builder.ConnectionString);
    }

    public string BuildPagedQuery(string headerTable, string claimKeyCol, string dateCol)
    {
        return $@"
SELECT TOP (@PageSize) {claimKeyCol}
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (@LastSeen IS NULL OR {claimKeyCol} > @LastSeen)
ORDER BY {claimKeyCol} ASC;";
    }
}

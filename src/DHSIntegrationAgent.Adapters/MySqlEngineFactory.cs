using System;
using System.Data.Common;

namespace DHSIntegrationAgent.Adapters;

public sealed class MySqlEngineFactory : IDbEngineFactory
{
    public bool SupportsEngine(string engineName)
    {
        return engineName.Equals("mysql", StringComparison.OrdinalIgnoreCase);
    }

    public DbConnection CreateConnection(string connectionString)
    {
        throw new NotImplementedException("MySQL provider is a placeholder.");
    }

    public string BuildPagedQuery(string headerTable, string claimKeyCol, string dateCol)
    {
        return $@"
SELECT {claimKeyCol}
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (@LastSeen IS NULL OR {claimKeyCol} > @LastSeen)
ORDER BY {claimKeyCol} ASC
LIMIT @PageSize;";
    }
}

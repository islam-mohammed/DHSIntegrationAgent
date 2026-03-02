using System;
using System.Data.Common;

namespace DHSIntegrationAgent.Adapters;

public sealed class OracleEngineFactory : IDbEngineFactory
{
    public bool SupportsEngine(string engineName)
    {
        return engineName.Equals("oracle", StringComparison.OrdinalIgnoreCase);
    }

    public DbConnection CreateConnection(string connectionString)
    {
        throw new NotImplementedException("Oracle provider is a placeholder.");
    }

    public string BuildPagedQuery(string headerTable, string claimKeyCol, string dateCol)
    {
        // Oracle 12c+ FETCH FIRST syntax, but throws so this won't be reached
        return $@"
SELECT {claimKeyCol}
FROM {headerTable}
WHERE CompanyCode = @CompanyCode
  AND {dateCol} >= @StartDate
  AND {dateCol} <= @EndDate
  AND (@LastSeen IS NULL OR {claimKeyCol} > @LastSeen)
ORDER BY {claimKeyCol} ASC
FETCH FIRST @PageSize ROWS ONLY;";
    }
}

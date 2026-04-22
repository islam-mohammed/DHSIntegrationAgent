namespace DHSIntegrationAgent.Sync.Sql;

public interface ISqlDialect
{
    // Parameter placeholder: "@Name" (SQL Server / MySQL) or ":Name" (Oracle).
    string Param(string name);

    // Keyset-pagination WHERE fragment: "AND [keyCol] > @LastSeen"
    string KeysetPage(string keyColumn);

    // Row-limit clause: SQL Server returns ("TOP (@PageSize)", ""),
    // Oracle/MySQL return ("", "FETCH FIRST :PageSize ROWS ONLY" / "LIMIT @PageSize").
    (string prefix, string suffix) RowLimitClause();

    // Identifier quoting: [col] / "col" / `col`
    string Quote(string identifier);

    // Current-timestamp function: GETDATE() / SYSDATE / NOW()
    string CurrentTimestamp();

    // Simple connectivity probe: "SELECT 1" (SQL Server/MySQL) or "SELECT 1 FROM DUAL" (Oracle).
    string ConnectivityCheckQuery();

    // Returns a query that produces COUNT(1) >= 1 when the table or view exists.
    // Schema and table are inlined (values come from trusted descriptor, not user input).
    string SourceExistenceQuery(string schema, string table);

    // Returns a query that produces one COLUMN_NAME row per physical column.
    // Schema and table are inlined.
    string ColumnMetadataQuery(string schema, string table);
}

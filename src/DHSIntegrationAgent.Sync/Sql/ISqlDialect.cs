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

    // Column metadata query for schema validation.
    string ColumnMetadataQuery(string schema, string table);
}

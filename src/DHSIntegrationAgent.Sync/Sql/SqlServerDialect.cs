namespace DHSIntegrationAgent.Sync.Sql;

public sealed class SqlServerDialect : ISqlDialect
{
    public string Param(string name) => $"@{name}";

    public string KeysetPage(string keyColumn) => $"AND {Quote(keyColumn)} > @LastSeen";

    public (string prefix, string suffix) RowLimitClause() => ("TOP (@PageSize)", "");

    public string Quote(string identifier)
    {
        if (identifier.Contains('.'))
        {
            var parts = identifier.Split('.');
            return string.Join(".", parts.Select(p => $"[{p.Trim('[', ']')}]"));
        }
        return $"[{identifier.Trim('[', ']')}]";
    }

    public string CurrentTimestamp() => "GETDATE()";

    public string ColumnMetadataQuery(string schema, string table)
        => $"""
            SELECT COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = '{schema}' AND TABLE_NAME = '{table}'
            """;
}

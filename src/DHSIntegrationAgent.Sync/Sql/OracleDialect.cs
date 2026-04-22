namespace DHSIntegrationAgent.Sync.Sql;

public sealed class OracleDialect : ISqlDialect
{
    public string Param(string name) => $":{name}";

    public string KeysetPage(string keyColumn) => $"AND {Quote(keyColumn)} > :LastSeen";

    // Oracle 12c+: FETCH FIRST n ROWS ONLY supports bind variables.
    public (string prefix, string suffix) RowLimitClause() => ("", "FETCH FIRST :PageSize ROWS ONLY");

    public string Quote(string identifier)
    {
        if (identifier.Contains('.'))
        {
            var parts = identifier.Split('.');
            return string.Join(".", parts.Select(p => $"\"{p.Trim('"')}\""));
        }
        return $"\"{identifier.Trim('"')}\"";
    }

    public string CurrentTimestamp() => "SYSDATE";

    public string ConnectivityCheckQuery() => "SELECT 1 FROM DUAL";

    // ALL_OBJECTS covers both TABLE and VIEW; UPPER() ensures case-insensitive match.
    public string SourceExistenceQuery(string schema, string table)
        => $"""
            SELECT COUNT(1) FROM ALL_OBJECTS
            WHERE OWNER = UPPER('{schema}') AND OBJECT_NAME = UPPER('{table}')
              AND OBJECT_TYPE IN ('TABLE', 'VIEW')
            """;

    // ALL_TAB_COLUMNS returns COLUMN_NAME in uppercase by default.
    public string ColumnMetadataQuery(string schema, string table)
        => $"""
            SELECT COLUMN_NAME FROM ALL_TAB_COLUMNS
            WHERE OWNER = UPPER('{schema}') AND TABLE_NAME = UPPER('{table}')
            """;
}

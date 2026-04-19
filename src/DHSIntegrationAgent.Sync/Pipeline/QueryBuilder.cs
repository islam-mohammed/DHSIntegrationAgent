using DHSIntegrationAgent.Sync.Descriptor;

namespace DHSIntegrationAgent.Sync.Pipeline;

public static class QueryBuilder
{
    public static string BuildSelectBatch(TableDefinition table, VendorSource source)
    {
        // Builds: SELECT * FROM [TableName] WHERE CompanyCode = @CompanyCode AND DateColumn >= @StartDate AND DateColumn <= @EndDate
        // Note: For real Oracle vs SqlServer dialect differences, we would inject an ISqlDialect here.
        var dateCol = Sanitize(source.DateColumn);
        var companyCol = Sanitize(source.CompanyCodeColumn);
        var tableName = Sanitize(table.Name);

        return $@"
            SELECT *
            FROM {tableName}
            WHERE {companyCol} = @CompanyCode
              AND {dateCol} >= @StartDate
              AND {dateCol} <= @EndDate";
    }

    public static string BuildSelectChildBatch(TableDefinition childTable, TableDefinition parentTable, VendorSource source)
    {
        var dateCol = Sanitize(source.DateColumn);
        var companyCol = Sanitize(source.CompanyCodeColumn);
        var childTableName = Sanitize(childTable.Name);
        var parentTableName = Sanitize(parentTable.Name);
        var childKey = Sanitize(childTable.ParentKeyColumn!);
        var parentKey = Sanitize(parentTable.KeyColumn!);

        return $@"
            SELECT c.*
            FROM {childTableName} c
            INNER JOIN {parentTableName} p ON c.{childKey} = p.{parentKey}
            WHERE p.{companyCol} = @CompanyCode
              AND p.{dateCol} >= @StartDate
              AND p.{dateCol} <= @EndDate";
    }

    private static string Sanitize(string identifier)
    {
        // Basic sanitization. In a full implementation, validate against a regex: ^[a-zA-Z0-9_\[\]]+$
        if (string.IsNullOrWhiteSpace(identifier)) return identifier;
        return identifier.Replace(";", "").Replace("--", "");
    }
}

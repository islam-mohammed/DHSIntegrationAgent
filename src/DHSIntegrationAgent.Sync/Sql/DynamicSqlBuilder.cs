using DHSIntegrationAgent.Sync.Mapper;

namespace DHSIntegrationAgent.Sync.Sql;

// Stateless singleton — all state is passed via parameters.
public sealed class DynamicSqlBuilder
{
    // Builds SELECT {cols} FROM {source} WHERE {keyCol} IN (@p0, @p1, …)
    // manifest key = canonical name, value = ColumnFieldDescriptor (null entry or null Source = skip).
    public string BuildEntityQuery(
        string sourceName,
        IReadOnlyDictionary<string, ColumnFieldDescriptor?> manifest,
        string keyColumn,
        IReadOnlyList<int> claimKeys,
        ISqlDialect dialect,
        bool distinct = false)
    {
        if (claimKeys.Count == 0)
            throw new ArgumentException("claimKeys cannot be empty.", nameof(claimKeys));

        var selectCols = manifest
            .Where(kv => kv.Value?.Source is not null)
            .Select(kv => $"{dialect.Quote(kv.Value!.Source!)} AS {dialect.Quote(kv.Key)}")
            .ToList();

        if (selectCols.Count == 0)
            throw new InvalidOperationException($"No selectable columns in manifest for source '{sourceName}'.");

        var distinctClause = distinct ? "DISTINCT " : "";
        var colList   = string.Join(", ", selectCols);
        var src       = dialect.Quote(sourceName);
        var key       = dialect.Quote(keyColumn);
        var paramList = string.Join(", ", claimKeys.Select((_, i) => dialect.Param($"p{i}")));

        return $"SELECT {distinctClause}{colList} FROM {src} WHERE {key} IN ({paramList})";
    }

    // Builds a keyset-paginated SELECT of claim keys from the header source.
    // Caller must bind: @PageSize, @CompanyCode, @StartDate, @EndDate, @LastSeen (nullable int).
    public string BuildPagedHeaderQuery(
        string sourceName,
        string keyColumn,
        string dateColumn,
        string companyCodeColumn,
        ISqlDialect dialect)
    {
        var (prefix, suffix) = dialect.RowLimitClause();
        var src      = dialect.Quote(sourceName);
        var key      = dialect.Quote(keyColumn);
        var date     = dialect.Quote(dateColumn);
        var comp     = dialect.Quote(companyCodeColumn);
        var prefixPart = string.IsNullOrEmpty(prefix) ? "" : $"{prefix} ";
        var suffixPart = string.IsNullOrEmpty(suffix) ? "" : $"\n{suffix}";

        return $"""
            SELECT {prefixPart}{key}
            FROM {src}
            WHERE {comp} = {dialect.Param("CompanyCode")}
              AND {date} >= {dialect.Param("StartDate")}
              AND {date} <= {dialect.Param("EndDate")}
              AND ({dialect.Param("LastSeen")} IS NULL OR {key} > {dialect.Param("LastSeen")})
            ORDER BY {key} ASC{suffixPart}
            """.Trim();
    }

    // Builds SELECT COUNT(1) for batch sizing.
    // Caller must bind: @CompanyCode, @StartDate, @EndDate.
    public string BuildCountQuery(
        string sourceName,
        string dateColumn,
        string companyCodeColumn,
        ISqlDialect dialect)
    {
        var src  = dialect.Quote(sourceName);
        var date = dialect.Quote(dateColumn);
        var comp = dialect.Quote(companyCodeColumn);

        return $"""
            SELECT COUNT(1)
            FROM {src}
            WHERE {comp} = {dialect.Param("CompanyCode")}
              AND {date} >= {dialect.Param("StartDate")}
              AND {date} <= {dialect.Param("EndDate")}
            """.Trim();
    }
}

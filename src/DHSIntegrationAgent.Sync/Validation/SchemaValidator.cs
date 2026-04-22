using System.Data.Common;
using DHSIntegrationAgent.Sync.Mapper;
using DHSIntegrationAgent.Sync.Pipeline;
using DHSIntegrationAgent.Sync.Sql;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Sync.Validation;

public sealed class SchemaValidator
{
    private readonly ILogger<SchemaValidator> _logger;

    public SchemaValidator(ILogger<SchemaValidator> logger)
    {
        _logger = logger;
    }

    public async Task<SchemaValidationResult> ValidateAsync(
        DbConnection conn,
        VendorDescriptor descriptor,
        ISqlDialect dialect,
        CancellationToken ct)
    {
        var issues = new List<SchemaValidationIssue>();

        // DbConnectivity — vendor database must be reachable.
        if (!await CheckDbConnectivityAsync(conn, dialect, issues, ct)) return new SchemaValidationResult(false, issues);

        // SourceExists / ColumnExists — source tables and mapped columns must exist (topologies 1 & 2).
        if (descriptor.Topology is "tableToTable" or "viewToTable")
        {
            foreach (var (entity, sourceName) in descriptor.Sources)
            {
                await CheckSourceExistsAsync(conn, entity, sourceName, dialect, issues, ct);

                if (descriptor.ColumnManifests.TryGetValue(entity, out var manifest))
                    await CheckColumnExistsAsync(conn, entity, sourceName, manifest, dialect, issues, ct);
            }
        }

        var hasError = issues.Any(i => i.Severity == "Error");
        return new SchemaValidationResult(!hasError, issues);
    }

    private static async Task<bool> CheckDbConnectivityAsync(
        DbConnection conn, ISqlDialect dialect, List<SchemaValidationIssue> issues, CancellationToken ct)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = dialect.ConnectivityCheckQuery();
            cmd.CommandTimeout = 10;
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            issues.Add(new SchemaValidationIssue("DbConnectivity", "", "", "Error", $"Cannot reach vendor database: {ex.Message}"));
            return false;
        }
    }

    private static async Task CheckSourceExistsAsync(
        DbConnection conn,
        string entity,
        string sourceName,
        ISqlDialect dialect,
        List<SchemaValidationIssue> issues,
        CancellationToken ct)
    {
        var (schema, table) = ParseSourceName(sourceName);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = dialect.SourceExistenceQuery(schema, table);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (count == 0)
            {
                issues.Add(new SchemaValidationIssue(
                    "SourceExists", entity, "", "Error",
                    $"Source '{sourceName}' (entity '{entity}') does not exist."));
            }
        }
        catch (Exception ex)
        {
            issues.Add(new SchemaValidationIssue("SourceExists", entity, "", "Error",
                $"Failed to check source existence for '{sourceName}': {ex.Message}"));
        }
    }

    // Checks that every source column named in the descriptor exists in the physical table.
    // No type compatibility is checked here; type coercion happens at fetch time.
    private static async Task CheckColumnExistsAsync(
        DbConnection conn,
        string entity,
        string sourceName,
        Dictionary<string, ColumnFieldDescriptor?> manifest,
        ISqlDialect dialect,
        List<SchemaValidationIssue> issues,
        CancellationToken ct)
    {
        var (schema, table) = ParseSourceName(sourceName);

        HashSet<string> physicalColumns;
        try
        {
            physicalColumns = await GetPhysicalColumnNamesAsync(conn, schema, table, dialect, ct);
        }
        catch (Exception ex)
        {
            issues.Add(new SchemaValidationIssue("ColumnExists", entity, "", "Error",
                $"Failed to read column metadata for '{sourceName}': {ex.Message}"));
            return;
        }

        foreach (var (canonical, field) in manifest)
        {
            var sourceCol = field?.Source;
            if (sourceCol is null) continue;

            if (!physicalColumns.Contains(sourceCol.ToUpperInvariant()))
            {
                issues.Add(new SchemaValidationIssue("ColumnExists", entity, sourceCol, "Error",
                    $"Column '{sourceCol}' (canonical '{canonical}') not found in '{sourceName}'."));
            }
        }
    }

    private static async Task<HashSet<string>> GetPhysicalColumnNamesAsync(
        DbConnection conn, string schema, string table, ISqlDialect dialect, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = dialect.ColumnMetadataQuery(schema, table);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0).ToUpperInvariant());
        return result;
    }

    private static (string schema, string table) ParseSourceName(string sourceName)
    {
        var parts = sourceName.Split('.');
        return parts.Length >= 2 ? (parts[0], parts[1]) : ("dbo", parts[0]);
    }

}

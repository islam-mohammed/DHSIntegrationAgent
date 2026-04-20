using System.Data;
using System.Data.Common;
using DHSIntegrationAgent.Sync.Mapper;
using DHSIntegrationAgent.Sync.Pipeline;
using DHSIntegrationAgent.Sync.Sql;
using Microsoft.Extensions.Logging;

namespace DHSIntegrationAgent.Sync.Validation;

public sealed class SchemaValidator
{
    private readonly TypeCoercionMap _typeMap;
    private readonly ILogger<SchemaValidator> _logger;

    public SchemaValidator(TypeCoercionMap typeMap, ILogger<SchemaValidator> logger)
    {
        _typeMap = typeMap;
        _logger = logger;
    }

    public async Task<SchemaValidationResult> ValidateAsync(
        DbConnection conn,
        VendorDescriptor descriptor,
        ISqlDialect dialect,
        CancellationToken ct)
    {
        var issues = new List<SchemaValidationIssue>();

        // G1 — Descriptor version: major version must be 1.
        RunG1(descriptor, issues);

        // G2 — Engine reachability.
        if (!await RunG2Async(conn, issues, ct)) return new SchemaValidationResult(false, issues);

        // G3/G4/G5 — Source existence and column presence (topologies 1 & 2).
        if (descriptor.Topology is "tableToTable" or "viewToTable")
        {
            foreach (var (entity, sourceName) in descriptor.Sources)
            {
                await RunG3Async(conn, entity, sourceName, issues, ct);

                if (descriptor.ColumnManifests.TryGetValue(entity, out var manifest))
                    await RunG4G5Async(conn, entity, sourceName, manifest, issues, ct);
            }
        }

        var hasError = issues.Any(i => i.Severity == "Error");
        return new SchemaValidationResult(!hasError, issues);
    }

    private static void RunG1(VendorDescriptor descriptor, List<SchemaValidationIssue> issues)
    {
        var parts = descriptor.SchemaVersion.Split('.');
        if (!int.TryParse(parts[0], out var major) || major != 1)
        {
            issues.Add(new SchemaValidationIssue(
                "G1", "", "", "Error",
                $"SchemaVersion '{descriptor.SchemaVersion}' has unsupported major version. Expected 1.x.x."));
        }
    }

    private static async Task<bool> RunG2Async(DbConnection conn, List<SchemaValidationIssue> issues, CancellationToken ct)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 10;
            await cmd.ExecuteScalarAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            issues.Add(new SchemaValidationIssue("G2", "", "", "Error", $"Cannot reach vendor database: {ex.Message}"));
            return false;
        }
    }

    private static async Task RunG3Async(
        DbConnection conn,
        string entity,
        string sourceName,
        List<SchemaValidationIssue> issues,
        CancellationToken ct)
    {
        var (schema, table) = ParseSourceName(sourceName);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 30;
            cmd.CommandText = """
                SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                """;
            AddParam(cmd, "@schema", schema);
            AddParam(cmd, "@table", table);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (count == 0)
            {
                issues.Add(new SchemaValidationIssue(
                    "G3", entity, "", "Error",
                    $"Source '{sourceName}' (entity '{entity}') does not exist."));
            }
        }
        catch (Exception ex)
        {
            issues.Add(new SchemaValidationIssue("G3", entity, "", "Error",
                $"Failed to check source existence for '{sourceName}': {ex.Message}"));
        }
    }

    private async Task RunG4G5Async(
        DbConnection conn,
        string entity,
        string sourceName,
        Dictionary<string, ColumnFieldDescriptor?> manifest,
        List<SchemaValidationIssue> issues,
        CancellationToken ct)
    {
        var (schema, table) = ParseSourceName(sourceName);

        Dictionary<string, string> physicalColumns;
        try
        {
            physicalColumns = await GetPhysicalColumnsAsync(conn, schema, table, ct);
        }
        catch (Exception ex)
        {
            issues.Add(new SchemaValidationIssue("G4", entity, "", "Error",
                $"Failed to read column metadata for '{sourceName}': {ex.Message}"));
            return;
        }

        foreach (var (canonical, field) in manifest)
        {
            var sourceCol = field?.Source;
            if (sourceCol is null) continue; // null entry or null source = skip column

            // G4 — Column presence.
            if (!physicalColumns.ContainsKey(sourceCol.ToUpperInvariant()))
            {
                issues.Add(new SchemaValidationIssue("G4", entity, sourceCol, "Error",
                    $"Column '{sourceCol}' (canonical '{canonical}') not found in '{sourceName}'."));
                continue;
            }

            // G5 — Type compatibility (warning only).
            // Descriptor-declared type takes precedence over TypeCoercionMap.
            var expectedClrType = field?.ClrType ?? _typeMap.GetTargetType(canonical);
            if (expectedClrType is not null)
            {
                var sqlType = physicalColumns[sourceCol.ToUpperInvariant()];
                if (!IsCompatible(sqlType, expectedClrType))
                {
                    issues.Add(new SchemaValidationIssue("G5", entity, sourceCol, "Warning",
                        $"Column '{sourceCol}' has SQL type '{sqlType}' which may not coerce cleanly to '{expectedClrType.Name}'."));
                }
            }
        }
    }

    private static async Task<Dictionary<string, string>> GetPhysicalColumnsAsync(
        DbConnection conn, string schema, string table, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = """
            SELECT COLUMN_NAME, DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            """;
        AddParam(cmd, "@schema", schema);
        AddParam(cmd, "@table", table);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result[reader.GetString(0).ToUpperInvariant()] = reader.GetString(1);
        return result;
    }

    private static bool IsCompatible(string sqlType, Type clrType)
    {
        var t = sqlType.ToLowerInvariant();
        if (clrType == typeof(int))      return t is "int" or "smallint" or "tinyint" or "bigint";
        if (clrType == typeof(decimal))  return t is "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real";
        if (clrType == typeof(DateTime)) return t is "datetime" or "datetime2" or "date" or "smalldatetime";
        if (clrType == typeof(bool))     return t is "bit";
        if (clrType == typeof(string))   return true; // any type can be represented as string
        return true;
    }

    private static (string schema, string table) ParseSourceName(string sourceName)
    {
        var parts = sourceName.Split('.');
        return parts.Length >= 2 ? (parts[0], parts[1]) : ("dbo", parts[0]);
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}

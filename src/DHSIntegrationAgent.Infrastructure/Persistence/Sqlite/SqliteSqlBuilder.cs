using System.Data.Common;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal static class SqliteSqlBuilder
{
    public static DbParameter AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    public static string AddIntInClause(DbCommand cmd, string prefix, IReadOnlyList<int> values)
    {
        if (values.Count == 0) throw new ArgumentException("IN clause requires at least one value.", nameof(values));

        var names = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var name = $"${prefix}{i}";
            names.Add(name);
            AddParam(cmd, name, values[i]);
        }

        return $"({string.Join(",", names)})";
    }

    /// <summary>
    /// Adds a CTE like:
    /// WITH keys(ProviderDhsCode, ProIdClaim) AS (VALUES ($p0,$id0),($p1,$id1)...)
    /// Returns the CTE SQL (WITH ...).
    /// </summary>
    public static string AddClaimKeysCte(DbCommand cmd, string cteName, IReadOnlyList<DHSIntegrationAgent.Application.Persistence.Repositories.ClaimKey> keys)
    {
        if (keys.Count == 0) throw new ArgumentException("keys cannot be empty", nameof(keys));

        var values = new List<string>(keys.Count);
        for (var i = 0; i < keys.Count; i++)
        {
            var p = $"$p{i}";
            var id = $"$id{i}";
            values.Add($"({p},{id})");
            AddParam(cmd, p, keys[i].ProviderDhsCode);
            AddParam(cmd, id, keys[i].ProIdClaim);
        }

        return $"WITH {cteName}(ProviderDhsCode, ProIdClaim) AS (VALUES {string.Join(",", values)})";
    }
}

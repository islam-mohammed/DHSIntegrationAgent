namespace DHSIntegrationAgent.Sync.Sql;

public sealed class SqlDialectFactory : ISqlDialectFactory
{
    private static readonly SqlServerDialect _sqlServer = new();

    public ISqlDialect ForEngine(string dbEngine)
        => dbEngine.ToLowerInvariant() switch
        {
            "sqlserver" => _sqlServer,
            _ => throw new NotSupportedException($"Database engine '{dbEngine}' is not supported in this release.")
        };
}

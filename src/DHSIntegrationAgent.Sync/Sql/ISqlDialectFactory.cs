namespace DHSIntegrationAgent.Sync.Sql;

public interface ISqlDialectFactory
{
    ISqlDialect ForEngine(string dbEngine);
}

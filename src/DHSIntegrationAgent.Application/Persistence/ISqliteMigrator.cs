namespace DHSIntegrationAgent.Application.Persistence;

public interface ISqliteMigrator
{
    int CurrentSchemaVersion { get; }

    /// <summary>
    /// Creates or upgrades the SQLite schema to CurrentSchemaVersion.
    /// </summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}

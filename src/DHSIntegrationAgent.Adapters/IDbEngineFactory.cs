using System.Data.Common;

namespace DHSIntegrationAgent.Adapters;

/// <summary>
/// Abstraction for different database engines to handle connection creation and SQL dialects.
/// </summary>
public interface IDbEngineFactory
{
    /// <summary>
    /// Returns true if this factory supports the given engine name.
    /// </summary>
    bool SupportsEngine(string engineName);

    /// <summary>
    /// Creates and configures a DbConnection for the engine, applying secure defaults.
    /// </summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Builds a paged query for listing claim keys.
    /// </summary>
    string BuildPagedQuery(string headerTable, string claimKeyCol, string dateCol);
}

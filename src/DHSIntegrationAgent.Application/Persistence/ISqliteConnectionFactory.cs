using System.Data.Common;

namespace DHSIntegrationAgent.Application.Persistence;

public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }

    /// <summary>
    /// Returns an OPEN connection configured for this app.
    /// Caller owns disposal.
    /// </summary>
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}

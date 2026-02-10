namespace DHSIntegrationAgent.Application.Persistence;

/// <summary>
/// Ensures SQLite file + required tables exist before UI routing begins.
/// </summary>
public interface IDatabaseInitializer
{
    Task EnsureCreatedAsync(CancellationToken ct);
}

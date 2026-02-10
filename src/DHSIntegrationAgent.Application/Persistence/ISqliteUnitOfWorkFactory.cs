namespace DHSIntegrationAgent.Application.Persistence;

public interface ISqliteUnitOfWorkFactory
{
    Task<ISqliteUnitOfWork> CreateAsync(CancellationToken cancellationToken);
}

using DHSIntegrationAgent.Application.Abstractions;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

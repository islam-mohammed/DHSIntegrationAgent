namespace DHSIntegrationAgent.Application.Configuration;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

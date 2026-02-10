namespace DHSIntegrationAgent.Application.Abstractions;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

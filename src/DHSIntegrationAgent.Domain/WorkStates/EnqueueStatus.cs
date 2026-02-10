namespace DHSIntegrationAgent.Domain.WorkStates;

public enum EnqueueStatus
{
    NotSent = 0,
    InFlight = 1,
    Enqueued = 2,
    Failed = 3
}

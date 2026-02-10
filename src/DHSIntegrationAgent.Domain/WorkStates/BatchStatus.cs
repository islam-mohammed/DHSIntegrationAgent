namespace DHSIntegrationAgent.Domain.WorkStates;

public enum BatchStatus
{
    Draft = 0,
    Ready = 1,
    Sending = 2,
    Enqueued = 3,
    HasResume = 4,
    Completed = 5,
    Failed = 6
}

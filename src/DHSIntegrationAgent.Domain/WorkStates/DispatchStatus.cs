namespace DHSIntegrationAgent.Domain.WorkStates;

public enum DispatchStatus
{
    Ready = 0,
    InFlight = 1,
    Succeeded = 2,
    Failed = 3,
    PartiallySucceeded = 4
}

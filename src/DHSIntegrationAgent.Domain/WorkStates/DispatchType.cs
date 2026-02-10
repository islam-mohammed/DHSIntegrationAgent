namespace DHSIntegrationAgent.Domain.WorkStates;

public enum DispatchType
{
    NormalSend = 0,
    RetrySend = 1,
    RequeueIncomplete = 2
}

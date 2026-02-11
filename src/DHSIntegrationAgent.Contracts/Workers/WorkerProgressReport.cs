namespace DHSIntegrationAgent.Contracts.Workers;

public record WorkerProgressReport(
    string WorkerId,
    string Message,
    double? Percentage = null,
    bool IsError = false);

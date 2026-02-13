namespace DHSIntegrationAgent.Contracts.Workers;

/// <summary>
/// Progress report from a background worker.
/// </summary>
public record WorkerProgressReport(
    string WorkerId,
    string Message,
    double? Percentage = null,
    bool IsError = false,
    long? BatchId = null,
    int? ProcessedCount = null,
    int? TotalCount = null,
    string? BcrId = null,
    string? FinancialMessage = null);

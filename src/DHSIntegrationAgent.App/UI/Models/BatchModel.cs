namespace DHSIntegrationAgent.App.UI.Models;

/// <summary>
/// Represents a batch entity with all relevant information for display in the Batch List.
/// </summary>
public class BatchModel
{
    public string PayerName { get; set; } = string.Empty;
    public string HISCode { get; set; } = string.Empty;
    public int TotalClaims { get; set; }
    public DateTime BatchDate { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime CreationDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

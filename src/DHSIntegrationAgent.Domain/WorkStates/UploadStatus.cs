namespace DHSIntegrationAgent.Domain.WorkStates;

public enum UploadStatus
{
    NotStaged = 0,
    Staged = 1,
    Uploading = 2,
    Uploaded = 3,
    Failed = 4
}

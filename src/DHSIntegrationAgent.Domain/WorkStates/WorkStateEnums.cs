namespace DHSIntegrationAgent.Domain.WorkStates;

// 4.1 EnqueueStatus (Claim.EnqueueStatus)
public enum EnqueueStatus
{
    NotSent = 0,
    InFlight = 1,
    Enqueued = 2,
    Failed = 3
}

// 4.2 CompletionStatus (Claim.CompletionStatus)
public enum CompletionStatus
{
    Unknown = 0,
    Completed = 1
}

// 4.3 BatchStatus (Batch.BatchStatus)
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

// 4.4 MappingStatus (DomainMapping.MappingStatus)
public enum MappingStatus
{
    Missing = 0,
    Posted = 1,
    Approved = 2,
    PostFailed = 3
}

// 4.5 DispatchType (Dispatch.DispatchType)
public enum DispatchType
{
    NormalSend = 0,
    RetrySend = 1,
    RequeueIncomplete = 2
}

// 4.6 DispatchStatus (Dispatch.DispatchStatus)
public enum DispatchStatus
{
    Ready = 0,
    InFlight = 1,
    Succeeded = 2,
    Failed = 3,
    PartiallySucceeded = 4
}

// DispatchItem.ItemResult (optional enum mentioned in spec)
public enum DispatchItemResult
{
    Unknown = 0,
    Success = 1,
    Fail = 2
}

// 4.7 UploadStatus (Attachment.UploadStatus)
public enum UploadStatus
{
    NotStaged = 0,
    Staged = 1,
    Uploading = 2,
    Uploaded = 3,
    Failed = 4
}

// 4.8 AttachmentSourceType (Attachment.AttachmentSourceType)
public enum AttachmentSourceType
{
    FilePath = 0,
    RawBytesInLocation = 1,
    Base64InAttachBit = 2
}

using System.Collections.Generic;

namespace DHSIntegrationAgent.Infrastructure.Persistence.Sqlite;

internal static class SqliteMigrations
{
    internal sealed record Migration(int Version, string Name, IReadOnlyList<string> Statements);

    /// <summary>
    /// Current consolidated schema version.
    /// </summary>
    public static readonly int CurrentSchemaVersion = 2;

    public static IReadOnlyList<Migration> All { get; } = new[]
    {
        new Migration(1, "001_InitialSchema_v1", BuildV1()),
        new Migration(2, "002_SeparateDomainMappings", BuildV2())
    };

    /// <summary>
    /// Migration 2: Separate DomainMapping into ApprovedDomainMapping and MissingDomainMapping.
    /// Adds DiscoverySource flag to MissingDomainMapping.
    /// </summary>
    private static IReadOnlyList<string> BuildV2() => new List<string>
    {
        """
        CREATE TABLE ApprovedDomainMapping (
            DomainMappingId INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode  TEXT NOT NULL,
            CompanyCode      TEXT NOT NULL,
            DomainName       TEXT NOT NULL,
            DomainTableId    INTEGER NOT NULL,
            SourceValue      TEXT NOT NULL,
            TargetValue      TEXT NOT NULL,
            DiscoveredUtc    TEXT NOT NULL,
            LastPostedUtc    TEXT NULL,
            LastUpdatedUtc   TEXT NOT NULL,
            Notes            TEXT NULL
        );
        """,
        "CREATE UNIQUE INDEX UX_ApprovedDomainMapping_Key ON ApprovedDomainMapping(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue);",

        """
        CREATE TABLE MissingDomainMapping (
            MissingMappingId INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode  TEXT NOT NULL,
            CompanyCode      TEXT NOT NULL,
            DomainName       TEXT NOT NULL,
            DomainTableId    INTEGER NOT NULL,
            SourceValue      TEXT NOT NULL,
            DiscoverySource  INTEGER NOT NULL, -- 0=Api, 1=Scanned
            DiscoveredUtc    TEXT NOT NULL,
            LastUpdatedUtc   TEXT NOT NULL,
            Notes            TEXT NULL
        );
        """,
        "CREATE UNIQUE INDEX UX_MissingDomainMapping_Key ON MissingDomainMapping(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue);",

        // Migrate existing data if table exists
        """
        INSERT INTO ApprovedDomainMapping (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue, TargetValue, DiscoveredUtc, LastPostedUtc, LastUpdatedUtc, Notes)
        SELECT ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue, COALESCE(TargetValue, ''), DiscoveredUtc, LastPostedUtc, LastUpdatedUtc, Notes
        FROM DomainMapping WHERE MappingStatus = 2;
        """,

        """
        INSERT INTO MissingDomainMapping (ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue, DiscoverySource, DiscoveredUtc, LastUpdatedUtc, Notes)
        SELECT ProviderDhsCode, CompanyCode, DomainName, DomainTableId, SourceValue, 0, DiscoveredUtc, LastUpdatedUtc, Notes
        FROM DomainMapping WHERE MappingStatus = 0;
        """,

        "DROP TABLE DomainMapping;"
    };

    /// <summary>
    /// Creates the latest schema directly (merged from historical migrations 1..5).
    /// </summary>
    private static IReadOnlyList<string> BuildV1() => new List<string>
    {
        // -----------------------
        // 5.1 AppMeta (singleton)
        // -----------------------
        """
        CREATE TABLE AppMeta (
            Id                   INTEGER PRIMARY KEY CHECK (Id = 1),
            SchemaVersion        INTEGER NOT NULL,
            CreatedUtc           TEXT NOT NULL,
            LastOpenedUtc        TEXT NOT NULL,
            ClientInstanceId     TEXT NOT NULL,
            ActiveKeyId          TEXT NULL,
            ActiveKeyCreatedUtc  TEXT NULL,
            ActiveKeyDpapiBlob   BLOB NULL,
            PreviousKeyId        TEXT NULL,
            PreviousKeyCreatedUtc TEXT NULL,
            PreviousKeyDpapiBlob BLOB NULL
        );
        """,

        // ----------------------------
        // 5.2 AppSettings (singleton)
        // ----------------------------
        """
        CREATE TABLE AppSettings (
            Id                        INTEGER PRIMARY KEY CHECK (Id = 1),
            GroupID                   TEXT NULL,
            ProviderDhsCode           TEXT NULL,
            ConfigCacheTtlMinutes      INTEGER NOT NULL DEFAULT 1440,
            FetchIntervalMinutes       INTEGER NOT NULL DEFAULT 5,
            ManualRetryCooldownMinutes INTEGER NOT NULL DEFAULT 10,
            LeaseDurationSeconds       INTEGER NOT NULL DEFAULT 120,
            StreamAIntervalSeconds     INTEGER NOT NULL DEFAULT 900,
            ResumePollIntervalSeconds  INTEGER NOT NULL DEFAULT 300,
            ApiTimeoutSeconds          INTEGER NOT NULL DEFAULT 60,
            CreatedUtc                 TEXT NOT NULL,
            UpdatedUtc                 TEXT NOT NULL
        );
        """,

        // -----------------------
        // 5.3 ProviderProfile
        // -----------------------
        """
        CREATE TABLE ProviderProfile (
            ProviderCode              TEXT NOT NULL,
            ProviderDhsCode           TEXT NOT NULL,
            DbEngine                  TEXT NOT NULL,
            IntegrationType           TEXT NOT NULL,
            EncryptedConnectionString BLOB NOT NULL,
            EncryptionKeyId           TEXT NULL,
            IsActive                  INTEGER NOT NULL DEFAULT 1,
            CreatedUtc                TEXT NOT NULL,
            UpdatedUtc                TEXT NOT NULL,
            PRIMARY KEY (ProviderCode)
        );
        """,

        // -----------------------------
        // 5.4 ProviderExtractionConfig
        // -----------------------------
        """
        CREATE TABLE ProviderExtractionConfig (
            ProviderCode           TEXT PRIMARY KEY,
            ClaimKeyColumnName     TEXT NULL,
            HeaderSourceName       TEXT NULL,
            DetailsSourceName      TEXT NULL,
            CustomHeaderSql        TEXT NULL,
            CustomServiceSql       TEXT NULL,
            CustomDiagnosisSql     TEXT NULL,
            CustomLabSql           TEXT NULL,
            CustomRadiologySql     TEXT NULL,
            CustomOpticalSql       TEXT NULL,
            Notes                  TEXT NULL,
            UpdatedUtc             TEXT NOT NULL
        );
        """,

        // ----------------------------
        // 5.5 ProviderConfigCache
        // ----------------------------
        """
        CREATE TABLE ProviderConfigCache (
            ProviderDhsCode TEXT NOT NULL,
            ETag            TEXT NULL,
            ConfigJson      TEXT NOT NULL,
            FetchedUtc      TEXT NOT NULL,
            ExpiresUtc      TEXT NOT NULL,
            LastError       TEXT NULL,
            PRIMARY KEY (ProviderDhsCode)
        );
        """,

        // -----------------------
        // 5.6 PayerProfile
        // -----------------------
        """
        CREATE TABLE PayerProfile (
            PayerId         INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode  TEXT NOT NULL,
            CompanyCode      TEXT NOT NULL,
            PayerCode        TEXT NULL,
            PayerName        TEXT NULL,
            IsActive         INTEGER NOT NULL DEFAULT 1,
            CreatedUtc       TEXT NOT NULL,
            UpdatedUtc       TEXT NOT NULL
        );
        """,
        "CREATE UNIQUE INDEX UX_PayerProfile_Provider_Company ON PayerProfile(ProviderDhsCode, CompanyCode);",
        "CREATE INDEX IX_PayerProfile_Provider_IsActive ON PayerProfile(ProviderDhsCode, IsActive);",

        // -----------------------
        // 5.7 DomainMapping
        // -----------------------
        """
        CREATE TABLE DomainMapping (
            DomainMappingId INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode  TEXT NOT NULL,
            CompanyCode      TEXT NOT NULL,
            DomainName       TEXT NOT NULL,
            DomainTableId    INTEGER NOT NULL,
            SourceValue      TEXT NOT NULL,
            TargetValue      TEXT NULL,
            MappingStatus    INTEGER NOT NULL,
            DiscoveredUtc    TEXT NOT NULL,
            LastPostedUtc    TEXT NULL,
            LastUpdatedUtc   TEXT NOT NULL,
            Notes            TEXT NULL
        );
        """,
        "CREATE UNIQUE INDEX UX_DomainMapping_Key ON DomainMapping(ProviderDhsCode, CompanyCode, DomainTableId, SourceValue);",
        "CREATE INDEX IX_DomainMapping_Status ON DomainMapping(MappingStatus);",
        "CREATE INDEX IX_DomainMapping_DomainName ON DomainMapping(DomainName);",

        // -----------------------
        // 5.8 Batch
        // -----------------------
        """
        CREATE TABLE Batch (
            BatchId        INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode TEXT NOT NULL,
            CompanyCode     TEXT NOT NULL,
            PayerCode       TEXT NULL,
            MonthKey        TEXT NOT NULL, -- YYYYMM
            BcrId           TEXT NULL,
            BatchStatus     INTEGER NOT NULL,
            HasResume       INTEGER NOT NULL DEFAULT 0,
            CreatedUtc      TEXT NOT NULL,
            UpdatedUtc      TEXT NOT NULL,
            LastError       TEXT NULL
        );
        """,
        "CREATE UNIQUE INDEX UX_Batch_Provider_Company_Month ON Batch(ProviderDhsCode, CompanyCode, MonthKey);",
        "CREATE INDEX IX_Batch_Status ON Batch(BatchStatus);",
        "CREATE INDEX IX_Batch_BcrId ON Batch(BcrId);",

        // -----------------------
        // 5.9 Claim (composite PK)
        // -----------------------
        """
        CREATE TABLE Claim (
            ProviderDhsCode        TEXT NOT NULL,
            ProIdClaim             INTEGER NOT NULL,
            CompanyCode            TEXT NOT NULL,
            MonthKey               TEXT NOT NULL, -- YYYYMM
            BatchId                INTEGER NULL,
            BcrId                  TEXT NULL, -- cached
            EnqueueStatus          INTEGER NOT NULL,
            CompletionStatus       INTEGER NOT NULL,
            LockedBy               TEXT NULL,
            InFlightUntilUtc       TEXT NULL,
            AttemptCount           INTEGER NOT NULL DEFAULT 0,
            NextRetryUtc           TEXT NULL,
            LastError              TEXT NULL,
            LastResumeCheckUtc     TEXT NULL,
            RequeueAttemptCount    INTEGER NOT NULL DEFAULT 0,
            NextRequeueUtc         TEXT NULL,
            LastRequeueError       TEXT NULL,
            LastEnqueuedUtc        TEXT NULL,
            FirstSeenUtc           TEXT NOT NULL,
            LastUpdatedUtc         TEXT NOT NULL,
            PRIMARY KEY (ProviderDhsCode, ProIdClaim),
            FOREIGN KEY (BatchId) REFERENCES Batch(BatchId) ON DELETE SET NULL
        );
        """,
        "CREATE INDEX IX_Claim_Provider_Company_Month ON Claim(ProviderDhsCode, CompanyCode, MonthKey);",
        "CREATE INDEX IX_Claim_EnqueueStatus_NextRetryUtc ON Claim(EnqueueStatus, NextRetryUtc);",
        "CREATE INDEX IX_Claim_CompletionStatus ON Claim(CompletionStatus);",
        "CREATE INDEX IX_Claim_BatchId ON Claim(BatchId);",
        "CREATE INDEX IX_Claim_InFlightUntilUtc ON Claim(InFlightUntilUtc);",
        "CREATE INDEX IX_Claim_Enqueue_Completion_RequeueUtc ON Claim(EnqueueStatus, CompletionStatus, NextRequeueUtc);",

        // -----------------------
        // 5.10 ClaimPayload (PHI: encrypted blob)
        // -----------------------
        """
        CREATE TABLE ClaimPayload (
            ProviderDhsCode  TEXT NOT NULL,
            ProIdClaim       INTEGER NOT NULL,
            PayloadJson      BLOB NOT NULL, -- encrypted at rest (implemented in WBS 1.3)
            PayloadSha256    TEXT NOT NULL,
            PayloadVersion   INTEGER NOT NULL DEFAULT 1,
            CreatedUtc       TEXT NOT NULL,
            UpdatedUtc       TEXT NOT NULL,
            PRIMARY KEY (ProviderDhsCode, ProIdClaim),
            FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE
        );
        """,
        "CREATE INDEX IX_ClaimPayload_PayloadSha256 ON ClaimPayload(PayloadSha256);",

        // -----------------------
        // 5.11 Dispatch
        // -----------------------
        """
        CREATE TABLE Dispatch (
            DispatchId       TEXT PRIMARY KEY, -- UUID
            ProviderDhsCode   TEXT NOT NULL,
            BatchId           INTEGER NOT NULL,
            BcrId             TEXT NULL,
            SequenceNo        INTEGER NOT NULL,
            DispatchType      INTEGER NOT NULL,
            DispatchStatus    INTEGER NOT NULL,
            AttemptCount      INTEGER NOT NULL DEFAULT 0,
            NextRetryUtc      TEXT NULL,
            RequestSizeBytes  INTEGER NULL,
            RequestGzip       INTEGER NOT NULL DEFAULT 1,
            HttpStatusCode    INTEGER NULL,
            LastError         TEXT NULL,
            CorrelationId     TEXT NULL,
            CreatedUtc        TEXT NOT NULL,
            UpdatedUtc        TEXT NOT NULL,
            FOREIGN KEY (BatchId) REFERENCES Batch(BatchId) ON DELETE CASCADE
        );
        """,
        "CREATE UNIQUE INDEX UX_Dispatch_Batch_Sequence ON Dispatch(BatchId, SequenceNo);",
        "CREATE INDEX IX_Dispatch_Status_NextRetryUtc ON Dispatch(DispatchStatus, NextRetryUtc);",
        "CREATE INDEX IX_Dispatch_BatchId ON Dispatch(BatchId);",

        // -----------------------
        // 5.12 DispatchItem
        // -----------------------
        """
        CREATE TABLE DispatchItem (
            DispatchId      TEXT NOT NULL,
            ProviderDhsCode TEXT NOT NULL,
            ProIdClaim      INTEGER NOT NULL,
            ItemOrder       INTEGER NOT NULL,
            ItemResult      INTEGER NULL,
            ErrorMessage    TEXT NULL,
            PRIMARY KEY (DispatchId, ProviderDhsCode, ProIdClaim),
            FOREIGN KEY (DispatchId) REFERENCES Dispatch(DispatchId) ON DELETE CASCADE,
            FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE
        );
        """,
        "CREATE INDEX IX_DispatchItem_Provider_ProIdClaim ON DispatchItem(ProviderDhsCode, ProIdClaim);",
        "CREATE INDEX IX_DispatchItem_DispatchId ON DispatchItem(DispatchId);",

        // -----------------------
        // 5.13 Attachment (PHI: encrypted blob/base64)
        // -----------------------
        """
        CREATE TABLE Attachment (
            AttachmentId             TEXT PRIMARY KEY, -- UUID
            ProviderDhsCode          TEXT NOT NULL,
            ProIdClaim               INTEGER NOT NULL,
            AttachmentSourceType     INTEGER NOT NULL,
            LocationPath             TEXT NULL,
            LocationBytesEncrypted   BLOB NULL, -- encrypted at rest (WBS 1.3)
            LocationPathEncrypted    BLOB NULL,
            AttachBitBase64Encrypted BLOB NULL, -- encrypted at rest (WBS 1.3)
            FileName                 TEXT NULL,
            ContentType              TEXT NULL,
            SizeBytes                INTEGER NULL,
            Sha256                   TEXT NULL,
            OnlineURL                TEXT NULL,
            OnlineUrlEncrypted       BLOB NULL,
            UploadStatus             INTEGER NOT NULL,
            AttemptCount             INTEGER NOT NULL DEFAULT 0,
            NextRetryUtc             TEXT NULL,
            LastError                TEXT NULL,
            CreatedUtc               TEXT NOT NULL,
            UpdatedUtc               TEXT NOT NULL,
            FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE
        );
        """,
        "CREATE INDEX IX_Attachment_Provider_ProIdClaim ON Attachment(ProviderDhsCode, ProIdClaim);",
        "CREATE INDEX IX_Attachment_Status_NextRetryUtc ON Attachment(UploadStatus, NextRetryUtc);",
        "CREATE UNIQUE INDEX UX_Attachment_Claim_Sha256 ON Attachment(ProviderDhsCode, ProIdClaim, Sha256) WHERE Sha256 IS NOT NULL AND Sha256 <> '';",

        // -----------------------
        // 5.14 ValidationIssue
        // -----------------------
        """
        CREATE TABLE ValidationIssue (
            ValidationIssueId INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode    TEXT NOT NULL,
            ProIdClaim         INTEGER NULL,
            IssueType          TEXT NOT NULL,
            FieldPath          TEXT NULL,
            RawValue           TEXT NULL,
            Message            TEXT NOT NULL,
            IsBlocking         INTEGER NOT NULL DEFAULT 0,
            CreatedUtc         TEXT NOT NULL,
            ResolvedUtc        TEXT NULL,
            ResolvedBy         TEXT NULL,
            FOREIGN KEY (ProviderDhsCode, ProIdClaim) REFERENCES Claim(ProviderDhsCode, ProIdClaim) ON DELETE CASCADE
        );
        """,
        "CREATE INDEX IX_ValidationIssue_Provider_ProIdClaim ON ValidationIssue(ProviderDhsCode, ProIdClaim);",
        "CREATE INDEX IX_ValidationIssue_IssueType_IsBlocking ON ValidationIssue(IssueType, IsBlocking);",

        // -----------------------
        // 5.15 ApiCallLog (NO PHI)
        // -----------------------
        """
        CREATE TABLE ApiCallLog (
            ApiCallLogId   INTEGER PRIMARY KEY AUTOINCREMENT,
            ProviderDhsCode TEXT NULL,
            EndpointName    TEXT NOT NULL,
            CorrelationId   TEXT NULL,
            RequestUtc      TEXT NOT NULL,
            ResponseUtc     TEXT NULL,
            DurationMs      INTEGER NULL,
            HttpStatusCode  INTEGER NULL,
            Succeeded       INTEGER NOT NULL DEFAULT 0,
            ErrorMessage    TEXT NULL,
            RequestBytes    INTEGER NULL,
            ResponseBytes   INTEGER NULL,
            WasGzipRequest  INTEGER NOT NULL DEFAULT 0
        );
        """,
        "CREATE INDEX IX_ApiCallLog_Endpoint_RequestUtc ON ApiCallLog(EndpointName, RequestUtc);",
    };
}

using System.Text.Json.Nodes;
using DHSIntegrationAgent.Contracts.Persistence;
using DHSIntegrationAgent.Domain.WorkStates;

namespace DHSIntegrationAgent.Adapters.Claims;

public static class AttachmentMapper
{
    public static AttachmentRow? MapToAttachmentRow(string providerDhsCode, int proIdClaim, JsonObject attObj, DateTimeOffset now)
    {
        string? attId = null;
        if (attObj.TryGetPropertyValue("AttachmentID", out var idNode) && idNode != null) attId = idNode.ToString();
        else if (attObj.TryGetPropertyValue("attachmentID", out idNode) && idNode != null) attId = idNode.ToString();

        if (string.IsNullOrWhiteSpace(attId))
        {
            // Use deterministic hash based on the entire JSON payload for stability
            var payloadString = attObj.ToJsonString();
            var payloadBytes = System.Text.Encoding.UTF8.GetBytes(payloadString);
            using var md5 = System.Security.Cryptography.MD5.Create();
            attId = Convert.ToHexString(md5.ComputeHash(payloadBytes)).ToLowerInvariant();
        }

        var compositeId = $"{providerDhsCode}_{proIdClaim}_{attId}";

        string? location = null;
        if (attObj.TryGetPropertyValue("location", out var locNode) && locNode != null) location = locNode.ToString().Trim('"');

        byte[]? attachBit = null;
        if (attObj.TryGetPropertyValue("AttachBit", out var bitNode) && bitNode != null)
        {
            var raw = bitNode.ToString().Trim('"');
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { attachBit = Convert.FromBase64String(raw); } catch { }
            }
        }

        string? fileName = null;
        if (attObj.TryGetPropertyValue("FileName", out var fnNode) && fnNode != null) fileName = fnNode.ToString().Trim('"');

        string? contentType = null;
        if (attObj.TryGetPropertyValue("AttachmentType", out var typeNode) && typeNode != null) contentType = typeNode.ToString().Trim('"');

        long? size = null;
        if (attObj.TryGetPropertyValue("FileSizeInByte", out var sizeNode) && sizeNode != null && long.TryParse(sizeNode.ToString(), out var s)) size = s;

        var sourceType = AttachmentSourceType.FilePath;
        if (attachBit != null) sourceType = AttachmentSourceType.Base64InAttachBit;
        else if (!string.IsNullOrWhiteSpace(location) && (location.Contains("/") || location.Contains("\\"))) sourceType = AttachmentSourceType.FilePath;
        else if (!string.IsNullOrWhiteSpace(location)) sourceType = AttachmentSourceType.RawBytesInLocation;

        if (string.IsNullOrWhiteSpace(fileName) && sourceType == AttachmentSourceType.FilePath && !string.IsNullOrWhiteSpace(location))
        {
            try
            {
                fileName = System.IO.Path.GetFileName(location);
            }
            catch
            {
                // Fallback if location is not a valid path
                fileName = "Unknown";
            }
        }

        return new AttachmentRow(
            AttachmentId: compositeId,
            ProviderDhsCode: providerDhsCode,
            ProIdClaim: proIdClaim,
            SourceType: sourceType,
            LocationPathPlaintext: sourceType == AttachmentSourceType.FilePath ? location : null,
            LocationBytesPlaintext: sourceType == AttachmentSourceType.RawBytesInLocation ? (location != null ? System.Text.Encoding.UTF8.GetBytes(location) : null) : null,
            AttachBitBase64Plaintext: attachBit,
            FileName: fileName,
            ContentType: contentType,
            SizeBytes: size,
            Sha256: null,
            OnlineUrlPlaintext: null,
            UploadStatus: UploadStatus.Staged,
            AttemptCount: 0,
            NextRetryUtc: null,
            LastError: null,
            CreatedUtc: now,
            UpdatedUtc: now
        );
    }
}

import re

with open("src/DHSIntegrationAgent.Workers/AttachmentDispatchService.cs", "r") as f:
    content = f.read()

pattern = r"Interlocked\.Add\(ref failedAttachmentsCount, attachmentInfos\.Count\(x => x\.IsNew\)\);\s*Interlocked\.Add\(ref notifiedAttachmentsCount, -attachmentInfos\.Count\(x => x\.IsNew\)\);"

new_code = """int affectedCount = totalAttachmentsToUpload > 0 ? attachmentInfos.Count(x => x.IsNew) : attachmentInfos.Count;
                    Interlocked.Add(ref failedAttachmentsCount, affectedCount);
                    Interlocked.Add(ref notifiedAttachmentsCount, -affectedCount);"""

content = re.sub(pattern, new_code, content)

with open("src/DHSIntegrationAgent.Workers/AttachmentDispatchService.cs", "w") as f:
    f.write(content)

print("Modifications done")

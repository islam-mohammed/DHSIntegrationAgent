import re

with open("src/DHSIntegrationAgent.Workers/FetchStageService.cs", "r") as f:
    content = f.read()

search = """                    // Injected fields per your request
                    var keysToRemove = buildResult.Bundle.ClaimHeader.Select(k => k.Key)
                        .Where(k => {
                            var cleanKey = k.Replace("_", "").Replace("-", "").Replace(" ", "");
                            return string.Equals(cleanKey, "providerdhscode", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(cleanKey, "providercode", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(cleanKey, "bcrid", StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();
                    foreach (var k in keysToRemove)
                    {
                        buildResult.Bundle.ClaimHeader.Remove(k);
                    }

                    buildResult.Bundle.ClaimHeader["providerCode"] = batch.ProviderDhsCode;
                    if (long.TryParse(bcrId, out var bcrIdLong))
                        buildResult.Bundle.ClaimHeader["bCR_Id"] = bcrIdLong;"""

replace = """                    // Injected fields per your request
                    RemoveOldIdentifiers(buildResult.Bundle.ClaimHeader);
                    RemoveOldIdentifiers(buildResult.Bundle.ServiceDetails);
                    RemoveOldIdentifiers(buildResult.Bundle.DiagnosisDetails);
                    RemoveOldIdentifiers(buildResult.Bundle.LabDetails);
                    RemoveOldIdentifiers(buildResult.Bundle.RadiologyDetails);
                    RemoveOldIdentifiers(buildResult.Bundle.OpticalVitalSigns);
                    RemoveOldIdentifiers(buildResult.Bundle.DhsDoctors);

                    buildResult.Bundle.ClaimHeader["providerCode"] = batch.ProviderDhsCode;
                    if (long.TryParse(bcrId, out var bcrIdLong))
                        buildResult.Bundle.ClaimHeader["bCR_Id"] = bcrIdLong;"""

content = content.replace(search, replace)

method = """    private static void RemoveOldIdentifiers(JsonNode? node)
    {
        if (node is null) return;

        if (node is JsonObject obj)
        {
            var keysToRemove = obj.Select(k => k.Key)
                .Where(k => {
                    var cleanKey = k.Replace("_", "").Replace("-", "").Replace(" ", "");
                    return string.Equals(cleanKey, "providerdhscode", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(cleanKey, "providercode", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(cleanKey, "bcrid", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            foreach (var k in keysToRemove)
            {
                obj.Remove(k);
            }

            foreach (var kv in obj)
            {
                RemoveOldIdentifiers(kv.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                RemoveOldIdentifiers(item);
            }
        }
    }
}"""

content = content.replace("}\n}", "}\n\n" + method)

with open("src/DHSIntegrationAgent.Workers/FetchStageService.cs", "w") as f:
    f.write(content)

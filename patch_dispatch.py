import re

with open("src/DHSIntegrationAgent.Workers/DispatchService.cs", "r") as f:
    content = f.read()

# Replace all occurrences of keysToRemove in DispatchService.cs
search = """                                var keysToRemove = header.Select(k => k.Key)
                                    .Where(k => {
                                        var cleanKey = k.Replace("_", "").Replace("-", "").Replace(" ", "");
                                        return string.Equals(cleanKey, "providerdhscode", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(cleanKey, "providercode", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(cleanKey, "bcrid", StringComparison.OrdinalIgnoreCase);
                                    })
                                    .ToList();
                                foreach (var k in keysToRemove)
                                {
                                    header.Remove(k);
                                }

                                header["providerCode"] = batch.ProviderDhsCode;
                                if (long.TryParse(batch.BcrId, out var bcrIdLong))
                                    header["bCR_Id"] = bcrIdLong;"""

replace = """                                RemoveOldIdentifiers(bundleObj);
                                header["providerCode"] = batch.ProviderDhsCode;
                                if (long.TryParse(batch.BcrId, out var bcrIdLong))
                                    header["bCR_Id"] = bcrIdLong;"""

content = content.replace(search, replace)

search_2 = """                            var keysToRemove = header.Select(k => k.Key)
                                .Where(k => {
                                        var cleanKey = k.Replace("_", "").Replace("-", "").Replace(" ", "");
                                        return string.Equals(cleanKey, "providerdhscode", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(cleanKey, "providercode", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(cleanKey, "bcrid", StringComparison.OrdinalIgnoreCase);
                                    })
                                .ToList();
                            foreach (var k in keysToRemove)
                            {
                                header.Remove(k);
                            }

                            header["providerCode"] = originalDispatch.ProviderDhsCode;
                            if (!string.IsNullOrEmpty(originalDispatch.BcrId) && long.TryParse(originalDispatch.BcrId, out var bcrIdLong))
                                header["bCR_Id"] = bcrIdLong;"""

replace_2 = """                            RemoveOldIdentifiers(bundleObj);
                            header["providerCode"] = originalDispatch.ProviderDhsCode;
                            if (!string.IsNullOrEmpty(originalDispatch.BcrId) && long.TryParse(originalDispatch.BcrId, out var bcrIdLong))
                                header["bCR_Id"] = bcrIdLong;"""

content = content.replace(search_2, replace_2)

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

with open("src/DHSIntegrationAgent.Workers/DispatchService.cs", "w") as f:
    f.write(content)

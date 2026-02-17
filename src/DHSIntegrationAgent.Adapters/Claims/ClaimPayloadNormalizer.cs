using System.Globalization;
using System.Text.Json.Nodes;

namespace DHSIntegrationAgent.Adapters.Claims;

public static class ClaimPayloadNormalizer
{
    public static void RemovePropertyIgnoreCase(JsonObject obj, string name)
    {
        var keysToRemove = obj.Select(kv => kv.Key)
                              .Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
                              .ToList();
        foreach (var key in keysToRemove)
        {
            obj.Remove(key);
        }
    }

    public static void NormalizeDiagnosisDates(JsonArray diagnosisDetails)
    {
        for (var i = 0; i < diagnosisDetails.Count; i++)
        {
            if (diagnosisDetails[i] is not JsonObject obj)
                continue;

            // Collect all potential diagnosis date fields
            var dateKeys = obj.Select(kv => kv.Key)
                              .Where(k => string.Equals(k, "diagnosisDate", StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(k, "diagnosis_Date", StringComparison.OrdinalIgnoreCase))
                              .ToList();

            if (dateKeys.Count == 0) continue;

            string? rawValue = null;

            // Prefer diagnosis_Date (with underscore) if it has a value, as it's often the raw datetime.
            var preferredKeysOrdered = dateKeys.OrderByDescending(k => k.Contains('_')).ToList();

            foreach (var key in preferredKeysOrdered)
            {
                var val = obj[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    rawValue = val;
                    break;
                }
            }

            if (rawValue != null)
            {
                DateTimeOffset? finalDate = null;
                if (DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                {
                    finalDate = dto;
                }
                else if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
                {
                    finalDate = new DateTimeOffset(dt, TimeSpan.Zero);
                }

                if (finalDate.HasValue)
                {
                    // Remove ALL existing variations to ensure no duplicates and correct casing
                    foreach (var k in dateKeys) obj.Remove(k);

                    // Set normalized camelCase field with date-only format
                    obj["diagnosisDate"] = finalDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }
        }
    }
}

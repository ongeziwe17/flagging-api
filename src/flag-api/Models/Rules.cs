using System.Text.Json;

namespace FeatureFlags.Api.Models;

// JSON shape stored in FlagAssignment.RulesJson
public class RuleSet
{
    // "all" (AND) or "any" (OR)
    public string Match { get; set; } = "all";
    public List<Rule> Rules { get; set; } = new();
}

public class Rule
{
    public string Attribute { get; set; } = ""; // e.g., "country"
    public string Operator { get; set; } = "eq"; // eq, ne, contains, startsWith, endsWith, in, gt, gte, lt, lte, regex
    public string Value { get; set; } = ""; // e.g., "US" or "alpha,beta"
}

public static class RuleSetParser
{
    public static RuleSet? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<RuleSet>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

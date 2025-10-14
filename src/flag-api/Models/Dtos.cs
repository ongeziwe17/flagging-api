namespace FeatureFlags.Api.Models;

public record CreateFlagDto(string Key, string Name, string? Description);
public record UpdateFlagDto(string? Name, string? Description, bool? IsArchived);

public record UpsertAssignmentDto(
    string EnvKey,
    bool IsEnabled,
    int? PercentageRollout,
    object? Rules // JSON object (RuleSet). Keep as object to accept arbitrary JSON.
);

public record CreateEnvDto(string Key, string Name);

public record EvalRequest(
    string EnvKey,
    string UserId,
    Dictionary<string, string>? Attributes // e.g., { "country": "US", "email": "a@b.com" }
);

public record EvalResponse(bool Enabled, string Reason, string? MatchedRule);

public record CreateApiKeyDto(string Name, string Role, string PlaintextKey); // one-time show

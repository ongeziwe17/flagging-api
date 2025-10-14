using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FeatureFlags.Api.Models;

public class FeatureFlag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public required string Key { get; set; } // unique, kebab_case

    [MaxLength(256)]
    public required string Name { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<FlagAssignment> Assignments { get; set; } = new List<FlagAssignment>();
}

public class EnvironmentEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public required string Key { get; set; } // dev, staging, prod (unique)

    [MaxLength(128)]
    public required string Name { get; set; }
}

public class FlagAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(FeatureFlag))]
    public Guid FeatureFlagId { get; set; }
    public FeatureFlag? FeatureFlag { get; set; }

    [ForeignKey(nameof(Environment))]
    public Guid EnvironmentId { get; set; }
    public EnvironmentEntity? Environment { get; set; }

    public bool IsEnabled { get; set; } = false;

    // Optional percentage rollout (0..100). If set and rules do not match, bucketing can enable the user.
    public int? PercentageRollout { get; set; }

    // JSON of RuleSet (see Rules.cs)
    public string? RulesJson { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(128)]
    public string? UpdatedBy { get; set; }
}

public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public required string Name { get; set; }

    // SHA256 hex
    [MaxLength(64)]
    public required string KeyHash { get; set; }

    // "Admin" or "Reader"
    [MaxLength(16)]
    public required string Role { get; set; } = "Reader";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; } = false;
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public required string Actor { get; set; }

    [MaxLength(64)]
    public required string Action { get; set; } // e.g., "CreateFlag", "UpdateAssignment"

    [MaxLength(64)]
    public required string EntityType { get; set; } // "FeatureFlag","FlagAssignment","Environment","ApiKey"

    [MaxLength(128)]
    public required string EntityId { get; set; } // Key or Guid

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public string? DataJson { get; set; }
}

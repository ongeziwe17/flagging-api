using FeatureFlags.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FeatureFlags.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<EnvironmentEntity> Environments => Set<EnvironmentEntity>();
    public DbSet<FlagAssignment> FlagAssignments => Set<FlagAssignment>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FeatureFlag>()
            .HasIndex(x => x.Key)
            .IsUnique();

        b.Entity<EnvironmentEntity>()
            .HasIndex(x => x.Key)
            .IsUnique();

        b.Entity<FlagAssignment>()
            .HasIndex(x => new { x.FeatureFlagId, x.EnvironmentId })
            .IsUnique();

        // Seed default environments (dev, staging, prod) via model seed is risky for migrations
        // seed them in Program.cs on startup to keep it straightforward.
    }
}

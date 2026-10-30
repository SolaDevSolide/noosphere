using Microsoft.EntityFrameworkCore;
using Noosphere.Domain;

namespace Noosphere.Api.Data;

public sealed class NoosphereDbContext(DbContextOptions<NoosphereDbContext> options) : DbContext(options)
{
    public DbSet<BusinessDomain> Domains => Set<BusinessDomain>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<DataObject> DataObjects => Set<DataObject>();
    public DbSet<Flow> Flows => Set<Flow>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BusinessDomain>(e =>
        {
            e.ToTable("Domains");
            Common(e);
        });
        modelBuilder.Entity<Team>(e =>
        {
            Common(e);
            e.Property(x => x.Contact).HasMaxLength(Rules.MaxShortTextLength);
        });
        modelBuilder.Entity<Application>(e =>
        {
            Common(e);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Tech).HasMaxLength(Rules.MaxShortTextLength);
            e.HasOne<BusinessDomain>().WithMany().HasForeignKey(x => x.DomainId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DataObject>(e =>
        {
            Common(e);
            e.Property(x => x.Sensitivity).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Application>().WithMany().HasForeignKey(x => x.SourceApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Flow>(e =>
        {
            Common(e);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Schedule).HasMaxLength(Rules.MaxShortTextLength);
            e.Property(x => x.Volume).HasMaxLength(Rules.MaxShortTextLength);
            e.Property(x => x.PlannedChangeSummary).HasMaxLength(Rules.MaxShortTextLength);
            e.HasOne<Application>().WithMany().HasForeignKey(x => x.FromId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Application>().WithMany().HasForeignKey(x => x.ToId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.OwnerTeamId).OnDelete(DeleteBehavior.Restrict);
            // Flows carrying a data object (lineage, filters).
            e.HasIndex(x => x.DataObjectIds).HasMethod("gin");
        });
        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(100);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(Rules.MaxNameLength);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
    }

    private static void Common<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e) where T : LandscapeEntity
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasMaxLength(Rules.MaxIdLength);
        e.Property(x => x.Name).HasMaxLength(Rules.MaxNameLength);
        e.Property(x => x.Description).HasMaxLength(Rules.MaxTextLength);
        e.Property(x => x.Version).IsConcurrencyToken();
    }
}

/// <summary>Used by <c>dotnet ef</c> at design time only (migrations).</summary>
public sealed class DesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<NoosphereDbContext>
{
    public NoosphereDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NoosphereDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=noosphere;Username=postgres;Password=postgres").Options);
}

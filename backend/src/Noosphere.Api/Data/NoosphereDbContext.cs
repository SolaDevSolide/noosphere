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
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Revision> Revisions => Set<Revision>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<ProposalChange> ProposalChanges => Set<ProposalChange>();
    public DbSet<ProposalApproval> ProposalApprovals => Set<ProposalApproval>();
    public DbSet<InstanceSettings> InstanceSettings => Set<InstanceSettings>();
    public DbSet<AccessLog> AccessLogs => Set<AccessLog>();

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
            e.Property(x => x.ProtectedNotes).HasMaxLength(Rules.MaxTextLength);
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
            e.Property(x => x.ProtectedNotes).HasMaxLength(Rules.MaxTextLength);
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
        modelBuilder.Entity<TeamMember>(e =>
        {
            e.HasKey(x => new { x.UserId, x.TeamId });
            e.Property(x => x.TeamId).HasMaxLength(Rules.MaxIdLength);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TeamId);
        });
        modelBuilder.Entity<Page>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.Title).HasMaxLength(Rules.MaxNameLength);
            e.Property(x => x.Body).HasMaxLength(Rules.MaxBodyLength);
            e.Property(x => x.ParentId).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.AttachedKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AttachedId).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<Page>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.OwnerTeamId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AttachedKind, x.AttachedId });
            // Backlinks and "mentioned in".
            e.HasIndex(x => x.Links).HasMethod("gin");
        });
        modelBuilder.Entity<Revision>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EntityId).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.Operation).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
            e.Property(x => x.AuthorName).HasMaxLength(100);
            e.Property(x => x.AuthorKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Summary).HasMaxLength(Rules.MaxShortTextLength);
            e.HasIndex(x => new { x.Kind, x.EntityId, x.Version }).IsUnique();
            e.HasIndex(x => x.ProposalId);
        });
        modelBuilder.Entity<Proposal>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(Rules.MaxNameLength);
            e.Property(x => x.Comment).HasMaxLength(Rules.MaxTextLength);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AuthorName).HasMaxLength(100);
            e.Property(x => x.AuthorKind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.OnBehalfOf).HasMaxLength(100);
            e.Property(x => x.DecidedByName).HasMaxLength(100);
            e.Property(x => x.RefusalReason).HasMaxLength(Rules.MaxShortTextLength);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasMany(x => x.Changes).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Approvals).WithOne().HasForeignKey(x => x.ProposalId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.AuthorId);
        });
        modelBuilder.Entity<ProposalChange>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TargetId).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.Operation).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Kind, x.TargetId });
        });
        modelBuilder.Entity<ProposalApproval>(e =>
        {
            e.HasKey(x => new { x.ProposalId, x.TeamId });
            e.Property(x => x.TeamId).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.UserName).HasMaxLength(100);
        });
        modelBuilder.Entity<InstanceSettings>(e =>
        {
            e.ToTable("InstanceSettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.InstanceName).HasMaxLength(Rules.MaxNameLength);
            e.Property(x => x.OrganisationName).HasMaxLength(Rules.MaxNameLength);
            e.Property(x => x.DefaultLanguage).HasMaxLength(5);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<AccessLog>(e =>
        {
            e.ToTable("AccessLog");
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EntityId).HasMaxLength(Rules.MaxIdLength);
            e.Property(x => x.What).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(Rules.MaxShortTextLength);
            e.Property(x => x.Why).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.WhyTeamId).HasMaxLength(Rules.MaxIdLength);
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.Username);
            e.HasIndex(x => new { x.Kind, x.EntityId });
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

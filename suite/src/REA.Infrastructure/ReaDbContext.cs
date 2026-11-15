using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using REA.Domain;

namespace REA.Infrastructure;

public sealed class ReaDbContext(DbContextOptions<ReaDbContext> options) : DbContext(options)
{
    public DbSet<ResearchProject> Projects => Set<ResearchProject>();
    public DbSet<ResearchEnvironment> Environments => Set<ResearchEnvironment>();
    public DbSet<ActivityEntry> Activity => Set<ActivityEntry>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite não ordena DateTimeOffset nativamente; o formato binário preserva ordem e fuso.
        builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ResearchProject>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(ResearchProject.NameMaxLength).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(ResearchProject.NameMaxLength).IsRequired();
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.Property(x => x.GameName).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(ResearchProject.DescriptionMaxLength);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => new { x.Status, x.UpdatedAt });
            e.HasMany(x => x.Environments).WithOne(x => x.Project).HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict); // nunca apagar histórico em cascata
        });

        b.Entity<ResearchEnvironment>(e =>
        {
            e.ToTable("environments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.ProjectId, x.NormalizedName }).IsUnique();
            e.Property(x => x.Kind).HasConversion<int>();
            e.Property(x => x.ClientVersion).HasMaxLength(80);
            e.Property(x => x.ClientDirectory).HasMaxLength(1024);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Notes).HasMaxLength(8000);
        });

        b.Entity<ActivityEntry>(e =>
        {
            e.ToTable("activity_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Operator).HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasMaxLength(120).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => new { x.ProjectId, x.Timestamp });
        });

        b.Entity<AppSetting>(e =>
        {
            e.ToTable("app_settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
        });
    }
}

/// <summary>Usado só pelo dotnet-ef para gerar migrações.</summary>
public sealed class ReaDbContextDesignFactory : IDesignTimeDbContextFactory<ReaDbContext>
{
    public ReaDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ReaDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

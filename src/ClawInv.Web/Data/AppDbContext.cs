using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UniverseSettings> UniverseSettings => Set<UniverseSettings>();
    public DbSet<StrategyConfig> StrategyConfigs => Set<StrategyConfig>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<PortfolioHolding> PortfolioHoldings => Set<PortfolioHolding>();
    public DbSet<TradeEvent> TradeEvents => Set<TradeEvent>();
    public DbSet<PortfolioDailySnapshot> PortfolioDailySnapshots => Set<PortfolioDailySnapshot>();

    public DbSet<BackgroundTask> BackgroundTasks => Set<BackgroundTask>();
    public DbSet<JobState> JobStates => Set<JobState>();

    public DbSet<RecommendationRun> RecommendationRuns => Set<RecommendationRun>();
    public DbSet<TradeRecommendation> TradeRecommendations => Set<TradeRecommendation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UniverseSettings>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<StrategyConfig>().HasIndex(x => x.Key).IsUnique();

        modelBuilder.Entity<Portfolio>()
            .HasOne(p => p.Strategy)
            .WithMany()
            .HasForeignKey(p => p.StrategyConfigId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PortfolioHolding>()
            .HasOne(h => h.Portfolio)
            .WithMany(p => p.Holdings)
            .HasForeignKey(h => h.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TradeEvent>()
            .HasOne(t => t.Portfolio)
            .WithMany(p => p.Trades)
            .HasForeignKey(t => t.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PortfolioDailySnapshot>()
            .HasOne(s => s.Portfolio)
            .WithMany(p => p.Snapshots)
            .HasForeignKey(s => s.PortfolioId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PortfolioDailySnapshot>()
            .HasIndex(s => new { s.PortfolioId, s.Date })
            .IsUnique();

        modelBuilder.Entity<RecommendationRun>()
            .HasOne(r => r.Strategy)
            .WithMany()
            .HasForeignKey(r => r.StrategyConfigId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TradeRecommendation>()
            .HasOne(t => t.Run)
            .WithMany(r => r.Trades)
            .HasForeignKey(t => t.RecommendationRunId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecommendationRun>()
            .HasIndex(r => new { r.StrategyConfigId, r.AsOfDate });

        modelBuilder.Entity<BackgroundTask>()
            .HasIndex(t => new { t.Type, t.Status, t.CreatedAtUtc });

        modelBuilder.Entity<JobState>().HasIndex(x => x.Key).IsUnique();
    }
}

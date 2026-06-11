using Microsoft.EntityFrameworkCore;
using BubbleSplash.Api.Models;

namespace BubbleSplash.Api.Data;

/// <summary>
/// EF Core database context for the Bubble Splash word suggestions.
/// </summary>
public class BubbleSplashDbContext : DbContext
{
    public BubbleSplashDbContext(DbContextOptions<BubbleSplashDbContext> options)
        : base(options)
    {
    }

    public DbSet<WordSuggestion> WordSuggestions => Set<WordSuggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WordSuggestion>(entity =>
        {
            entity.ToTable("word_suggestions");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.Word)
                .HasColumnName("word")
                .HasMaxLength(45)
                .IsRequired();

            entity.Property(e => e.Reason)
                .HasColumnName("reason")
                .HasMaxLength(250);

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(SuggestionStatus.Pending);

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasDefaultValueSql("now()");

            entity.Property(e => e.ReviewedAtUtc)
                .HasColumnName("reviewed_at_utc");

            entity.Property(e => e.SubmitterIp)
                .HasColumnName("submitter_ip")
                .HasMaxLength(45);

            // Index on word for quick duplicate lookups
            entity.HasIndex(e => e.Word)
                .HasDatabaseName("ix_word_suggestions_word");

            // Index on status for admin filtering
            entity.HasIndex(e => e.Status)
                .HasDatabaseName("ix_word_suggestions_status");
        });
    }
}

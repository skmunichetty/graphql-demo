using Accounting.Domain;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Accounting service's own database ("accounting").
/// It stores export records only; estimate data is always read from the Estimates API.
/// </summary>
public sealed class AccountingDbContext : DbContext
{
    public AccountingDbContext(DbContextOptions<AccountingDbContext> options)
        : base(options)
    {
    }

    public DbSet<EstimateExport> EstimateExports => Set<EstimateExport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EstimateExport>(export =>
        {
            export.ToTable("estimate_exports", table =>
            {
                table.HasCheckConstraint("ck_estimate_exports_status", "status IN ('Pending', 'Succeeded', 'Failed')");
                table.HasCheckConstraint(
                    "ck_estimate_exports_reference_when_succeeded",
                    "status <> 'Succeeded' OR external_reference IS NOT NULL");
                table.HasCheckConstraint("ck_estimate_exports_attempts_positive", "attempt_count > 0");
            });

            export.HasKey(item => item.Id);
            export.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            export.Property(item => item.TenantId).HasColumnName("tenant_id").HasMaxLength(64).IsRequired();
            export.Property(item => item.EstimateId).HasColumnName("estimate_id");
            export.Property(item => item.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(30).IsRequired();
            export.Property(item => item.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
            export.Property(item => item.ExternalReference).HasColumnName("external_reference").HasMaxLength(200);
            export.Property(item => item.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsFixedLength().IsRequired();
            export.Property(item => item.ExportedTotal).HasColumnName("exported_total").HasPrecision(18, 2);
            export.Property(item => item.AttemptCount).HasColumnName("attempt_count");
            export.Property(item => item.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(100);
            export.Property(item => item.LastErrorMessage).HasColumnName("last_error_message").HasMaxLength(EstimateExport.MaxErrorMessageLength);
            export.Property(item => item.CreatedAt).HasColumnName("created_at");
            export.Property(item => item.UpdatedAt).HasColumnName("updated_at");
            export.Property(item => item.CompletedAt).HasColumnName("completed_at");
            export.Property(item => item.Version).HasColumnName("version").IsConcurrencyToken();

            // The idempotency guarantee: at most one export per tenant, estimate and provider,
            // even when two requests arrive at the same moment.
            export.HasIndex(item => new { item.TenantId, item.EstimateId, item.Provider })
                .IsUnique()
                .HasDatabaseName(UniqueExportIndexName);

            export.HasIndex(item => new { item.TenantId, item.CreatedAt, item.Id })
                .HasDatabaseName("ix_estimate_exports_tenant_created_id")
                .IsDescending(false, true, true);
        });
    }

    public const string UniqueExportIndexName = "ux_estimate_exports_tenant_estimate_provider";
}

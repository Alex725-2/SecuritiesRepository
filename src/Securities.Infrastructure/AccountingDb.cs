using Microsoft.EntityFrameworkCore;
using Securities.Domain;

namespace Securities.Infrastructure;
/// <summary>Единица работы PostgreSQL для остатков, журнала и доставки событий.</summary>
public sealed class AccountingDb(DbContextOptions<AccountingDb> options) : DbContext(options)
{
    /// <summary>Данные Funds.</summary>
    public DbSet<Fund> Funds => Set<Fund>();
    /// <summary>Данные Accounts.</summary>
    public DbSet<CustodyAccount> Accounts => Set<CustodyAccount>();
    /// <summary>Данные Securities.</summary>
    public DbSet<Security> Securities => Set<Security>();
    /// <summary>Данные Positions.</summary>
    public DbSet<Position> Positions => Set<Position>();
    /// <summary>Данные Operations.</summary>
    public DbSet<Operation> Operations => Set<Operation>();
    /// <summary>Данные Entries.</summary>
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();

    /// <summary>Настраивает и выполняет инфраструктурную часть учёта.</summary>
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ManagementCompany>().HasIndex(x => x.INN).IsUnique();
        b.Entity<Fund>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Fund>().HasOne<ManagementCompany>().WithMany().HasForeignKey(x => x.ManagementCompanyId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Security>().HasIndex(x => x.ISIN).IsUnique();
        b.Entity<Custodian>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Exchange>().HasIndex(x => x.Code).IsUnique();
        b.Entity<CustodyAccount>().HasIndex(x => new { x.CustodianId, x.AccountNumber }).IsUnique();
        b.Entity<CustodyAccount>().HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CustodyAccount>().HasOne<Custodian>().WithMany().HasForeignKey(x => x.CustodianId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().HasKey(x => new { x.CustodyAccountId, x.SecurityId });
        b.Entity<Position>().HasOne<CustodyAccount>().WithMany().HasForeignKey(x => x.CustodyAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Position>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<Position>().ToTable(t => t.HasCheckConstraint("CK_Position_Quantity", "\"Quantity\" >= 0"));
        b.Entity<Operation>().HasIndex(x => x.IdempotencyKey).IsUnique();
        b.Entity<Operation>().Property(x => x.IdempotencyKey).HasMaxLength(128);
        b.Entity<Operation>().HasIndex(x => x.ReversesOperationId).IsUnique();
        b.Entity<Operation>().HasIndex(x => new { x.FundId, x.CreatedAt });
        b.Entity<Operation>().HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Operation>().HasOne<Operation>().WithMany().HasForeignKey(x => x.ReversesOperationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LedgerEntry>().HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LedgerEntry>().HasOne<CustodyAccount>().WithMany().HasForeignKey(x => x.CustodyAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LedgerEntry>().HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LedgerEntry>().HasIndex(x => new { x.CustodyAccountId, x.SecurityId, x.CreatedAt });
        b.Entity<LedgerEntry>().ToTable(t => t.HasCheckConstraint("CK_Entry_Delta", "\"QuantityDelta\" <> 0"));
        b.Entity<SecurityPrice>().HasKey(x => new { x.SecurityId, x.PriceDate });
        b.Entity<SecurityPrice>().HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
        b.Entity<SecurityPrice>().ToTable(t => t.HasCheckConstraint("CK_Price", "\"Price\" >= 0"));
        b.Entity<FundValuation>().HasKey(x => new { x.FundId, x.ValuationDate });
        b.Entity<FundValuation>().HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId);
        b.Entity<FundUnits>().HasKey(x => new { x.FundId, x.EffectiveDate });
        b.Entity<FundUnits>().HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId);
        b.Entity<FundUnits>().ToTable(t => t.HasCheckConstraint("CK_Units", "\"UnitsOutstanding\" > 0"));
        b.Entity<ReconciliationRun>().HasOne<Fund>().WithMany().HasForeignKey(x => x.FundId);
        b.Entity<ReconciliationItem>().HasOne<CustodyAccount>().WithMany().HasForeignKey(x => x.CustodyAccountId);
        b.Entity<ReconciliationItem>().HasOne<Security>().WithMany().HasForeignKey(x => x.SecurityId);
        b.Entity<ReconciliationRun>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ReconciliationRunId);
        b.Entity<OutboxMessage>().HasIndex(x => x.DeliveredAt);
        b.Entity<OutboxMessage>().HasIndex(x => x.OperationId).IsUnique();
        b.Entity<OutboxMessage>().HasOne<Operation>().WithMany().HasForeignKey(x => x.OperationId);
        b.Entity<InboxMessage>().HasIndex(x => x.OperationId).IsUnique();
        b.Entity<Fund>().ToTable(t => t.HasCheckConstraint("CK_Fund_Status", "\"Status\" IN ('Active','Closed')"));
        b.Entity<CustodyAccount>().ToTable(t => t.HasCheckConstraint("CK_Account_Status", "\"Status\" IN ('Active','Closed')"));
        b.Entity<Position>().ToTable(t => t.HasCheckConstraint("CK_Position_Version", "\"Version\" >= 0"));
        b.Entity<Operation>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Operation_Type", "\"Type\" IN ('Credit','Debit','Transfer','Reverse')");
            t.HasCheckConstraint("CK_Operation_Complete", "\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL");
            t.HasCheckConstraint("CK_Operation_External", "\"Type\" NOT IN ('Credit','Debit') OR (length(trim(\"MovementParty\")) > 0 AND length(trim(\"DocumentReference\")) > 0 AND \"MovementParty\" IS NOT NULL AND \"DocumentReference\" IS NOT NULL)");
        });
        b.Entity<FundValuation>().ToTable(t => t.HasCheckConstraint("CK_Valuation", "\"TotalAssets\" >= 0 AND \"TotalLiabilities\" >= 0 AND \"NAV\" = \"TotalAssets\" - \"TotalLiabilities\""));
        b.Entity<Security>().ToTable(t => t.HasCheckConstraint("CK_Security_Currency", "\"Currency\" ~ '^[A-Z]{3}$'"));
        b.Entity<SecurityPrice>().ToTable(t => t.HasCheckConstraint("CK_Price_Currency", "\"Currency\" ~ '^[A-Z]{3}$'"));
        b.Entity<ReconciliationItem>().HasIndex(x => new { x.ReconciliationRunId, x.CustodyAccountId, x.SecurityId }).IsUnique();
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
                if (p.ClrType == typeof(decimal))
                {
                    p.SetPrecision(28);
                    p.SetScale(8);
                }
    }

    /// <summary>Сохраняет изменения с защитой неизменяемого журнала.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (ChangeTracker.Entries<LedgerEntry>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Проводки неизменяемы.");
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
            foreach (var property in entry.Properties.Where(x => x.Metadata.ClrType == typeof(decimal)))
                property.CurrentValue = AccountingRules.Normalize((decimal)property.CurrentValue!);
        return base.SaveChangesAsync(ct);
    }
}

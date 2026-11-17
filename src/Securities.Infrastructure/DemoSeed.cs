using Microsoft.EntityFrameworkCore;
using Securities.Application;
using Securities.Domain;

namespace Securities.Infrastructure;
/// <summary>Только вымышленные справочники и демонстрационное зачисление.</summary>
public static class DemoSeed
{
    /// <summary>Заполняет только пустую демонстрационную базу.</summary>
    public static async Task Run(AccountingDb db)
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(3, 1)");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            if (await db.Funds.AnyAsync())
            {
                await tx.CommitAsync();
                if (await db.Accounts.AnyAsync(x => x.Id == Guid.Parse("50000000-0000-0000-0000-000000000001")))
                    await new AccountingService(db).Execute("Credit", new MovementRequest("demo-credit", Guid.Parse("50000000-0000-0000-0000-000000000001"), null, Guid.Parse("40000000-0000-0000-0000-000000000001"), 100, "Учебный внешний счёт", "DEMO-DOC-001"));
                return;
            }
            var company = new ManagementCompany
            {
                Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Name = "Демо УК",
                INN = "0000000000"
            };
            var fund = new Fund
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                ManagementCompanyId = company.Id,
                Code = "DEMO",
                Name = "Учебный фонд"
            };
            var custodian = new Custodian
            {
                Id = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Code = "DEMO-CUSTODY",
                Name = "Учебный депозитарий"
            };
            var security = new Security
            {
                Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                ISIN = "DEMO00000001",
                Ticker = "DEMO",
                Name = "Вымышленная акция"
            };
            var a = new CustodyAccount
            {
                Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
                FundId = fund.Id,
                CustodianId = custodian.Id,
                AccountNumber = "DEMO-001"
            };
            var b = new CustodyAccount
            {
                Id = Guid.Parse("50000000-0000-0000-0000-000000000002"),
                FundId = fund.Id,
                CustodianId = custodian.Id,
                AccountNumber = "DEMO-002"
            };
            db.AddRange(company, fund, custodian, security, a, b, new Exchange { Id = Guid.NewGuid(), Code = "DEMO-EX", Name = "Учебная биржа" }, new SecurityPrice { SecurityId = security.Id, PriceDate = DateOnly.FromDateTime(DateTime.UtcNow), Price = 100, Currency = "RUB", Source = "Вымышленные данные" }, new FundUnits { FundId = fund.Id, EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow), UnitsOutstanding = 100 });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            await new AccountingService(db).Execute("Credit", new MovementRequest("demo-credit", a.Id, null, security.Id, 100, "Учебный внешний счёт", "DEMO-DOC-001"));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(3, 1)");
            await db.Database.CloseConnectionAsync();
        }
    }
}

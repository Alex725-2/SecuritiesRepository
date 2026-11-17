using Microsoft.EntityFrameworkCore;
using Securities.Application;
using Securities.Domain;
using Securities.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Securities.IntegrationTests;
/// <summary>В CI Docker обязателен; локальный пропуск явно виден в отчёте.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS") != "1")
            Skip = "Установите RUN_POSTGRES_TESTS=1 при доступном Docker.";
    }
}

/// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
public class PostgresTests
{
    static readonly Guid A = Guid.Parse("50000000-0000-0000-0000-000000000001"), B = Guid.Parse("50000000-0000-0000-0000-000000000002"), S = Guid.Parse("40000000-0000-0000-0000-000000000001");
    static AccountingDb Db(string connection) => new(new DbContextOptionsBuilder<AccountingDb>().UseNpgsql(connection).Options);
    static MovementRequest Transfer(string key, decimal q = 80) => new(key, A, B, S, q);
    static async Task<(PostgreSqlContainer Container, string Connection)> Start()
    {
        var c = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await c.StartAsync();
        await using var db = Db(c.GetConnectionString());
        await DatabaseSetup.Create(db);
        await DemoSeed.Run(db);
        return (c, c.GetConnectionString());
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task ConcurrentTransfersOnlyOneSucceeds()
    {
        var(c, cs) = await Start();
        await using var container = c;
        async Task<bool> Run(string key)
        {
            await using var db = Db(cs);
            try
            {
                await new AccountingService(db).Execute("Transfer", Transfer(key));
                return true;
            }
            catch (BusinessException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(Run("one"), Run("two"));
        Assert.Single(results, x => x);
        await using var check = Db(cs);
        Assert.Equal(20, (await check.Positions.FindAsync(A, S))!.Quantity);
        Assert.Equal(80, (await check.Positions.FindAsync(B, S))!.Quantity);
        Assert.Equal(2, await check.Operations.CountAsync());
        Assert.Equal(3, await check.Entries.CountAsync());
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task RetryAndConflict()
    {
        var(c, cs) = await Start();
        await using var container = c;
        Guid id;
        await using (var db = Db(cs))
            id = (await new AccountingService(db).Execute("Transfer", Transfer("same"))).Id;
        await using (var db = Db(cs))
            Assert.Equal(id, (await new AccountingService(db).Execute("Transfer", Transfer("same", 80.000m))).Id);
        await using (var db = Db(cs))
            Assert.Equal(409, (await Assert.ThrowsAsync<BusinessException>(() => new AccountingService(db).Execute("Transfer", Transfer("same", 1)))).Status);
        await using var check = Db(cs);
        Assert.Equal(2, await check.Operations.CountAsync());
        Assert.Equal(2, await check.Set<OutboxMessage>().CountAsync());
        Assert.Equal(0, await check.Entries.Where(x => x.OperationId == id).SumAsync(x => x.QuantityDelta));
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task RollbackAndReverse()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using (var db = Db(cs))
            await Assert.ThrowsAsync<BusinessException>(() => new AccountingService(db).Execute("Transfer", Transfer("fail", 101)));
        await using (var db = Db(cs))
        {
            Assert.Equal(100, (await db.Positions.FindAsync(A, S))!.Quantity);
            Assert.Equal(1, await db.Entries.CountAsync());
            Assert.Equal(1, await db.Set<OutboxMessage>().CountAsync());
        }

        Guid id;
        await using (var db = Db(cs))
            id = (await new AccountingService(db).Execute("Transfer", Transfer("ok"))).Id;
        await using (var db = Db(cs))
            await new AccountingService(db).Reverse(id, new("reverse", "Исправление"));
        await using (var db = Db(cs))
            await Assert.ThrowsAsync<BusinessException>(() => new AccountingService(db).Reverse(id, new("reverse-again", "Повтор")));
        await using var check = Db(cs);
        Assert.Equal(100, (await check.Positions.FindAsync(A, S))!.Quantity);
        Assert.Equal(0, (await check.Positions.FindAsync(B, S))!.Quantity);
        Assert.Equal(5, await check.Entries.CountAsync());
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task DuplicateEventHasOneInboxReceipt()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using var db = Db(cs);
        var op = await db.Operations.FirstAsync();
        var message = new Securities.Contracts.OperationCompleted(Guid.NewGuid(), op.Id, DateTimeOffset.UtcNow);
        var consumer = new OperationConsumer(db);
        await consumer.Record(message);
        await consumer.Record(message);
        await consumer.Record(message with { EventId = Guid.NewGuid() });
        Assert.Equal(1, await db.Set<InboxMessage>().CountAsync());
        Assert.Equal(100, (await db.Positions.FindAsync(A, S))!.Quantity);
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task LedgerCannotBeChanged()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using var db = Db(cs);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"Entries\" SET \"QuantityDelta\"=1"));
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task DatabasePreventsNegativePositions()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using var db = Db(cs);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Positions\" SET \"Quantity\" = -1 WHERE \"CustodyAccountId\" = {A}"));
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [PostgresFact]
    public async Task MissingQuoteAndValuation()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using var db = Db(cs);
        var service = new PortfolioService(db);
        await service.Value(new(Guid.Parse("20000000-0000-0000-0000-000000000001"), DateOnly.FromDateTime(DateTime.UtcNow), 0, "RUB"), default);
        await db.Set<SecurityPrice>().ExecuteDeleteAsync();
        await Assert.ThrowsAsync<BusinessException>(() => service.Value(new(Guid.Parse("20000000-0000-0000-0000-000000000001"), DateOnly.FromDateTime(DateTime.UtcNow), 0, "RUB"), default));
    }

    /// <summary>Проверяет атомарность при ошибке SQL внутри записи Outbox.</summary>
    [PostgresFact]
    public async Task DatabaseFailureRollsBackAllChanges()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using (var setup = Db(cs))
        {
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION fail_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Тестовая ошибка записи'; END; $$;
                CREATE TRIGGER fail_outbox BEFORE INSERT ON "OutboxMessage" FOR EACH ROW EXECUTE FUNCTION fail_outbox();
                """);
        }

        await using (var db = Db(cs))
            await Assert.ThrowsAsync<DbUpdateException>(() => new AccountingService(db).Execute("Transfer", Transfer("sql-failure")));
        await using var check = Db(cs);
        Assert.Equal(100, (await check.Positions.FindAsync(A, S))!.Quantity);
        Assert.Null(await check.Positions.FindAsync(B, S));
        Assert.Equal(1, await check.Operations.CountAsync());
        Assert.Equal(1, await check.Entries.CountAsync());
        Assert.Equal(1, await check.Set<OutboxMessage>().CountAsync());
    }

    /// <summary>Проверяет сериализацию одинакового ключа на разных соединениях.</summary>
    [PostgresFact]
    public async Task ConcurrentSameKeyReturnsSameOperation()
    {
        var(c, cs) = await Start();
        await using var container = c;
        async Task<Guid> Run()
        {
            await using var db = Db(cs);
            return (await new AccountingService(db).Execute("Transfer", Transfer("concurrent-key"))).Id;
        }

        var ids = await Task.WhenAll(Run(), Run());
        Assert.Equal(ids[0], ids[1]);
        await using var check = Db(cs);
        Assert.Equal(2, await check.Operations.CountAsync());
        Assert.Equal(20, (await check.Positions.FindAsync(A, S))!.Quantity);
    }

    /// <summary>Сверка сохраняет расхождения, включая отсутствующую внутреннюю позицию.</summary>
    [PostgresFact]
    public async Task ReconciliationSavesUnionWithoutChangingPositions()
    {
        var(c, cs) = await Start();
        await using var container = c;
        await using var db = Db(cs);
        var fund = await db.Funds.SingleAsync();
        var run = await new PortfolioService(db).Reconcile(new(fund.Id, [new(A, S, 99), new(B, S, 7)]), default);
        Assert.Equal(2, run.Items.Count);
        Assert.Contains(run.Items, x => x.CustodyAccountId == A && x.Expected == 99 && x.Actual == 100);
        Assert.Contains(run.Items, x => x.CustodyAccountId == B && x.Expected == 7 && x.Actual == 0);
        Assert.Equal(100, (await db.Positions.FindAsync(A, S))!.Quantity);
        Assert.Equal(2, await db.Set<ReconciliationItem>().CountAsync());
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [PostgresFact]
    public async Task ConcurrentCreditDebitAndOpposingTransfers()
    {
        var (c, cs) = await Start(); await using var container = c;
        async Task Run(string type, string key, Guid from, Guid? to, decimal quantity)
        {
            await using var db = Db(cs);
            await new AccountingService(db).Execute(type, new(key, from, to, S, quantity, "external", "doc"));
        }
        await Task.WhenAll(Enumerable.Range(0, 10).SelectMany(i => new[] {
            Run("Credit", "credit-"+i, A, null, 1.12345678m),
            Run("Debit", "debit-"+i, A, null, 1.12345678m) })).WaitAsync(TimeSpan.FromSeconds(30));
        await Run("Transfer", "initial-b", A, B, 40);
        await Task.WhenAll(Run("Transfer", "forward", A, B, 10), Run("Transfer", "backward", B, A, 10)).WaitAsync(TimeSpan.FromSeconds(30));
        await using var check = Db(cs);
        Assert.Equal(60m, (await check.Positions.FindAsync(A,S))!.Quantity);
        Assert.Equal(40m, (await check.Positions.FindAsync(B,S))!.Quantity);
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [PostgresFact]
    public async Task ConcurrentReverseAndAdversarialKeys()
    {
        var (c, cs) = await Start(); await using var container = c;
        Guid first, second;
        await using (var db = Db(cs)) first=(await new AccountingService(db).Execute("Transfer", Transfer("first", 10))).Id;
        await using (var db = Db(cs)) second=(await new AccountingService(db).Execute("Transfer", Transfer("second", 10))).Id;
        async Task<bool> Reverse(Guid id,string key)
        {
            await using var db = Db(cs);
            try { await new AccountingService(db).Reverse(id,new(key,"reason")); return true; }
            catch (BusinessException) { return false; }
        }
        var results=await Task.WhenAll(Reverse(first,"reverse:"+second),Reverse(second,"reverse:"+first)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(results, Assert.True);
        Guid third;
        await using (var db = Db(cs)) third=(await new AccountingService(db).Execute("Transfer", Transfer("third", 10))).Id;
        results=await Task.WhenAll(Reverse(third,"r1"),Reverse(third,"r2")).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results,x=>x);
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [PostgresFact]
    public async Task PrecisionAndRepeatedSeed()
    {
        var (c, cs) = await Start(); await using var container = c;
        await using (var db = Db(cs)) await new AccountingService(db).Execute("Credit",new("large",A,null,S,9007199254740993.12345678m,"external","doc"));
        await using (var db = Db(cs)) await new AccountingService(db).Execute("Credit",new("fraction",A,null,S,0.00000001m,"external","doc"));
        await using (var db = Db(cs)) await DemoSeed.Run(db);
        await using var check=Db(cs);
        Assert.Equal(9007199254741093.12345679m,(await check.Positions.FindAsync(A,S))!.Quantity);
        Assert.Equal(3, await check.Operations.CountAsync());
        Assert.Single(await check.Funds.ToListAsync());
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [PostgresFact]
    public async Task SqlRejectsTransferWithoutEntries()
    {
        var (c, cs) = await Start(); await using var container = c;
        await using var db=Db(cs);
        db.Operations.Add(new Operation {Id=Guid.NewGuid(),FundId=(await db.Funds.SingleAsync()).Id,Type="Transfer",IdempotencyKey="invalid",Fingerprint="invalid",CreatedAt=DateTimeOffset.UtcNow,CompletedAt=DateTimeOffset.UtcNow});
        var error = await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
        Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
    }

    /// <summary>Validates internal transfer invariants through SQL constraints, without service validation.</summary>
    [PostgresFact]
    public async Task SqlRejectsWrongAccountsSecuritiesAndFunds()
    {
        var (c,cs)=await Start(); await using var container=c;
        foreach(var scenario in new[]{"same-account","different-security","different-fund"})
        {
            await using var db=Db(cs);
            var fund=(await db.Funds.SingleAsync()).Id;
            var target=B; var secondSecurity=S;
            if(scenario=="same-account") target=A;
            if(scenario=="different-security")
            {
                secondSecurity=Guid.NewGuid();
                db.Securities.Add(new Security {Id=secondSecurity,ISIN="OTHER0000001",Ticker="OTHER",Name="test"});
                await db.SaveChangesAsync();
            }
            if(scenario=="different-fund")
            {
                var other=Guid.NewGuid();
                db.Funds.Add(new Fund {Id=other,Code="OTHER",Name="test",ManagementCompanyId=(await db.Funds.SingleAsync()).ManagementCompanyId});
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Accounts\" SET \"FundId\"={other} WHERE \"Id\"={B}");
            }
            var operation=Guid.NewGuid();
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                db.Operations.Add(new Operation {Id=operation,FundId=fund,Type="Transfer",IdempotencyKey=scenario,Fingerprint="test",CreatedAt=DateTimeOffset.UtcNow,CompletedAt=DateTimeOffset.UtcNow});
                db.Entries.AddRange(new LedgerEntry {Id=Guid.NewGuid(),OperationId=operation,CustodyAccountId=A,SecurityId=S,QuantityDelta=-1,CreatedAt=DateTimeOffset.UtcNow},
                    new LedgerEntry {Id=Guid.NewGuid(),OperationId=operation,CustodyAccountId=target,SecurityId=secondSecurity,QuantityDelta=1,CreatedAt=DateTimeOffset.UtcNow});
                // The deferred constraint permits INSERTs, but must reject COMMIT.
                await db.SaveChangesAsync();
                var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => tx.CommitAsync());
                Assert.Equal("23514", error.SqlState);
            }
            await using var check = Db(cs);
            Assert.False(await check.Operations.AnyAsync(x => x.Id == operation));
            Assert.False(await check.Entries.AnyAsync(x => x.OperationId == operation));
        }
    }

    /// <summary>An interrupted initial credit is resumed; concurrent reruns do not duplicate it.</summary>
    [PostgresFact]
    public async Task SeedRecoversAfterInterruptedCredit()
    {
        await using var c=new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build(); await c.StartAsync();
        var cs=c.GetConnectionString();
        await using(var db=Db(cs))
        {
            await DatabaseSetup.Create(db);
            await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_seed() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'seed failure'; END; $$; CREATE TRIGGER fail_seed BEFORE INSERT ON \"OutboxMessage\" FOR EACH ROW EXECUTE FUNCTION fail_seed();");
            await Assert.ThrowsAsync<DbUpdateException>(()=>DemoSeed.Run(db));
        }
        await using(var db=Db(cs))
        {
            Assert.Single(await db.Funds.ToListAsync()); Assert.Empty(await db.Operations.ToListAsync());
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_seed ON \"OutboxMessage\"");
        }
        async Task Seed(){await using var db=Db(cs);await DemoSeed.Run(db);}
        await Task.WhenAll(Seed(),Seed()).WaitAsync(TimeSpan.FromSeconds(30));
        await using var check=Db(cs);
        Assert.Single(await check.Operations.ToListAsync()); Assert.Single(await check.Set<OutboxMessage>().ToListAsync());
        Assert.Equal(100m,(await check.Positions.FindAsync(A,S))!.Quantity);
    }
}

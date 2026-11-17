using System.Data;
using Microsoft.EntityFrameworkCore;
using Securities.Application;
using Securities.Domain;

namespace Securities.Infrastructure;
/// <summary>Упрощённая оценка и ручная сверка снимка позиций.</summary>
public sealed class PortfolioService(AccountingDb db)
{
    /// <summary>Оценивает количество по обязательной котировке.</summary>
    public async Task<object> Value(ValuationRequest r, CancellationToken ct)
    {
        if (r.Liabilities < 0 || (string.IsNullOrWhiteSpace(r.Currency) || !System.Text.RegularExpressions.Regex.IsMatch(r.Currency, "^[A-Z]{3}$")) || r.Date > DateOnly.FromDateTime(DateTime.UtcNow))
            throw new BusinessException("Проверьте дату, валюту и обязательства.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await db.Funds.AnyAsync(x => x.Id == r.FundId, ct))
            throw new BusinessException("Фонд не найден.", 404);
        // Историческая оценка строится по неизменяемому журналу на конец даты UTC.
        var cutoff = new DateTimeOffset(r.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var quantities = await (
            from e in db.Entries
            join a in db.Accounts on e.CustodyAccountId equals a.Id
            where a.FundId == r.FundId && e.CreatedAt < cutoff
            group e by e.SecurityId into g
                select new
                {
                    SecurityId = g.Key,
                    Quantity = g.Sum(x => x.QuantityDelta)
                }

        ).ToListAsync(ct);
        var components = new List<(decimal Quantity, decimal Price)>();
        foreach (var p in quantities.Where(x => x.Quantity != 0))
        {
            var price = await db.Set<SecurityPrice>().SingleOrDefaultAsync(x => x.SecurityId == p.SecurityId && x.PriceDate == r.Date, ct);
            if (price is null)
                throw new BusinessException($"Нет котировки {p.SecurityId} на {r.Date}.");
            var security = await db.Securities.SingleAsync(x => x.Id == p.SecurityId, ct);
            if (price.Currency != r.Currency || security.Currency != r.Currency)
                throw new BusinessException("Валюты различаются; конвертация в MVP отсутствует.");
            components.Add((p.Quantity, price.Price));
        }

        var units = await db.Set<FundUnits>().Where(x => x.FundId == r.FundId && x.EffectiveDate <= r.Date).OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (units is null || units.UnitsOutstanding <= 0)
            throw new BusinessException("Нет положительного количества паёв.");
        var valuation = await db.Set<FundValuation>().FindAsync([r.FundId, r.Date], ct);
        if (valuation is null)
        {
            valuation = new FundValuation
            {
                FundId = r.FundId,
                ValuationDate = r.Date
            };
            db.Add(valuation);
        }

        var assets = AccountingRules.TotalValue(components);
        valuation.TotalAssets = assets;
        valuation.TotalLiabilities = AccountingRules.Normalize(r.Liabilities);
        valuation.NAV = AccountingRules.Normalize(assets - valuation.TotalLiabilities);
        valuation.Currency = r.Currency;
        var unitPrice = AccountingRules.UnitPrice(valuation.NAV, units.UnitsOutstanding);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new
        {
            Valuation = valuation,
            UnitPrice = unitPrice
        };
    }

    /// <summary>Сохраняет сверку полного набора контрольных остатков.</summary>
    public async Task<ReconciliationRun> Reconcile(ReconcileRequest r, CancellationToken ct)
    {
        if (r.Balances is null || r.Balances.Any(x => x is null || x.Quantity < 0) || r.Balances.GroupBy(x => (x.CustodyAccountId, x.SecurityId)).Any(x => x.Count() > 1))
            throw new BusinessException("Контрольные остатки должны быть уникальны и неотрицательны.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await db.Funds.AnyAsync(x => x.Id == r.FundId, ct))
            throw new BusinessException("Фонд не найден.", 404);
        var accounts = await db.Accounts.Where(x => x.FundId == r.FundId).Select(x => x.Id).ToListAsync(ct);
        var securityIds = await db.Securities.Select(x => x.Id).ToListAsync(ct);
        if (r.Balances.Any(x => !accounts.Contains(x.CustodyAccountId) || !securityIds.Contains(x.SecurityId)))
            throw new BusinessException("Контрольные данные содержат чужой счёт или неизвестную бумагу.");
        var actual = await db.Positions.Where(x => accounts.Contains(x.CustodyAccountId)).ToListAsync(ct);
        var expected = r.Balances.ToDictionary(x => (x.CustodyAccountId, x.SecurityId), x => x.Quantity);
        var keys = expected.Keys.Union(actual.Select(x => (x.CustodyAccountId, x.SecurityId)));
        var run = new ReconciliationRun
        {
            Id = Guid.NewGuid(),
            FundId = r.FundId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        foreach (var key in keys)
            run.Items.Add(new ReconciliationItem { Id = Guid.NewGuid(), CustodyAccountId = key.CustodyAccountId, SecurityId = key.SecurityId, Expected = expected.GetValueOrDefault(key), Actual = actual.FirstOrDefault(x => x.CustodyAccountId == key.CustodyAccountId && x.SecurityId == key.SecurityId)?.Quantity ?? 0 });
        db.Add(run);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return run;
    }
}

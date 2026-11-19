using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Securities.Application;
using Securities.Domain;

namespace Securities.Infrastructure;
/// <summary>Проводит операции в одной транзакции с журналом и Outbox.</summary>
public sealed class AccountingService(AccountingDb db)
{
    static string Fingerprint(object request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    static void Key(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            throw new BusinessException("IdempotencyKey обязателен, максимум 128 символов.", 400);
    }

    async Task LockKey(string key, CancellationToken ct, int lockNamespace = 1)
    {
        // Коллизия хеша только сериализует независимые запросы; уникальность обеспечивает индекс.
        // pg_advisory_xact_lock - логическая блокировка (не строки таблицы), тут применяю чтобы избежать гонки (race condition)
        //
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockNamespace}, hashtext({key}))", ct);
    }

    async Task<Operation?> Replay(string key, string fingerprint, CancellationToken ct)
    {
        var existing = await db.Operations.SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing is not null)
            AccountingRules.SameRequest(existing.Fingerprint, fingerprint);
        return existing;
    }

    async Task<List<CustodyAccount>> LockAccounts(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new List<CustodyAccount>();
        // Одинаковый порядок блокировок исключает взаимную блокировку встречных переводов.
        foreach (var id in ids.Distinct().Order())
        {
            var account = await db.Accounts.FromSqlInterpolated($"SELECT * FROM \"Accounts\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new BusinessException("Счёт не найден.", 404);
            if (account.Status != "Active")
                throw new BusinessException("Счёт неактивен.");
            result.Add(account);
        }

        foreach (var fundId in result.Select(x => x.FundId).Distinct())
            if (!await db.Funds.AnyAsync(x => x.Id == fundId && x.Status == "Active", ct))
                throw new BusinessException("Фонд неактивен.");
        return result;
    }

    async Task Commit(Operation operation, IEnumerable<(Guid Account, Guid Security, decimal Delta)> changes, CancellationToken ct)
    {
        foreach (var change in changes)
        {
            var p = await db.Positions.FindAsync([change.Account, change.Security], ct);
            if (p is null)
            {
                p = new Position
                {
                    CustodyAccountId = change.Account,
                    SecurityId = change.Security
                };
                db.Positions.Add(p);
            }

            p.Quantity = AccountingRules.Apply(p.Quantity, change.Delta);
            p.Version++;
            db.Entries.Add(new LedgerEntry { Id = Guid.NewGuid(), OperationId = operation.Id, CustodyAccountId = change.Account, SecurityId = change.Security, QuantityDelta = change.Delta, CreatedAt = operation.CreatedAt });
        }

        db.Operations.Add(operation);
        db.Add(new OutboxMessage { Id = Guid.NewGuid(), OperationId = operation.Id, CreatedAt = operation.CreatedAt });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Повтор с тем же ключом возвращает сохранённый результат.</summary>
    public async Task<Operation> Execute(string type, MovementRequest r, CancellationToken ct = default)
    {
        Key(r.IdempotencyKey);
        AccountingRules.Positive(r.Quantity);
        if (type is not ("Credit" or "Debit" or "Transfer"))
            throw new BusinessException("Неизвестный тип.", 400);
        if (type != "Transfer" && (string.IsNullOrWhiteSpace(r.MovementParty) || string.IsNullOrWhiteSpace(r.DocumentReference)))
            throw new BusinessException("Укажите источник/назначение и документ.", 400);
        if (type == "Transfer" && (r.TargetAccountId is null || r.TargetAccountId == r.SourceAccountId))
            throw new BusinessException("Укажите другой счёт получателя.", 400);
        // Числовые значения 80 и 80.00 должны давать один результат повтора.
        var fingerprint = Fingerprint(new { type, r.IdempotencyKey, r.SourceAccountId, r.TargetAccountId, r.SecurityId, Quantity = r.Quantity.ToString("G29", System.Globalization.CultureInfo.InvariantCulture), r.MovementParty, r.DocumentReference, r.ExternalId });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockKey(r.IdempotencyKey, ct);
        var previous = await Replay(r.IdempotencyKey, fingerprint, ct);
        if (previous is not null)
            return previous;
        var accounts = await LockAccounts(type == "Transfer" ? [r.SourceAccountId, r.TargetAccountId!.Value] : [r.SourceAccountId], ct);
        if (type == "Transfer")
            AccountingRules.SameFund(accounts[0].FundId, accounts[1].FundId);
        if (!await db.Securities.AnyAsync(x => x.Id == r.SecurityId && x.IsActive, ct))
            throw new BusinessException("Бумага отсутствует или неактивна.");
        var op = new Operation
        {
            Id = Guid.NewGuid(),
            Type = type,
            FundId = accounts[0].FundId,
            IdempotencyKey = r.IdempotencyKey,
            Fingerprint = fingerprint,
            ExternalId = r.ExternalId,
            MovementParty = r.MovementParty,
            DocumentReference = r.DocumentReference,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        var changes = MovementPlan.Create(type, r.SourceAccountId, r.TargetAccountId, r.SecurityId, r.Quantity);
        await Commit(op, changes, ct);
        await tx.CommitAsync(ct);
        return op;
    }

    /// <summary>Создаёт обратные проводки и сохраняет ссылку на исходную операцию.</summary>
    public async Task<Operation> Reverse(Guid id, ReverseRequest r, CancellationToken ct = default)
    {
        Key(r.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(r.Reason))
            throw new BusinessException("Укажите причину.", 400);
        var fingerprint = Fingerprint(new { id, r });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockKey(r.IdempotencyKey, ct);
        var previous = await Replay(r.IdempotencyKey, fingerprint, ct);
        if (previous is not null)
            return previous;
        await LockKey(id.ToString(), ct, 2);
        var original = await db.Operations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("Операция не найдена.", 404);
        if (original.Type == "Reverse")
            throw new BusinessException("Корректировка корректировки запрещена.");
        AccountingRules.Reversible(await db.Operations.AnyAsync(x => x.ReversesOperationId == id, ct));
        var entries = await db.Entries.Where(x => x.OperationId == id).ToListAsync(ct);
        await LockAccounts(entries.Select(x => x.CustodyAccountId), ct);
        foreach (var securityId in entries.Select(x => x.SecurityId).Distinct())
            if (!await db.Securities.AnyAsync(x => x.Id == securityId && x.IsActive, ct))
                throw new BusinessException("Бумага неактивна.");
        var op = new Operation
        {
            Id = Guid.NewGuid(),
            Type = "Reverse",
            FundId = original.FundId,
            ReversesOperationId = id,
            IdempotencyKey = r.IdempotencyKey,
            Fingerprint = fingerprint,
            MovementParty = original.MovementParty,
            DocumentReference = r.Reason,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await Commit(op, entries.Select(x => (x.CustodyAccountId, x.SecurityId, -x.QuantityDelta)), ct);
        await tx.CommitAsync(ct);
        return op;
    }
}

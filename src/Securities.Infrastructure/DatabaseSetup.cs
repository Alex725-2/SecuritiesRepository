using Microsoft.EntityFrameworkCore;

namespace Securities.Infrastructure;
/// <summary>Создание демонстрационной БД и дополнительных инвариантов PostgreSQL.</summary>
public static class DatabaseSetup
{
    /// <summary>Данные IntegritySql.</summary>
    public const string IntegritySql = """
-- Неизменяемость проводок обеспечивается также для прямых SQL-команд.
CREATE OR REPLACE FUNCTION reject_ledger_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Проводки неизменяемы' USING ERRCODE = '23514'; END; $$;
DROP TRIGGER IF EXISTS ledger_immutable ON "Entries";
CREATE TRIGGER ledger_immutable BEFORE UPDATE OR DELETE ON "Entries" FOR EACH ROW EXECUTE FUNCTION reject_ledger_change();
-- Проверка итогового журнала отложена до завершения транзакции.
CREATE OR REPLACE FUNCTION validate_operation_ledger() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE operation_type text; entry_count integer; total numeric; operation_id uuid;
BEGIN
 IF TG_TABLE_NAME = 'Operations' THEN operation_id := NEW."Id";
 ELSE operation_id := NEW."OperationId"; END IF;
 SELECT "Type" INTO operation_type FROM "Operations" WHERE "Id" = operation_id;
 SELECT count(*), coalesce(sum("QuantityDelta"),0) INTO entry_count,total FROM "Entries" WHERE "OperationId" = operation_id;
 IF operation_type = 'Transfer' AND (entry_count <> 2 OR total <> 0 OR
 (SELECT count(DISTINCT "CustodyAccountId") FROM "Entries" WHERE "OperationId" = operation_id) <> 2 OR
 (SELECT count(DISTINCT "SecurityId") FROM "Entries" WHERE "OperationId" = operation_id) <> 1 OR
 EXISTS (SELECT 1 FROM "Entries" e JOIN "Accounts" a ON a."Id" = e."CustodyAccountId"
 JOIN "Operations" o ON o."Id" = e."OperationId" WHERE o."Id" = operation_id AND a."FundId" <> o."FundId")) THEN RAISE EXCEPTION 'Несбалансированный перевод' USING ERRCODE = '23514'; END IF;
 RETURN NEW;
END; $$;
DROP TRIGGER IF EXISTS ledger_balanced ON "Entries";
CREATE CONSTRAINT TRIGGER ledger_balanced AFTER INSERT ON "Entries" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_operation_ledger();
DROP TRIGGER IF EXISTS operation_balanced ON "Operations";
CREATE CONSTRAINT TRIGGER operation_balanced AFTER INSERT OR UPDATE ON "Operations" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION validate_operation_ledger();
""";
    /// <summary>Создаёт результат с проверкой инвариантов.</summary>
    public static async Task Create(AccountingDb db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        await db.Database.ExecuteSqlRawAsync(IntegritySql, ct);
    }
}

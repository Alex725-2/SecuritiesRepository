namespace Securities.Application;
/// <summary>Команда движения. Для внешних движений обязательны контрагент и документ.</summary>
public record MovementRequest(string IdempotencyKey, Guid SourceAccountId, Guid? TargetAccountId, Guid SecurityId, decimal Quantity, string? MovementParty = null, string? DocumentReference = null, string? ExternalId = null);
/// <summary>Ключ повторяемой корректировки и её основание.</summary>
public record ReverseRequest(string IdempotencyKey, string Reason);
/// <summary>Контрольный остаток по паре счёт и бумага.</summary>
public record ControlBalance(Guid CustodyAccountId, Guid SecurityId, decimal Quantity);
/// <summary>Полный контрольный набор для сверки фонда.</summary>
public record ReconcileRequest(Guid FundId, List<ControlBalance> Balances);
/// <summary>Дата, обязательства и единая валюта оценки.</summary>
public record ValuationRequest(Guid FundId, DateOnly Date, decimal Liabilities, string Currency);

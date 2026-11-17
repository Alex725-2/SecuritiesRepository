namespace Securities.Contracts;
/// <summary>Факт завершения учётной операции; повторная доставка допустима.</summary>
public record OperationCompleted(Guid EventId, Guid OperationId, DateTimeOffset CompletedAt);

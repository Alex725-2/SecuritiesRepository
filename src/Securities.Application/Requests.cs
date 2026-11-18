namespace Securities.Application;
/// <summary>Команда движения. Для внешних движений обязательны контрагент и документ.</summary>
/// <param name="IdempotencyKey">Уникальный ключ команды, до 128 символов. При повторе отправьте тот же ключ и те же параметры. Сервер вернёт сохранённую операцию. При изменении параметров с тем же ключом ответит 409.</param>
/// <param name="SourceAccountId">Счёт хранения фонда. Для Credit это счёт зачисления, для Debit и Transfer — счёт списания.</param>
/// <param name="TargetAccountId">Счёт зачисления для Transfer. Должен отличаться от счёта списания и принадлежать тому же фонду.</param>
/// <param name="SecurityId">Идентификатор активной ценной бумаги.</param>
/// <param name="Quantity">Положительное количество в диапазоне numeric(28,8), до 20 целых и 8 дробных знаков. Передаётся JSON-строкой, например "25".</param>
/// <param name="MovementParty">Внешний источник для Credit или получатель для Debit. Обязательно для обоих видов внешнего движения.</param>
/// <param name="DocumentReference">Номер или ссылка на документ-основание. Обязательно для Credit и Debit.</param>
/// <param name="ExternalId">Необязательная ссылка на запись во внешней системе. Для повторной отправки используется idempotencyKey.</param>
public record MovementRequest(string IdempotencyKey, Guid SourceAccountId, Guid? TargetAccountId, Guid SecurityId, decimal Quantity, string? MovementParty = null, string? DocumentReference = null, string? ExternalId = null);
/// <summary>Ключ повторяемой корректировки и её основание.</summary>
/// <param name="IdempotencyKey">Уникальный ключ команды корректировки, до 128 символов. Для повтора используйте тот же ключ, идентификатор исходной операции и причину.</param>
/// <param name="Reason">Обязательная причина корректировки. Сохраняется в documentReference операции Reverse.</param>
public record ReverseRequest(string IdempotencyKey, string Reason);
/// <summary>Контрольный остаток по паре счёт и бумага.</summary>
/// <param name="CustodyAccountId">Идентификатор счёта хранения выбранного фонда.</param>
/// <param name="SecurityId">Идентификатор бумаги из справочника.</param>
/// <param name="Quantity">Неотрицательный контрольный остаток. Передаётся JSON-строкой, например "25".</param>
public record ControlBalance(Guid CustodyAccountId, Guid SecurityId, decimal Quantity);
/// <summary>Полный контрольный набор для сверки фонда.</summary>
/// <param name="FundId">Идентификатор сверяемого фонда.</param>
/// <param name="Balances">Полный контрольный набор. Каждая пара счёт и бумага встречается один раз. Пропущенная пара считается нулевой.</param>
public record ReconcileRequest(Guid FundId, List<ControlBalance> Balances);
/// <summary>Дата, обязательства и единая валюта оценки.</summary>
/// <param name="FundId">Идентификатор оцениваемого фонда.</param>
/// <param name="Date">Дата оценки на конец суток UTC в формате ГГГГ-ММ-ДД. Будущие даты отклоняются.</param>
/// <param name="Liabilities">Неотрицательные обязательства в валюте оценки. Передаются JSON-строкой, например "0".</param>
/// <param name="Currency">Единая валюта оценки и котировок. Три заглавные латинские буквы, например RUB.</param>
public record ValuationRequest(Guid FundId, DateOnly Date, decimal Liabilities, string Currency);

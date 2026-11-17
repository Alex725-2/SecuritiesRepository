namespace Securities.Domain;
/// <summary>Ошибка нарушения правил учёта.</summary>
public sealed class BusinessException(string message, int status = 422) : Exception(message)
{
    /// <summary>Состояние записи.</summary>
    public int Status { get; } = status;
}

/// <summary>Управляющая компания фонда.</summary>
public class ManagementCompany
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Наименование.</summary>
    public string Name { get; set; } = "";
    /// <summary>Идентификатор налогоплательщика.</summary>
    public string INN { get; set; } = "";
}

/// <summary>Инвестиционный фонд; исторические оценки хранятся отдельно.</summary>
public class Fund
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid ManagementCompanyId { get; set; }
    /// <summary>Уникальный код справочника.</summary>
    public string Code { get; set; } = "";
    /// <summary>Наименование.</summary>
    public string Name { get; set; } = "";
    /// <summary>Данные FundType.</summary>
    public string FundType { get; set; } = "Open";
    /// <summary>Состояние записи.</summary>
    public string Status { get; set; } = "Active";
}

/// <summary>Ценная бумага с валютой номинала.</summary>
public class Security
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Данные ISIN.</summary>
    public string ISIN { get; set; } = "";
    /// <summary>Данные Ticker.</summary>
    public string Ticker { get; set; } = "";
    /// <summary>Наименование.</summary>
    public string Name { get; set; } = "";
    /// <summary>Трёхбуквенный код валюты.</summary>
    public string Currency { get; set; } = "RUB";
    /// <summary>Данные SecurityType.</summary>
    public string SecurityType { get; set; } = "Share";
    /// <summary>Данные IsActive.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Депозитарий хранения активов.</summary>
public class Custodian
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Уникальный код справочника.</summary>
    public string Code { get; set; } = "";
    /// <summary>Наименование.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Торговая площадка, не депозитарий хранения.</summary>
public class Exchange
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Уникальный код справочника.</summary>
    public string Code { get; set; } = "";
    /// <summary>Наименование.</summary>
    public string Name { get; set; } = "";
}

/// <summary>Счёт хранения одного фонда в депозитарии.</summary>
public class CustodyAccount
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid FundId { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid CustodianId { get; set; }
    /// <summary>Данные AccountNumber.</summary>
    public string AccountNumber { get; set; } = "";
    /// <summary>Состояние записи.</summary>
    public string Status { get; set; } = "Active";
}

/// <summary>Текущий остаток бумаги на счёте.</summary>
public class Position
{
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid CustodyAccountId { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid SecurityId { get; set; }
    /// <summary>Текущий неотрицательный остаток.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Версия позиции для контроля конкурентности.</summary>
    public long Version { get; set; }
}

/// <summary>Завершённая учётная операция и её идемпотентный результат.</summary>
public class Operation
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Данные Type.</summary>
    public string Type { get; set; } = "";
    /// <summary>Состояние записи.</summary>
    public string Status { get; set; } = "Completed";
    /// <summary>Ссылка на связанную сущность.</summary>
    public string? ExternalId { get; set; }
    /// <summary>Уникальный ключ повторяемой команды.</summary>
    public string IdempotencyKey { get; set; } = "";
    /// <summary>Хеш типа и всех параметров команды.</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid FundId { get; set; }
    /// <summary>Исходная операция для единственной корректировки.</summary>
    public Guid? ReversesOperationId { get; set; }
    /// <summary>Источник зачисления или назначение списания.</summary>
    public string? MovementParty { get; set; }
    /// <summary>Подтверждающий документ либо основание корректировки.</summary>
    public string? DocumentReference { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Данные ErrorMessage.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Неизменяемая запись движения по счёту.</summary>
public class LedgerEntry
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid OperationId { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid CustodyAccountId { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid SecurityId { get; set; }
    /// <summary>Знаковое изменение количества бумаг.</summary>
    public decimal QuantityDelta { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Цена бумаги на конкретную дату.</summary>
public class SecurityPrice
{
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid SecurityId { get; set; }
    /// <summary>Дата действия исторической записи.</summary>
    public DateOnly PriceDate { get; set; }
    /// <summary>Неотрицательная цена единицы бумаги.</summary>
    public decimal Price { get; set; }
    /// <summary>Трёхбуквенный код валюты.</summary>
    public string Currency { get; set; } = "RUB";
    /// <summary>Источник вымышленной котировки.</summary>
    public string Source { get; set; } = "";
}

/// <summary>Сохранённая упрощённая оценка фонда.</summary>
public class FundValuation
{
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid FundId { get; set; }
    /// <summary>Дата действия исторической записи.</summary>
    public DateOnly ValuationDate { get; set; }
    /// <summary>Оценка активов в одной валюте.</summary>
    public decimal TotalAssets { get; set; }
    /// <summary>Обязательства фонда в валюте оценки.</summary>
    public decimal TotalLiabilities { get; set; }
    /// <summary>Разность стоимости активов и обязательств.</summary>
    public decimal NAV { get; set; }
    /// <summary>Трёхбуквенный код валюты.</summary>
    public string Currency { get; set; } = "RUB";
}

/// <summary>Историческое количество паёв.</summary>
public class FundUnits
{
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid FundId { get; set; }
    /// <summary>Дата действия исторической записи.</summary>
    public DateOnly EffectiveDate { get; set; }
    /// <summary>Количество паёв на дату, строго больше нуля.</summary>
    public decimal UnitsOutstanding { get; set; }
}

/// <summary>Снимок ручной сверки полного набора позиций фонда.</summary>
public class ReconciliationRun
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid FundId { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Данные Items.</summary>
    public List<ReconciliationItem> Items { get; set; } = [];
}

/// <summary>Сравнение контрольного и внутреннего остатка.</summary>
public class ReconciliationItem
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid ReconciliationRunId { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid CustodyAccountId { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid SecurityId { get; set; }
    /// <summary>Ожидаемый контрольный остаток.</summary>
    public decimal Expected { get; set; }
    /// <summary>Фактический внутренний остаток.</summary>
    public decimal Actual { get; set; }
}

/// <summary>Событие, ожидающее подтверждённой публикации.</summary>
public class OutboxMessage
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid OperationId { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
}

/// <summary>Аудиторский факт обработки события.</summary>
public class InboxMessage
{
    /// <summary>Уникальный идентификатор.</summary>
    public Guid Id { get; set; }
    /// <summary>Ссылка на связанную сущность.</summary>
    public Guid OperationId { get; set; }
    /// <summary>Момент фиксации события в UTC.</summary>
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>Чистые правила изменения позиции.</summary>
public static class AccountingRules
{
    /// <summary>Проверяет положительное количество и допустимую точность.</summary>
    public static void Positive(decimal quantity)
    {
        if (quantity <= 0 || decimal.Round(quantity, 8) != quantity || quantity >= 100000000000000000000m)
            throw new BusinessException("Количество должно быть положительным numeric(28,8).");
    }

    /// <summary>Rounds ToEven to storage scale and enforces numeric(28,8) bounds.</summary>
    public static decimal Normalize(decimal value)
    {
        var rounded = decimal.Round(value, 8, MidpointRounding.ToEven);
        if (rounded <= -100000000000000000000m || rounded >= 100000000000000000000m)
            throw new BusinessException("Value exceeds numeric(28,8).", 400);
        return rounded;
    }

    /// <summary>Applies a movement and enforces storage bounds.</summary>
    public static decimal Apply(decimal current, decimal delta)
    {
        var result = Normalize(current + delta);
        if (result < 0)
            throw new BusinessException("Недостаточный остаток.");
        return result;
    }

    /// <summary>Запрещает движение между разными фондами.</summary>
    public static void SameFund(Guid source, Guid target)
    {
        if (source != target)
            throw new BusinessException("Перевод между фондами запрещён.");
    }

    /// <summary>Проверяет совпадение параметров повторной команды.</summary>
    public static void SameRequest(string stored, string supplied)
    {
        if (stored != supplied)
            throw new BusinessException("Ключ идемпотентности использован с другими параметрами.", 409);
    }

    /// <summary>Запрещает вторую корректировку исходной операции.</summary>
    public static void Reversible(bool reversed)
    {
        if (reversed)
            throw new BusinessException("Операция уже скорректирована.", 409);
    }

    /// <summary>Оценивает количество по обязательной котировке.</summary>
    public static decimal Value(decimal quantity, decimal? price) => price is null ? throw new BusinessException("Отсутствует котировка.") : quantity * price.Value;
    /// <summary>Accumulates exact fixed-point products, then rounds once to eight places.</summary>
    public static decimal TotalValue(IEnumerable<(decimal Quantity, decimal Price)> components)
    {
        var total = components.Aggregate(System.Numerics.BigInteger.Zero,
            (sum, item) => sum + Scaled(item.Quantity) * Scaled(item.Price));
        return RoundRatio(total, 100000000);
    }

    static System.Numerics.BigInteger Scaled(decimal value) =>
        System.Numerics.BigInteger.Parse(Normalize(value).ToString("F8", System.Globalization.CultureInfo.InvariantCulture).Replace(".", ""), System.Globalization.CultureInfo.InvariantCulture);

    static decimal RoundRatio(System.Numerics.BigInteger total, System.Numerics.BigInteger denominator)
    {
        var quotient = System.Numerics.BigInteger.DivRem(total, denominator, out var remainder);
        var comparison = (System.Numerics.BigInteger.Abs(remainder) * 2).CompareTo(denominator);
        if (comparison > 0 || (comparison == 0 && !quotient.IsEven)) quotient += total.Sign;
        var negative = quotient.Sign < 0;
        var digits = System.Numerics.BigInteger.Abs(quotient).ToString().PadLeft(9, '0');
        if (!decimal.TryParse((negative ? "-" : "") + digits[..^8] + "." + digits[^8..],
            System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var result))
            throw new BusinessException("Valuation exceeds decimal range.", 422);
        return Normalize(result);
    }

    /// <summary>Рассчитывает стоимость пая, запрещая нулевое количество паёв.</summary>
    public static decimal UnitPrice(decimal nav, decimal units)
    {
        if (units <= 0) throw new BusinessException("Нет положительного количества паёв.");
        Positive(units);
        return RoundRatio(Scaled(nav) * 100000000, Scaled(units));
    }
}

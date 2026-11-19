using Securities.Domain;

namespace Securities.Application;

/// <summary>Расчёт стоимости позиции по последней допустимой котировке.</summary>
/// <param name="SecurityId">Идентификатор бумаги.</param>
/// <param name="Ticker">Тикер бумаги.</param>
/// <param name="Name">Наименование бумаги.</param>
/// <param name="Quantity">Суммарное количество по счетам фонда.</param>
/// <param name="Price">Котировка в валюте оценки.</param>
/// <param name="PriceDate">Фактическая дата использованной котировки.</param>
/// <param name="PositionValue">Точное произведение количества и цены в виде десятичной строки.</param>
public sealed record ValuationPosition(Guid SecurityId, string Ticker, string Name,
    decimal Quantity, decimal Price, DateOnly PriceDate, string PositionValue);

/// <summary>Сохранённая оценка и данные использованного расчёта.</summary>
/// <param name="Valuation">Сохранённые активы, обязательства и СЧА.</param>
/// <param name="UnitPrice">Расчётная стоимость пая.</param>
/// <param name="UnitsOutstanding">Количество паёв на дату оценки.</param>
/// <param name="Positions">Позиции, вошедшие в расчёт.</param>
public sealed record ValuationResponse(FundValuation Valuation, decimal UnitPrice,
    decimal UnitsOutstanding, IReadOnlyList<ValuationPosition> Positions);

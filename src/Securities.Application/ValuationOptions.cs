namespace Securities.Application;

/// <summary>Ограничения при выборе котировок для оценки фонда.</summary>
public sealed class ValuationOptions
{
    /// <summary>Предельный возраст котировки в календарных днях, включая границу.</summary>
    public int MaxPriceAgeDays { get; set; } = 30;
}

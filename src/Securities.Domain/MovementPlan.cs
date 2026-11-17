namespace Securities.Domain;
/// <summary>Правила формирования проводок внешнего движения и внутреннего перевода.</summary>
public static class MovementPlan
{
    /// <summary>Создаёт результат с проверкой инвариантов.</summary>
    public static IReadOnlyList<(Guid Account, Guid Security, decimal Delta)> Create(string type, Guid source, Guid? target, Guid security, decimal quantity)
    {
        AccountingRules.Positive(quantity);
        if (type == "Transfer")
        {
            if (target is null || target == source)
                throw new BusinessException("Укажите другой счёт получателя.");
            return[(source, security, -quantity), (target.Value, security, quantity)];
        }

        if (type == "Credit")
            return[(source, security, quantity)];
        if (type == "Debit")
            return[(source, security, -quantity)];
        throw new BusinessException("Неизвестный тип операции.");
    }
}

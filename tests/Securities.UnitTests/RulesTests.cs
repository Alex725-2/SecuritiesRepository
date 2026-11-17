using Securities.Domain;
using Xunit;

namespace Securities.UnitTests;
/// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
public class RulesTests
{
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuantityMustBePositive(decimal q) => Assert.Throws<BusinessException>(() => AccountingRules.Positive(q));
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void PositiveQuantityAccepted() => AccountingRules.Positive(1.25m);
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void InsufficientBalance() => Assert.Throws<BusinessException>(() => AccountingRules.Apply(100, -101));
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void DifferentFundsForbidden() => Assert.Throws<BusinessException>(() => AccountingRules.SameFund(Guid.NewGuid(), Guid.NewGuid()));
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void TransferEntriesBalance()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var security = Guid.NewGuid();
        var entries = MovementPlan.Create("Transfer", a, b, security, 80);
        Assert.Equal(2, entries.Count);
        Assert.Equal((a, security, -80m), entries[0]);
        Assert.Equal((b, security, 80m), entries[1]);
        Assert.Equal(0, entries.Sum(x => x.Delta));
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Theory]
    [InlineData("Credit", 10)]
    [InlineData("Debit", -10)]
    public void ExternalMovementHasSingleExplicitEntry(string type, decimal expected)
    {
        var entries = MovementPlan.Create(type, Guid.NewGuid(), null, Guid.NewGuid(), 10);
        Assert.Equal(expected, Assert.Single(entries).Delta);
    }

    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void SameAccountForbidden() => Assert.Throws<BusinessException>(() =>
    {
        var a = Guid.NewGuid();
        MovementPlan.Create("Transfer", a, a, Guid.NewGuid(), 1);
    });
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void RepeatedKeySamePayload() => AccountingRules.SameRequest("abc", "abc");
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void RepeatedKeyDifferentPayload() => Assert.Equal(409, Assert.Throws<BusinessException>(() => AccountingRules.SameRequest("abc", "def")).Status);
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void SecondReversalForbidden() => Assert.Throws<BusinessException>(() => AccountingRules.Reversible(true));
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void Valuation() => Assert.Equal(125, AccountingRules.Value(5, 25));
    /// <summary>Проверяет заявленный инвариант учётного ядра.</summary>
    [Fact]
    public void MissingPrice() => Assert.Throws<BusinessException>(() => AccountingRules.Value(5, null));
    /// <summary>Проверяет стоимость пая после вычитания обязательств.</summary>
    [Fact]
    public void UnitPriceUsesNetAssets() => Assert.Equal(95m, AccountingRules.UnitPrice(10000m - 500m, 100m));

    /// <summary>Не допускает деления на нулевое количество паёв.</summary>
    [Fact]
    public void ZeroUnitsForbidden() => Assert.Throws<BusinessException>(() => AccountingRules.UnitPrice(100, 0));
}

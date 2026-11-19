using System.Globalization;
using System.Text.Json;
using Securities.Application;
using Securities.Domain;
using Securities.Infrastructure;
using Xunit;

namespace Securities.UnitTests;

/// <summary>Verifies the accounting or transport contract.</summary>
public class PrecisionTests
{
    /// <summary>Verifies the accounting or transport contract.</summary>
    [Theory]
    [InlineData("9007199254740993.12345678")]
    [InlineData("0.00000001")]
    [InlineData("99999999999999999999.99999999")]
    public void DecimalJsonRoundTrip(string text)
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new DecimalJsonConverter());
        var value = decimal.Parse(text, CultureInfo.InvariantCulture);
        Assert.Equal('"' + text + '"', JsonSerializer.Serialize(value, options));
        Assert.Equal(value, JsonSerializer.Deserialize<decimal>('"' + text + '"', options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>(text, options));
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [Fact]
    public void ValuationRoundsOnceAndPreservesLargeFractions()
    {
        Assert.Equal("0.000000005", AccountingRules.ExactPositionValue(0.00000001m, 0.5m));
        Assert.Equal("9007199254740993.12345678", AccountingRules.ExactPositionValue(9007199254740993.12345678m, 1m));
        Assert.Equal("999999999999.9999999999999999",
            AccountingRules.ExactPositionValue(99999999999999999999.99999999m, 0.00000001m));
        Assert.Equal(0.00000001m, AccountingRules.TotalValue([(0.00000001m, 0.5m), (0.00000001m, 0.5m)]));
        Assert.Equal(9007199254740993.12345678m, AccountingRules.TotalValue([(9007199254740993.12345678m, 1m)]));
        Assert.Throws<BusinessException>(() => AccountingRules.TotalValue([(99999999999999999999m, 2m)]));
        Assert.Equal(9007199254740993.12345678m, AccountingRules.UnitPrice(9007199254740993.12345678m, 1m));
        Assert.Equal(0.00000002m, AccountingRules.UnitPrice(0.00000003m, 2m));
        Assert.Throws<BusinessException>(() => AccountingRules.UnitPrice(99999999999999999999m, 0.00000001m));
    }

    /// <summary>Returns the quote date as an ISO calendar date and preserves exact amounts.</summary>
    [Fact]
    public void ValuationPositionJsonIncludesQuoteDate()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new DecimalJsonConverter());
        var position = new ValuationPosition(Guid.NewGuid(), "TEST", "Test", 0.00000001m, 0.5m,
            new DateOnly(2026, 11, 18), "0.000000005");
        var json = JsonSerializer.SerializeToElement(position, options);
        Assert.Equal("2026-11-18", json.GetProperty("priceDate").GetString());
        Assert.Equal("0.5", json.GetProperty("price").GetString());
        Assert.Equal("0.000000005", json.GetProperty("positionValue").GetString());
    }

    /// <summary>Rejects decimal strings that System.Decimal would silently round.</summary>
    [Fact]
    public void DecimalJsonRejectsLossOfPrecision()
    {
        var options = new JsonSerializerOptions(); options.Converters.Add(new DecimalJsonConverter());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<decimal>("\"9007199254740993.123456789012345678\"", options));
    }

    /// <summary>Normalizes liabilities before calculating NAV and enforces storage bounds.</summary>
    [Fact]
    public void NormalizationPreservesNavInvariant()
    {
        var liabilities = AccountingRules.Normalize(0.000000005m);
        Assert.Equal(0m, liabilities);
        Assert.Equal(1m, AccountingRules.Normalize(1m - liabilities));
        Assert.Throws<BusinessException>(() => AccountingRules.Normalize(100000000000000000000m));
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [Fact]
    public void PasswordDelimitersAreLiteral()
    {
        const string password = "semi;quote\"equals= space '";
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseConnection.Resolve("Host=localhost;Database=test", password));
        Assert.Equal(password, parsed.Password);
    }

    /// <summary>Verifies the accounting or transport contract.</summary>
    [Fact]
    public async Task NullBalanceFailsBeforeDatabaseAccess()
    {
        await using var db = new AccountingDb(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AccountingDb>().Options);
        await Assert.ThrowsAsync<BusinessException>(() => new PortfolioService(db).Reconcile(new(Guid.NewGuid(), [null!]), default));
    }
}

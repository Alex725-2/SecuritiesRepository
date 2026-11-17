using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Securities.Application;

/// <summary>Decimals cross the JSON boundary as invariant strings only.</summary>
public sealed class DecimalJsonConverter : JsonConverter<decimal>
{
    /// <summary>Reads an exact invariant decimal string; JSON numbers are rejected.</summary>
    public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !decimal.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw new JsonException("Expected an invariant decimal string.");
        var original = reader.GetString()!;
        if (original.Length > 128 || !System.Text.RegularExpressions.Regex.IsMatch(original, @"^-?\d+(\.\d+)?$"))
            throw new JsonException("Expected an invariant decimal string.");
        static (System.Numerics.BigInteger Digits, int Scale) Parts(string text)
        {
            var dot = text.IndexOf('.');
            return (System.Numerics.BigInteger.Parse(text.Replace(".", ""), CultureInfo.InvariantCulture), dot < 0 ? 0 : text.Length - dot - 1);
        }
        var input = Parts(original);
        var parsed = Parts(value.ToString("0.############################", CultureInfo.InvariantCulture));
        if (input.Digits * System.Numerics.BigInteger.Pow(10, parsed.Scale) != parsed.Digits * System.Numerics.BigInteger.Pow(10, input.Scale))
            throw new JsonException("Decimal cannot be represented exactly.");
        return value;
    }

    /// <summary>Writes a plain string without exponents or insignificant zeroes.</summary>
    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
}

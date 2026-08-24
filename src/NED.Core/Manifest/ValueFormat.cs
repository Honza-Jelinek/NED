using System.Globalization;
using System.Text.Json;
using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>
/// Jediné místo, kde se hodnota uzlu převádí mezi řetězcem, JSON a typovanou podobou.
///
/// Dřív to dělaly tři nezávislé konvertory (persistence, export, editor pole), každý
/// se znal s jinou podmnožinou typů — přidání typu se opravilo na dvou místech ze tří.
/// </summary>
public static class ValueFormat
{
    /// <summary>Hodnota jako řetězec pro UI a export literálů. Invariantní kultura — soubor musí být přenositelný.</summary>
    public static string? ToStringValue(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "true" : "false",
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    /// <summary>
    /// Hodnota pro stručný popisek v UI. Prázdné a čistě whitespace řetězce zapisuje
    /// jako JSON literál, aby explicitní default nebyl zaměnitelný s chybějící hodnotou.
    /// </summary>
    public static string ToDisplayValue(object? value) => value switch
    {
        null => "null",
        string s when string.IsNullOrWhiteSpace(s) => JsonSerializer.Serialize(s),
        JsonElement { ValueKind: JsonValueKind.String } element
            when string.IsNullOrWhiteSpace(element.GetString()) => JsonSerializer.Serialize(element.GetString()),
        _ => ToStringValue(value) ?? "null",
    };

    /// <summary>
    /// Řetězec z UI → typovaná hodnota podle type id. Neparsovatelný vstup vrací
    /// <paramref name="fallback"/>, ať editace pole nikdy nezahodí rozdělanou hodnotu.
    /// </summary>
    public static object? Parse(string? raw, string typeId, object? fallback = null)
    {
        var s = raw ?? "";

        if (typeId == TypeIds.Bool) return bool.TryParse(s, out var b) && b;
        if (typeId == TypeIds.String) return s;
        if (!TypeIds.IsNumeric(typeId)) return s;   // enum i doménový typ se drží jako řetězec

        if (string.IsNullOrWhiteSpace(s)) s = "0";
        try
        {
            return typeId switch
            {
                TypeIds.Byte => byte.Parse(s, CultureInfo.InvariantCulture),
                TypeIds.Short => short.Parse(s, CultureInfo.InvariantCulture),
                TypeIds.Int => int.Parse(s, CultureInfo.InvariantCulture),
                TypeIds.Long => long.Parse(s, CultureInfo.InvariantCulture),
                TypeIds.Float => float.Parse(s, CultureInfo.InvariantCulture),
                TypeIds.Double => double.Parse(s, CultureInfo.InvariantCulture),
                TypeIds.Decimal => decimal.Parse(s, CultureInfo.InvariantCulture),
                _ => s,
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Hodnota ze souboru (<see cref="JsonElement"/> po deserializaci) → typovaná podle manifestu.
    /// Nerozpoznaná kombinace se vrací tak, jak přišla — radši nepřesná hodnota než ztracená.
    /// </summary>
    public static object? FromJson(object? raw, string typeId)
    {
        if (raw is not JsonElement je) return raw;

        return je.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => Parse(je.GetString(), typeId, je.GetString()),
            JsonValueKind.Number => Number(je, typeId),
            _ => je,
        };
    }

    private static object? Number(JsonElement je, string typeId) => typeId switch
    {
        TypeIds.Byte => je.GetByte(),
        TypeIds.Short => je.GetInt16(),
        TypeIds.Int => je.GetInt32(),
        TypeIds.Long => je.GetInt64(),
        TypeIds.Float => je.GetSingle(),
        TypeIds.Decimal => je.GetDecimal(),
        TypeIds.Double => je.GetDouble(),
        TypeIds.String => je.ToString(),
        _ => je.GetDouble(),
    };

    /// <summary>
    /// Hodnota pro export: skaláry jako JSON primitiva, ostatní jako řetězec.
    /// <paramref name="typeId"/> rozhoduje, aby se z „5" nestal řetězec tam, kde má být číslo.
    /// </summary>
    public static object? ForExport(object? value, string typeId)
    {
        if (value is JsonElement je) return FromJson(je, typeId);
        if (value is null) return null;
        if (TypeIds.IsNumeric(typeId) && value is string s) return Parse(s, typeId, s);
        return value;
    }
}

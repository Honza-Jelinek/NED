using System;
using System.Collections.Generic;

namespace NED.Abstractions.Manifest;

/// <summary>
/// Vestavěný vokabulář typů. Skaláry mají krátká id bez packu; všechno ostatní
/// je <c>"pack/typ"</c>. Číselné rozšíření (int → double) se řídí <see cref="NumericRank"/>.
/// </summary>
public static class TypeIds
{
    /// <summary>Přijme cokoliv. Odpovídá dřívějšímu <c>IGraphData</c> / <c>object</c> na vstupu.</summary>
    public const string Any = "any";

    /// <summary>Řídicí hrana — nese pořadí provádění, ne hodnotu.</summary>
    public const string Exec = "exec";

    public const string String = "string";
    public const string Bool = "bool";
    public const string Byte = "byte";
    public const string Short = "short";
    public const string Int = "int";
    public const string Long = "long";
    public const string Float = "float";
    public const string Double = "double";
    public const string Decimal = "decimal";

    /// <summary>Mapa CLR typ → type id. Používá generátor; editor už CLR typy nezná.</summary>
    public static readonly IReadOnlyDictionary<Type, string> FromClrType = new Dictionary<Type, string>
    {
        [typeof(string)] = String,
        [typeof(bool)] = Bool,
        [typeof(byte)] = Byte,
        [typeof(short)] = Short,
        [typeof(int)] = Int,
        [typeof(long)] = Long,
        [typeof(float)] = Float,
        [typeof(double)] = Double,
        [typeof(decimal)] = Decimal,
        [typeof(object)] = Any,
        [typeof(IGraphData)] = Any,
        [typeof(NED.Abstractions.Exec)] = Exec,
    };

    /// <summary>Skalár = editovatelný inline jako text/číslo/checkbox (na rozdíl od doménového typu).</summary>
    public static bool IsScalar(string typeId) =>
        typeId == String || typeId == Bool || IsNumeric(typeId);

    public static bool IsNumeric(string typeId) => NumericRank(typeId) > 0;

    /// <summary>
    /// Pořadí pro implicitní rozšíření: výstup se smí připojit na vstup se stejným
    /// nebo vyšším rankem (int → double ano, double → int ne). 0 = nečíselný.
    /// </summary>
    public static int NumericRank(string typeId) => typeId switch
    {
        Byte => 1,
        Short => 2,
        Int => 3,
        Long => 4,
        Float => 5,
        Double => 6,
        Decimal => 7,
        _ => 0,
    };

    /// <summary>
    /// Smí výstup daného typu téct do vstupu typu <paramref name="inputType"/>?
    ///
    /// <paramref name="outputExtends"/> je <see cref="NodeTypeDescriptor.Extends"/> produkujícího
    /// typu — nese polymorfii. Předává se sem, protože ho zná ten, kdo se ptá (výstupní port si ho
    /// drží od svého vzniku), a není tak potřeba globální graf typů.
    /// </summary>
    public static bool IsCompatible(string outputType, IEnumerable<string>? outputExtends, string inputType)
    {
        // Exec je uzavřený svět. Tohle musí zůstat před pravidlem pro Any.
        if (outputType == Exec || inputType == Exec) return outputType == inputType;
        if (inputType == Any) return true;
        if (outputType == inputType) return true;

        if (outputExtends is not null)
            foreach (var ancestor in outputExtends)
                if (ancestor == inputType)
                    return true;

        // Implicitní číselné rozšíření (int → double ano, double → int ne).
        var outRank = NumericRank(outputType);
        var inRank = NumericRank(inputType);
        return outRank > 0 && inRank > 0 && outRank <= inRank;
    }

    /// <summary>Lidsky čitelný název pro UI (tooltipy, dropdowny).</summary>
    public static string Friendly(string? typeId) => typeId switch
    {
        null or "" => "",
        Any => "Any",
        Exec => "Exec",
        _ => typeId.Substring(typeId.LastIndexOf('/') + 1),
    };
}

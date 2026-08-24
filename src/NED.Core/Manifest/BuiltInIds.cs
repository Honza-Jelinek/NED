namespace NED.Core.Manifest;

/// <summary>
/// Id vestavěných uzlů editoru. Jediné dva typy, které NED zná jménem — jejich chování
/// nejde vyjádřit v manifestu, protože závisí na běhovém stavu (nastavení grafu, volba
/// v type-pickeru). Všechny ostatní typy jsou pro editor anonymní data.
/// </summary>
public static class BuiltInIds
{
    public const string Pack = "ned";
    public const string DefaultOutput = NED.Abstractions.Manifest.NodeOutputNames.Default;

    /// <summary>Sink datového grafu. Porty staví z <c>GraphSettings.Outputs</c>.</summary>
    public const string Output = "ned/OutputNode";

    /// <summary>Parametr subgrafu. Typ jeho výstupu určuje uživatel type-pickerem.</summary>
    public const string GraphInput = "ned/GraphInputNode";
    public const string ExecEntry = "ned/ExecEntry";

    /// <summary>Konec exec větve. Porty staví ze stejných deklarací jako Output uzel.</summary>
    public const string Return = "ned/ReturnNode";

    public const string ExecEntryOutput = "Then";

    /// <summary>
    /// Drátové jméno exec vstupu na uzlu volané funkce (klíč v uloženém linku).
    /// Prefix <c>$</c> schválně: parametry subgrafu se jmenují, jak si autor přeje, a
    /// <b>výchozí</b> jméno parametru je zrovna „In" — bez prefixu by šlo o kolizi.
    /// Na plátně se pin popisuje <see cref="ExecInputLabel"/>.
    /// </summary>
    public const string ExecInput = "$exec";
    public const string ExecInputLabel = "In";
    public const string GraphInputName = "Name";
    public const string GraphInputTypeName = "InputTypeName";
    public const string GraphInputMultiple = "Multiple";
    public const string GraphInputExposure = "Exposure";
    public const string GraphInputDefault = "DefaultValue";
    public const string GraphInputOrder = "Order";
    public const string GraphInputDescription = "Description";
}

using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Abstractions;
using NED.Abstractions.Manifest;
using NED.Core.Manifest;

namespace NED.Core;

/// <summary>
/// Jeden vstup uzlu (z manifestu). Primitivní vstupy lze za běhu přepínat mezi
/// <b>portem</b> (linkovatelný) a <b>polem</b> (inline konstanta); manifest určuje
/// jen výchozí režim.
/// </summary>
public sealed class NodeInput
{
    public required NodeInputDescriptor Descriptor { get; init; }

    /// <summary>Klíč do <see cref="DataNodeModel.Values"/>, do souboru i do linků.</summary>
    public string Name => Descriptor.Name;

    public string Label => Descriptor.Label;

    /// <summary>Type id, nebo přetypované za běhu (Output uzel podle nastavení grafu).</summary>
    public string DataType { get; set; } = TypeIds.Any;

    /// <summary>Arita portu; u dynamických vestavěných uzlů se může za běhu změnit.</summary>
    public bool Multiple { get; set; }
    public bool Optional => Descriptor.Optional;
    public object? DefaultValue => Descriptor.Default;
    public bool HasExplicitDefault => Descriptor.HasExplicitDefault;
    public string? Description => Descriptor.Description;

    /// <summary>Dropdown typů, nikdy port.</summary>
    public bool TypePicker => Descriptor.TypePicker;

    /// <summary>Nerenderuje se inline na uzlu, jen v Details panelu. Nikdy port.</summary>
    /// <remarks>Exec je řídicí hrana, ne hodnota — do Details panelu nepatří, ať si
    /// manifest říká co chce. Nesoulad hlásí katalog (<c>Notice_ExecMustBePort</c>).</remarks>
    public bool Details => Descriptor.Details && DataType != TypeIds.Exec;

    /// <summary>Doménový typ — ve field režimu dropdown typů/subgrafů místo textového pole.</summary>
    public bool Complex => !TypePicker && !TypeIds.IsScalar(DataType);

    /// <summary>Přepínatelné port↔pole — vše kromě array, type-pickeru a Details polí.</summary>
    public bool Togglable =>
        DataType != TypeIds.Exec && DataType != TypeIds.Any
        && !TypePicker && !Multiple && !Details;

    /// <summary>
    /// Výchozí režim z manifestu. Exec je port vždy — konstantu z něj udělat nejde.
    /// <c>any</c> taky: neznámý typ nejde napsat do pole, editor by musel nabídnout
    /// dropdown všech typů světa a uživatel by z něj vybíral nesmysly.
    /// </summary>
    public bool DefaultAsPort =>
        Descriptor.Kind == InputKind.Port || DataType is TypeIds.Exec or TypeIds.Any;

    /// <summary>Aktuální režim expozice.</summary>
    public bool AsPort { get; set; }

    /// <summary>Živý port, když je vstup v port režimu (jinak null).</summary>
    public TypedPortModel? Port { get; set; }
}

/// <summary>
/// Univerzální model uzlu. Data uzlu jsou <b>pytel pojmenovaných hodnot</b>
/// (<see cref="Values"/>) popsaný manifestem — editor nezná žádný doménový CLR typ
/// a nikdy nespouští cizí kód. Viz docs/14-manifest.md.
/// </summary>
public class DataNodeModel : NodeModel
{
    /// <summary>Popis typu uzlu z manifestu.</summary>
    public NodeTypeDescriptor Descriptor { get; }

    /// <summary>Type id tohoto uzlu — zkratka pro <c>Descriptor.Id</c>.</summary>
    public string TypeId => Descriptor.Id;

    /// <summary>Hodnoty vstupů v field režimu. Klíčem je <see cref="NodeInput.Name"/>.</summary>
    public Dictionary<string, object?> Values { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Hodnoty ze souboru, kterým v manifestu nic neodpovídá (starší schéma, chybějící pack).
    /// Drží se beze změny a zapíší se zpět při uložení — save nikdy neztratí data.
    /// </summary>
    public Dictionary<string, object?> UnknownValues { get; } = new(StringComparer.Ordinal);

    /// <summary>Totéž pro override expozice vstupů, které manifest nezná.</summary>
    public Dictionary<string, bool> UnknownPortModes { get; } = new(StringComparer.Ordinal);

    /// <summary>port.Id → vstup, jen pro AKTUÁLNĚ aktivní vstupní porty.</summary>
    public Dictionary<string, NodeInput> Inputs { get; } = new();

    /// <summary>Všechny vstupy uzlu (port i field režim) v pořadí z manifestu.</summary>
    public List<NodeInput> InputDefs { get; } = new();

    /// <summary>Výstupní (pravé) porty podle jména. Prázdné u sinku (Output uzel).</summary>
    public Dictionary<string, TypedPortModel> Outputs { get; } = new(StringComparer.Ordinal);

    /// <summary>Type id, který uzel produkuje.</summary>
    public string OutputType { get; private set; }

    /// <summary>
    /// Vstupy odvozené z deklarací grafu (Output a Return uzel), ne z manifestu.
    /// Klíčem je <see cref="GraphOutput.Id"/> — jméno se smí měnit, port přitom zůstává.
    /// </summary>
    private readonly Dictionary<string, NodeInput> _declaredInputs = new(StringComparer.Ordinal);

    /// <summary>Sink datového grafu — porty staví z deklarací.</summary>
    public bool IsOutputNode => Descriptor.Id == BuiltInIds.Output;

    /// <summary>Vestavěný parametr subgrafu.</summary>
    public bool IsGraphInputNode => Descriptor.Id == BuiltInIds.GraphInput;
    public bool IsExecEntryNode => Descriptor.Id == BuiltInIds.ExecEntry;
    public bool IsReturnNode => Descriptor.Id == BuiltInIds.Return;

    /// <summary>Uzel, jehož hodnotové vstupy diktují <c>GraphSettings.Outputs</c>.</summary>
    public bool DeclaresGraphOutputs => IsOutputNode || IsReturnNode;

    /// <summary><paramref name="id"/> = Id z DTO při načítání ze souboru; null = nový uzel.</summary>
    public DataNodeModel(NodeTypeDescriptor descriptor, Point? position = null, string? id = null,
                         NedCatalog? catalog = null)
        : base(id ?? Guid.NewGuid().ToString(), position ?? new Point(0, 0))
    {
        Descriptor = descriptor;

        foreach (var inputDescriptor in descriptor.Inputs)
        {
            var input = new NodeInput
            {
                Descriptor = inputDescriptor,
                DataType = inputDescriptor.Type,
                Multiple = inputDescriptor.Multiple,
            };

            // Až po dopočítání DataType — DefaultAsPort se na něj u exec ptá.
            input.AsPort = input.DefaultAsPort;

            Values[input.Name] = inputDescriptor.Default;
            InputDefs.Add(input);

            if (input.AsPort) CreatePort(input);
        }

        OutputType = ResolveOutputType();

        foreach (var outputDescriptor in descriptor.Outputs)
        {
            // Duplicitní jméno hlásí katalog; tady jen first-wins, ať se druhý port
            // vůbec nezaloží. Jinak by visel na uzlu bez možnosti ho zapojit.
            if (Outputs.ContainsKey(outputDescriptor.Name)) continue;

            var outputType = IsGraphInputNode ? OutputType : outputDescriptor.Type ?? descriptor.Id;
            var output = new TypedPortModel(this, PortAlignment.Right, outputType)
            {
                Multiple = IsGraphInputNode ? ResolveOutputMultiple() : outputDescriptor.Multiple,
                Subflow = outputDescriptor.Role == ExecOutputRole.Subflow,
                Label = outputDescriptor.Name,
                Extends = catalog?.ExtendsOf(outputType)
                    ?? (outputType == descriptor.Id ? descriptor.Extends : Array.Empty<string>()),
                Description = outputDescriptor.Description ?? descriptor.Description,
            };
            AddPort(output);
            Outputs[outputDescriptor.Name] = output;
        }
    }

    /// <summary>Hodnota vstupu jako řetězec (pro widgety a export literálů).</summary>
    public string? ValueAsString(string name) =>
        Values.TryGetValue(name, out var v) ? ValueFormat.ToStringValue(v) : null;

    /// <summary>
    /// Výstupní typ. U parametru subgrafu ho volí uživatel type-pickerem, takže se čte
    /// z hodnoty, ne z manifestu — jeden ze dvou vestavěných uzlů s běhovým chováním.
    /// </summary>
    private string ResolveOutputType()
    {
        if (IsGraphInputNode)
        {
            var picked = ValueAsString(BuiltInIds.GraphInputTypeName);
            return string.IsNullOrWhiteSpace(picked) ? TypeIds.Any : picked!;
        }
        return Descriptor.Outputs.FirstOrDefault()?.Type ?? Descriptor.Id;
    }

    private bool ResolveOutputMultiple() =>
        IsGraphInputNode
        && bool.TryParse(ValueAsString(BuiltInIds.GraphInputMultiple), out var multiple)
        && multiple;

    /// <summary>
    /// Znovu aplikuje metadata výstupního portu, která se mění za běhu (uživatelský popis
    /// parametru subgrafu). Volá se po editaci pole.
    /// </summary>
    public void SyncOutputMetadata()
    {
        if (!IsGraphInputNode) return;

        var description = ValueAsString(BuiltInIds.GraphInputDescription);
        foreach (var output in Outputs.Values)
            output.Description = string.IsNullOrWhiteSpace(description) ? Descriptor.Description : description;
    }

    /// <summary>Přepne přepínatelný vstup mezi portem a polem (a obráceně).</summary>
    public void SetExposure(NodeInput input, bool asPort)
    {
        if (!input.Togglable || input.AsPort == asPort) return;
        input.AsPort = asPort;

        if (asPort)
        {
            CreatePort(input);
        }
        else if (input.Port is not null)
        {
            RemoveInputPort(input);
        }
        Refresh();
    }

    private void CreatePort(NodeInput input)
    {
        var port = new TypedPortModel(this, PortAlignment.Left, input.DataType)
        {
            Multiple = input.Multiple,
            Label = input.Label,
            HasExplicitDefault = input.HasExplicitDefault,
            DefaultValue = input.DefaultValue,
            Description = input.Description,
        };
        input.Port = port;
        AddPort(port);
        Inputs[port.Id] = input;
    }

    /// <summary>Nechá na portu první link a zbytek zruší (ZBD linky sám nečistí).</summary>
    private static void TrimToSingleLink(TypedPortModel port)
    {
        foreach (var link in port.Links.Skip(1).ToList())
            link.Diagram?.Links.Remove(link);
    }

    private void RemoveInputPort(NodeInput input)
    {
        if (input.Port is null) return;
        // Odebrání portu nečistí linky — musíme je zrušit ručně přes jejich diagram.
        foreach (var link in input.Port.Links.ToList())
            link.Diagram?.Links.Remove(link);
        Inputs.Remove(input.Port.Id);
        RemovePort(input.Port);
        input.Port = null;
    }

    /// <summary>
    /// Přečte znovu běhově určený výstupní typ a aktualizuje port (volá widget po změně
    /// type-pickeru). Linky zůstávají; případnou nekompatibilitu nahlásí validace.
    /// </summary>
    public void RefreshDynamicTypes()
    {
        OutputType = ResolveOutputType();
        foreach (var output in Outputs.Values)
        {
            output.DataType = OutputType;
            output.Multiple = ResolveOutputMultiple();
            output.Refresh();
        }
        Refresh();
    }

    /// <summary>
    /// Srovná hodnotové vstupy s deklaracemi grafu. Platí pro Output uzel (datový tok)
    /// i pro každý Return uzel (exec tok) — obojí je „jeden vstupní port na deklaraci",
    /// jen na jiném konci toku.
    ///
    /// Páruje se podle <see cref="GraphOutput.Id"/>, ne podle jména: existující port se pak
    /// aktualizuje <b>na místě</b> a jeho linky přežijí i přejmenování. Zaniklá deklarace
    /// si svůj port i dráty odnese.
    /// </summary>
    public void SyncDeclaredInputs(IReadOnlyList<GraphOutput> declared)
    {
        if (!DeclaresGraphOutputs) return;

        var stale = new Dictionary<string, NodeInput>(_declaredInputs, StringComparer.Ordinal);
        foreach (var output in declared
                     .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                     .GroupBy(item => item.Id, StringComparer.Ordinal)
                     .Select(group => group.First()))
        {
            if (stale.Remove(output.Id, out var existing))
            {
                RenameDeclaredInput(existing, output.Name);
                existing.DataType = output.Type;
                existing.Multiple = output.Multiple;
                existing.Descriptor.Type = output.Type;
                existing.Descriptor.Multiple = output.Multiple;
                if (existing.Port is { } port)
                {
                    port.DataType = output.Type;
                    port.Multiple = output.Multiple;
                    port.Label = output.Name;
                    // Pole → skalár: přebytečné dráty musí pryč hned. Typová kontrola je
                    // nechytí (arita vstupu se neporovnává) a export by vzal jen první.
                    if (!output.Multiple) TrimToSingleLink(port);
                    port.Refresh();
                }
                continue;
            }

            // $exec je rezervované manifestové jméno řídicího vstupu Return uzlu.
            if (InputDefs.Any(input => input.Name == output.Name)) continue;

            var input = new NodeInput
            {
                Descriptor = new NodeInputDescriptor
                {
                    Name = output.Name,
                    Label = output.Name,
                    Kind = InputKind.Port,
                    Type = output.Type,
                    Multiple = output.Multiple,
                    Optional = true,
                },
                DataType = output.Type,
                Multiple = output.Multiple,
                AsPort = true,
            };
            Values[input.Name] = null;
            InputDefs.Add(input);
            _declaredInputs[output.Id] = input;
            CreatePort(input);
        }

        foreach (var (id, input) in stale)
        {
            RemoveInputPort(input);
            InputDefs.Remove(input);
            _declaredInputs.Remove(id);
            Values.Remove(input.Name);
        }
        Refresh();
    }

    /// <summary>
    /// Přepíše jméno odvozeného vstupu. Deskriptor je syntetický a patří uzlu, takže se smí
    /// měnit; <see cref="Values"/> je klíčovaný jménem, proto se hodnota musí přestěhovat.
    /// <see cref="Inputs"/> jede přes port.Id, ten se nemění.
    /// </summary>
    private void RenameDeclaredInput(NodeInput input, string name)
    {
        if (input.Name == name) return;
        if (Values.Remove(input.Name, out var value)) Values[name] = value;
        input.Descriptor.Name = name;
        input.Descriptor.Label = name;
    }
}

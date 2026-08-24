using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using NED.Core.Assets;
using NED.Core.Manifest;
using NED.Abstractions.Manifest;

namespace NED.Core;

/// <summary>
/// Reference na subgraf vložená do rodičovského grafu. Porty se staví z cachovaného
/// <see cref="SubgraphInterface"/> (ne reflexí nad C# typem). Renderuje se jako
/// sbalený node; double-click otevře subgraf k editaci (C5).
/// </summary>
public sealed class SubgraphNodeModel : NodeModel
{
    /// <summary>GUID referencovaného subgrafu (stabilní identita).</summary>
    public Guid SubgraphId { get; }

    /// <summary>Název subgrafu (zobrazuje se v headeru). Aktualizuje se při refreshi reference
    /// (rename souboru, zahojení stale reference po znovuvytvoření assetu).</summary>
    public string SubgraphName { get; set; }

    /// <summary>Cachované rozhraní subgrafu (vstupy + output typ).</summary>
    public SubgraphInterface Interface { get; private set; }

    /// <summary>Hodnoty Field-exposed vstupů (klíč = input name).</summary>
    public Dictionary<string, string> FieldValues { get; } = new();

    /// <summary>
    /// Per-instance override expozice vstupu (name → je port?). Subgraf definuje
    /// výchozí expozici v rozhraní; rodič ji může na konkrétní instanci přepnout.
    /// </summary>
    public Dictionary<string, bool> ExposureOverride { get; } = new();

    /// <summary>Vstupní porty — input name → port (pro save/load linků).</summary>
    public Dictionary<string, TypedPortModel> InputPorts { get; } = new();

    /// <summary>Exec vstup — jen u volané funkce (<see cref="SubgraphInterface.Flow"/>).</summary>
    public TypedPortModel? ExecInput { get; private set; }

    /// <summary>Výstupní port (návratový typ subgrafu).</summary>
    public Dictionary<string, TypedPortModel> Outputs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Port → id deklarace, ze které vznikl. Jen běhová vazba (do souboru se ukládá jméno
    /// portu), díky které přejmenování výstupu v subgrafu neodpojí volajícího.
    /// </summary>
    private readonly Dictionary<TypedPortModel, string> _portDeclarationIds = new();

    /// <summary>
    /// <paramref name="id"/> = Id z DTO při načítání ze souboru; null = nový node.
    /// Viz <see cref="DataNodeModel"/> — Id musí přežít round-trip, jinak se soubor mění bez zásahu uživatele.
    /// </summary>
    public SubgraphNodeModel(AssetEntry asset, Point? position = null, string? id = null)
        : base(id ?? Guid.NewGuid().ToString(), position ?? new Point(0, 0))
    {
        SubgraphId = asset.Id;
        SubgraphName = asset.Name;
        Interface = asset.Interface;
        BuildPorts();
    }

    /// <summary>
    /// Srovná porty s novým rozhraním. Existující porty se aktualizují NA MÍSTĚ
    /// (objekt portu zůstává → linky na něj přežijí; případnou typovou nekompatibilitu
    /// nahlásí validace jako broken link). Zaniklé vstupy se odeberou i s linky,
    /// nové se přidají. Output port se jen přetypuje.
    /// </summary>
    public void RebuildFromInterface(SubgraphInterface iface)
    {
        Interface = iface;

        // Co zbyde v `stale`, v novém rozhraní není (nebo přešlo na pole) → zrušit.
        var stale = new Dictionary<string, TypedPortModel>(InputPorts);
        InputPorts.Clear();

        foreach (var inp in Interface.Inputs)
        {
            if (IsPort(inp))
            {
                if (stale.TryGetValue(inp.Name, out var existing))
                {
                    stale.Remove(inp.Name);
                    existing.DataType = inp.Type;
                    existing.Multiple = inp.Multiple;
                    existing.Description = inp.Description;
                    InputPorts[inp.Name] = existing;
                }
                else
                {
                    var port = new TypedPortModel(this, PortAlignment.Left, inp.Type) { Label = inp.Name, Description = inp.Description, Multiple = inp.Multiple };
                    AddPort(port);
                    InputPorts[inp.Name] = port;
                }
            }
            else if (!FieldValues.ContainsKey(inp.Name))
            {
                FieldValues[inp.Name] = inp.Default ?? "";
            }
        }

        foreach (var port in stale.Values)
            RemovePortWithLinks(port);

        EnsureExecInput();
        SyncOutputPorts();
        Refresh();
    }

    /// <summary>Aktuální expozice vstupu — per-instance override, jinak výchozí z rozhraní.</summary>
    public bool IsPort(SubgraphInput inp) =>
        ExposureOverride.TryGetValue(inp.Name, out var ov) ? ov : inp.Exposure == InputExposure.Port;

    /// <summary>Přepne vstup mezi portem a polem (per-instance). Při port→pole zruší jeho linky.</summary>
    public void SetExposure(string inputName, bool asPort)
    {
        var inp = Interface.Inputs.FirstOrDefault(i => i.Name == inputName);
        if (inp is null || IsPort(inp) == asPort) return;

        ExposureOverride[inputName] = asPort;

        if (asPort)
        {
            var port = new TypedPortModel(this, PortAlignment.Left, inp.Type) { Label = inp.Name, Description = inp.Description, Multiple = inp.Multiple };
            AddPort(port);
            InputPorts[inputName] = port;
        }
        else if (InputPorts.TryGetValue(inputName, out var port))
        {
            RemovePortWithLinks(port);
            InputPorts.Remove(inputName);
            if (!FieldValues.ContainsKey(inputName))
                FieldValues[inputName] = inp.Default ?? "";
        }
        Refresh();
    }

    /// <summary>Odebrání portu nečistí linky (ZBD) — zrušíme je ručně přes jejich diagram.</summary>
    /// <summary>
    /// Exec vstup volané funkce. <c>Multiple</c> schválně — větve, které se sbíhají na jedno
    /// volání, končí tady. Datový subgraf ho nemá; ten se do volajícího vlévá.
    /// </summary>
    private void EnsureExecInput()
    {
        var wanted = Interface.Flow == GraphFlow.Exec;
        var has = ExecInput is not null;
        if (wanted == has) return;

        if (wanted)
        {
            ExecInput = new TypedPortModel(this, PortAlignment.Left, TypeIds.Exec)
                { Label = BuiltInIds.ExecInputLabel, Multiple = true };
            AddPort(ExecInput);
        }
        else
        {
            RemovePortWithLinks(ExecInput!);
            ExecInput = null;
        }
    }

    private void RemovePortWithLinks(TypedPortModel port)
    {
        foreach (var link in port.Links.ToList())
            link.Diagram?.Links.Remove(link);
        RemovePort(port);
    }

    private void BuildPorts()
    {
        EnsureExecInput();
        foreach (var inp in Interface.Inputs)
        {
            if (IsPort(inp))
            {
                var port = new TypedPortModel(this, PortAlignment.Left, inp.Type) { Label = inp.Name, Description = inp.Description, Multiple = inp.Multiple };
                AddPort(port);
                InputPorts[inp.Name] = port;
            }
            else
            {
                // Field-exposed: no port, set default if not already set
                if (!FieldValues.ContainsKey(inp.Name))
                    FieldValues[inp.Name] = inp.Default ?? "";
            }
        }

        SyncOutputPorts();
    }

    /// <summary>
    /// Srovná výstupní porty s rozhraním. Existující port se přetypuje (linky přežijí),
    /// zmizelý se zruší. Subgraf bez Output uzlu žádný výstupní port nemá — funkce,
    /// která nic nevrací, se dá zapojit jen exec hranou.
    /// </summary>
    private void SyncOutputPorts()
    {
        var stale = new Dictionary<string, TypedPortModel>(Outputs, StringComparer.Ordinal);
        Outputs.Clear();

        // Exec funkce má jedno pokračování. Víc východů by znamenalo pojmenované Return
        // uzly v těle — přidat jde kdykoliv, ubrat ne.
        if (Interface.Flow == GraphFlow.Exec)
        {
            if (stale.TryGetValue(BuiltInIds.ExecEntryOutput, out var existingExec))
            {
                stale.Remove(BuiltInIds.ExecEntryOutput);
                Outputs[BuiltInIds.ExecEntryOutput] = existingExec;
            }
            else
            {
                var execOut = new TypedPortModel(this, PortAlignment.Right, TypeIds.Exec)
                    { Label = BuiltInIds.ExecEntryOutput };
                AddPort(execOut);
                Outputs[BuiltInIds.ExecEntryOutput] = execOut;
            }
        }

        foreach (var (portName, output) in Interface.PortOutputs())
        {
            // Návratová hodnota pojmenovaná „Then" by jinak přepsala exec pin.
            if (Outputs.ContainsKey(portName)) continue;

            // Nejdřív podle stabilního id deklarace — port pak přežije přejmenování výstupu
            // v subgrafu. Jméno je záloha pro porty založené dřív, než id existovala.
            var existing = TakeByDeclarationId(stale, output.Id) ?? Take(stale, portName);
            if (existing is not null)
            {
                existing.DataType = output.Type;
                existing.Multiple = output.Multiple;
                existing.Label = portName;
                _portDeclarationIds[existing] = output.Id;
                Outputs[portName] = existing;
                continue;
            }

            var port = new TypedPortModel(this, PortAlignment.Right, output.Type)
                { Label = portName, Multiple = output.Multiple };
            AddPort(port);
            _portDeclarationIds[port] = output.Id;
            Outputs[portName] = port;
        }

        foreach (var port in stale.Values)
        {
            _portDeclarationIds.Remove(port);
            RemovePortWithLinks(port);
        }
    }

    /// <summary>Vytáhne ze sady port patřící dané deklaraci (prázdné id nepáruje).</summary>
    private TypedPortModel? TakeByDeclarationId(Dictionary<string, TypedPortModel> stale, string declarationId)
    {
        if (string.IsNullOrEmpty(declarationId)) return null;
        foreach (var (key, port) in stale)
            if (_portDeclarationIds.TryGetValue(port, out var id) && id == declarationId)
            {
                stale.Remove(key);
                return port;
            }
        return null;
    }

    private static TypedPortModel? Take(Dictionary<string, TypedPortModel> stale, string portName) =>
        stale.Remove(portName, out var port) ? port : null;
}

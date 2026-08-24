# Vkládání grafů, rozhraní a instance

Každý `.nedgraph.json` je asset se stabilním GUID a veřejným rozhraním. NED už nerozděluje soubory
na role `Graph`, `Subgraph` a `Template`: každý tokově kompatibilní graf lze vložit do jiného a
samostatný příznak `Instanceable` určuje, zda z něj lze vytvářet parametrizované instance.

## Jedna osa: datový nebo exec tok

`GraphSettings.Flow` má dvě hodnoty:

- `Data` — graf je výraz. Do jiného grafu se vloží jako datový `SubgraphNode` a při exportu se
  inlinuje.
- `Exec` — graf je procedura/funkce. Lze ho vložit pouze do exec grafu a exportuje se jako `$call`
  s definicí v `functions`.

Datový graf lze použít i uvnitř exec grafu jako producent argumentu. Exec graf v datovém grafu
smysl nemá, protože datový export neobsahuje řídicí hrany; picker ho nenabídne a validátor ho odmítne.
Právě editovaný graf se z pickeru vyloučí, aby nešlo vytvořit přímou sebe-referenci.

## Veřejné rozhraní

Rozhraní má parametry, návraty a tok:

```text
SubgraphInterface
├── Flow
├── Inputs[]   ← GraphInputNode na plátně
└── Outputs[]  ← GraphSettings.Outputs
```

### Parametry

Jeden `GraphInputNode` deklaruje:

- `Name` — veřejné jméno parametru;
- `InputTypeName` — manifestové type id;
- `Exposure` — `Port` nebo `Field` na vloženém uzlu;
- `DefaultValue`, `Order` a `Description`.

Jeho výstupní port se za běhu přetypuje podle `InputTypeName`. Parametry se v rozhraní a exportu
řadí podle `Order`; duplicitní jména jsou validační chyba.

### Návraty

Návraty jsou seřazené deklarace v `GraphSettings.Outputs`, nikoli samostatné Output uzly. Každá má:

- stabilní `Id`, díky kterému port přežije přejmenování;
- `Name` a type id `Type`;
- `Multiple`, tedy zda jeden drát nese pole prvků.

Datový graf má právě jeden `OutputNode`, který z deklarací vytvoří všechny vstupní porty. Exec graf
používá `ReturnNode`; různé větve mohou vrátit různé hodnoty stejného deklarovaného rozhraní.

Na instanci vloženého grafu se jediný návrat kvůli kompatibilitě starších souborů stále jmenuje
`Out`. Teprve více návratů používá jejich veřejná jména jako jména výstupních portů.

## SubgraphNodeModel

Vložený graf není manifestový `DataNodeModel`, protože jeho porty vznikají z jiného souboru:

```csharp
public sealed class SubgraphNodeModel : NodeModel
{
    public Guid SubgraphId { get; }
    public SubgraphInterface Interface { get; }
    public Dictionary<string, TypedPortModel> InputPorts { get; }
    public Dictionary<string, string> FieldValues { get; }
    public Dictionary<string, TypedPortModel> Outputs { get; }
    public TypedPortModel? ExecInput { get; }
}
```

Datový graf dostane parametry a datové výstupy. Exec graf navíc dostane řídicí vstup a výstup
`Then`. `RebuildFromInterface()` aktualizuje existující porty po změně assetu. Návratové porty páruje
nejdřív podle stabilního id deklarace a až potom podle jména, takže přejmenování neodpojí link.
Změna arity nebo typu upraví port na místě; přebytečné linky se odstraní jen tehdy, když nová arita
už jejich počet nebo směr nepovoluje.

## Persistence reference

Rodič ukládá pouze GUID a per-instance hodnoty/režimy:

```jsonc
{
  "SubgraphNodes": [
    {
      "Id": "call-1",
      "X": 320,
      "Y": 140,
      "SubgraphId": "7f3a…-guid",
      "FieldValues": { "Scale": "2" },
      "PortModes": { "Scale": false }
    }
  ]
}
```

Cestu v referenci nenajdeš. `AssetIndex.Resolve(Guid)` ji dohledá v knihovních roots, takže
přejmenování nebo přesun souboru link nerozbije. Když asset chybí, editor zachová stale referenci a
umí vytvořit opravný stub místo tichého zahození dat.

## Datový export: inlining

Při překročení hranice `SubgraphNode` exporter:

1. resolvuje GUID a načte tělo;
2. sestaví binding každého `GraphInputNode` na producer nebo field hodnotu volajícího;
3. podle konzumovaného výstupního portu vybere návratovou deklaraci;
4. najde jediný `OutputNode` a rekurzivně sestaví hodnotu jeho odpovídajícího vstupu;
5. zachová identitu uzlů přes `$id`/`$ref` a hranici subgrafu z výsledku odstraní.

Polový návrat zachová všechny prvky. Pokud je celý polový subgraf zapojen do polového vstupu,
export použije `$spread` a interní `$list`, aby nevzniklo vnořené pole. Zásobník GUID zastaví
rekurzivní inlining a místo nekonečné expanze vydá `$error`.

## Exec export: funkce

Exec subgraf se neinlinuje. Volající uzel se zapíše jako:

```jsonc
{
  "$id": "n4",
  "$call": "f1",
  "Ticket": { "$param": "Ticket" }
}
```

Tělo se jednou uloží do `functions` se svým `id`, `name`, `inputs`, deklaracemi `outputs`, `entry`,
`nodes` a `exec`. Konkrétní návratové hodnoty nesou jeho Return uzly. Další volání používají stejné
id funkce; rekurzivní reference nezpůsobí nekonečné sestavování exportu.

## Instance

`GraphSettings.Instanceable` je opt-in pro parametrizované kopie. `AssetIndex.Templates()` vrací
právě assety s tímto příznakem; historická role `Template` už neexistuje. Výchozí hodnotu pro nově
založený graf drží `Workspace.NewGraphInstanceable`, per-graf přepínač je v Details panelu.

Instance ukládá GUID šablony a konkrétní hodnoty parametrů. Při exportu se načte aktuální tělo,
parametry se navážou stejně jako při inliningu a výsledné hodnoty se sestaví z deklarovaných
návratů. Změna těla se tak projeví i ve starší instanci bez kopírování celého grafu.

## Obnova rozhraní

`AssetIndex.Changed` se vyvolá po rescanu knihoven. `NedCanvas` projde otevřené taby a
`SubgraphNodeModel.RebuildFromInterface()` synchronizuje porty. Přidané a odebrané deklarace se
projeví okamžitě; přejmenování, změna typu a arity zachovají port i link, pokud zůstane spojení
kompatibilní.

Podrobnosti o indexu jsou v [13-libraries-and-assets.md](13-libraries-and-assets.md), o obou
exportních topologiích v [05-export.md](05-export.md).

Zpět na [mapu dokumentace](README.md).

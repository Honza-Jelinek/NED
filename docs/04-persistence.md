# Save/Load — `.nedgraph.json`

Graf je zdrojový dokument editoru: obsahuje nastavení, stabilní identity a pozice uzlů, hodnoty,
režimy portů a linky. Runtime ho nemá vykonávat přímo; odvozený veřejný artefakt popisuje
[05-export.md](05-export.md).

## Formát dokumentu

```jsonc
{
  "SchemaVersion": 4,
  "Settings": {
    "Id": "7f3a…-guid",
    "Name": "Add Two Numbers",
    "Description": "Ukázkový datový graf",
    "Outputs": [
      { "Id": "a4ce912f", "Name": "Result", "Type": "double", "Multiple": false },
      { "Id": "08b1d944", "Name": "Warnings", "Type": "string", "Multiple": true }
    ],
    "Instanceable": true,
    "ExportTranslator": "ned.json",
    "RequiredPacks": [
      { "Id": "sandbox", "Version": "1.0.0" }
    ]
  },
  "Nodes": [
    {
      "Id": "abc-123",
      "X": 120,
      "Y": 80,
      "TypeName": "sandbox/NumberConstant",
      "Fields": { "Value": 10 }
    },
    {
      "Id": "def-456",
      "X": 460,
      "Y": 120,
      "TypeName": "sandbox/Add",
      "Fields": { "A": 0, "B": 0 },
      "PortModes": { "A": false }
    }
  ],
  "SubgraphNodes": [],
  "Links": [
    { "FromNode": "abc-123", "FromPort": "Out", "ToNode": "def-456", "ToPort": "B" }
  ]
}
```

`Flow` se zapisuje jen pro exec graf jako `"Exec"`; chybějící hodnota znamená datový tok.
`Instanceable` určuje, zda lze z grafu vytvářet parametrizované instance. Role `Graph`, `Subgraph`
a `Template` už formát nemá: každý tokově kompatibilní graf lze vložit a instancovatelnost je
samostatný příznak.

`Settings.Outputs` je seřazený seznam návratových deklarací. Každá má:

- stabilní `Id`, podle kterého živý port přežije přejmenování;
- veřejné `Name` a manifestové type id `Type`;
- `Multiple`, tedy zda hodnota představuje ploché pole prvků daného typu.

Datový tok připojuje všechny deklarace do jediného `OutputNode`. Exec tok používá stejné deklarace
na každém `ReturnNode`. Dynamické porty vzniknou před obnovou linků, takže round-trip neztratí jejich
propojení.

`TypeName` je historický název pole; jeho hodnota je stabilní type id `"pack/node"`, nikoli CLR
`FullName`. `RequiredPacks` se při save odvodí z použitých type id a katalog doplní verzi. Vestavěný
pack `ned` se nezapisuje. Dokumenty schématu 2 s `RequiredPacks: ["sandbox"]` zůstávají čitelné.

## API

```csharp
public static GraphDocument ToDocument(
    BlazorDiagram diagram, GraphSettings settings, NedCatalog? catalog = null);
public static string Serialize(GraphDocument document);
public static GraphDocument? Deserialize(string json);
public static GraphSettings LoadInto(
    BlazorDiagram diagram, GraphDocument document,
    NedCatalog catalog, AssetIndex? assetIndex = null);
```

Produkční save předává katalog, aby se do requirements zapsaly verze. Interní snapshoty undo mohou
katalog vynechat; id packů se pořád odvodí z dokumentu.

## Bezztrátový round-trip

- Známý typ vytvoří `DataNodeModel` z descriptoru manifestu.
- Neznámý typ vytvoří `MissingNodeModel`, který drží původní `GraphNodeDto`.
- Pole, která aktuální descriptor nezná, zůstanou v `UnknownValues` a při save se vrátí.
- Neznámé režimy portů stejně přežijí v `UnknownPortModes`.
- Linky se obnovují podle stabilního id uzlu a jména vstupu, nikoli identity objektu portu.
- Chybějící nebo duplicitní id návratových deklarací se při načtení nahradí unikátními id.

Proto otevření grafu bez packu a následné uložení nesmí zahodit jeho data. Po návratu nebo hot
reloadu packu se placeholder z téhož DTO znovu sestaví jako normální uzel.

## Verze schématu

`GraphDocument.CurrentSchemaVersion` je 4. Schéma 4 nahradilo `Kind` a jediný `OutputType`
vlastnostmi `Flow`, `Outputs[]` a `Instanceable`. Vyšší neznámá verze se načte degradovaně a editor
uživatele varuje. Schéma 3 zavedlo objektové `RequiredPacks` s `Id` a `Version`; tolerantní converter
čte i starší řetězcový tvar.

## Save vs. export

`.nedgraph.json` obsahuje editorový stav a je optimalizovaný pro úpravy a bezztrátovou migraci.
Export neobsahuje pozice ani link metadata; skládá datové hodnoty nebo plochý exec graf a má vlastní
nezávislou `exportVersion` a [JSON Schema](ned-export-v1.schema.json).

Zpět na [mapu dokumentace](README.md).

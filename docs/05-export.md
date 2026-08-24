# Export — z grafu na runtime JSON

Export je odvozený artefakt, ne zdrojový soubor editoru. Analogie je `.cs` → build → `.dll`:
`.nedgraph.json` zachovává editorový graf, zatímco export verze 1 je veřejná runtime smlouva.

Veřejný formát popisuje [JSON Schema](ned-export-v1.schema.json). Konzument nejdřív ověří
`exportVersion` a dostupnost `packs`; `version` je verze packu, proti které export vznikl.

## Společná obálka

```jsonc
{
  "exportVersion": 1,
  "packs": [{ "id": "sandbox", "version": "1.0.0" }],
  "settings": {},
  "inputs": [
    { "name": "Ticket", "type": "string", "default": "", "description": null }
  ]
}
```

`inputs` vznikají z `GraphInputNode` a vynechají se, když graf žádné nemá. Datový graf má prázdné
`settings`; exec graf zapisuje `{ "graphKind": "exec" }`. Návratové typy už nejsou v `settings`,
ale přímo v deklaracích `outputs`.

## Datový graf

Datový tok má právě jeden `OutputNode`, jehož dynamické vstupy odpovídají
`GraphSettings.Outputs`. Export má pro každou deklaraci jméno, type id, aritu a hodnotu:

```jsonc
{
  "exportVersion": 1,
  "packs": [{ "id": "sandbox", "version": "1.0.0" }],
  "settings": {},
  "outputs": [
    {
      "name": "Result",
      "type": "double",
      "multiple": false,
      "value": {
        "$id": "n1", "$type": "sandbox/Add",
        "A": { "$id": "n2", "$type": "sandbox/NumberConstant", "Value": 2 },
        "B": { "$id": "n3", "$type": "sandbox/NumberConstant", "Value": 3 }
      }
    },
    {
      "name": "Warnings",
      "type": "string",
      "multiple": true,
      "value": []
    }
  ]
}
```

Nezapojený skalární návrat má `value: null`; nezapojený polový návrat `value: []`. Klíč `value`
je v datovém exportu přítomný vždy, aby se „nezapojeno“ nepletlo s exec deklarací bez hodnoty.

### Identita a odkazy

Výsledkem není prostá kopie stromu: sdílený producer musí zůstat jedinou runtime instancí.

- Každá definice uzlu má `$id` a plné manifestové `$type`.
- První setkání uzel definuje; další zapíše `{ "$ref": "n2" }`.
- Odkaz na jiný než výchozí výstup přidá `$output`.
- Druhé setkání během cyklu přidá `$cycle: true`.
- `$literal` je hodnota, ne uzel, a `$id` nedostává.

Pojmenovaný výstup může být označen `$output` jak na referenci, tak na první definici producenta.

### Pole a `$spread`

Polový vstup nebo návrat obsahuje pole všech připojených producentů. Skalární producent vloží jeden
prvek. Producent s `Multiple = true` se označí markerem `$spread`, aby runtime jeho pole zploštil o
jednu úroveň:

```jsonc
"Items": [
  { "$id": "n2", "$type": "sandbox/NumberConstant", "Value": 1 },
  { "$spread": { "$id": "n3", "$type": "sandbox/NumberList" } }
]
```

Při inliningu polového výstupu subgrafu může vnitřní hodnota použít obálku `$list`; význam zůstává
stejný: `$spread` rozbalí její prvky do rodičovského pole.

## Exec graf

Exec tok má plochou tabulku `nodes`, vstupní id `entry` a samostatné řídicí hrany `exec`:

```jsonc
{
  "exportVersion": 1,
  "packs": [{ "id": "sandbox", "version": "1.0.0" }],
  "settings": { "graphKind": "exec" },
  "outputs": [
    { "name": "Result", "type": "double", "multiple": false }
  ],
  "entry": "n1",
  "nodes": [
    { "$id": "n1", "$type": "ned/ExecEntry" },
    { "$id": "n2", "$type": "ned/ReturnNode",
      "Result": { "$id": "n3", "$type": "sandbox/NumberConstant", "Value": 7 } }
  ],
  "exec": [{ "from": "n1", "pin": "Then", "to": "n2" }]
}
```

Exec `outputs` jsou pouze deklarace; nemají `value`. Hodnoty nese každý dosažený `ReturnNode`, takže
různé větve mohou vrátit různé výsledky. Graf bez návratů pole `outputs` vynechá. Datový producer,
který je zároveň exec krokem, se do konzumenta znovu nevnoří: export použije `$ref` a případně
`$output`, aby runtime krok nespustil podruhé.

Smyčka je legální exec hrana. Exporter každý uzel navštíví jednou a hranu zapíše samostatně. Cílový
exec pin se zatím neexportuje; runtime kontrakt předpokládá jeden řídicí vstup na uzel.

## Volání exec funkcí

Exec graf lze vložit do jiného exec grafu. Volající uzel má `$call` s id funkce a její datové
argumenty; tělo se jednou uloží do `functions`:

```jsonc
{
  "$id": "n4",
  "$call": "f1",
  "Ticket": { "$param": "Ticket" }
}
```

Položka funkce obsahuje `id`, `name`, volitelné `inputs` a `outputs`, dále vlastní `entry`, `nodes`
a `exec`. Výstupy funkce jsou deklarace bez `value`; konkrétní výsledky opět nesou její Return uzly.
Opakované volání sdílí jednu definici funkce a rekurzivní odkaz se při exportu nezacyklí.

Datový subgraf se naopak inlinuje: `GraphInputNode` se nahradí argumentem volajícího, zvolená
návratová deklarace se vyhodnotí od jediného Output sinku a hranice subgrafu z exportu zmizí.
Rekurzivní inlining hlídá zásobník GUID a místo nekonečné rekurze vydá `$error`.

## Validace před exportem

Editor export blokuje při validační chybě. Mezi kořenová pravidla patří:

- datový graf deklaruje alespoň jeden návrat a má právě jeden `OutputNode`;
- exec graf má právě jeden `ExecEntry`, nesmí obsahovat `OutputNode` a při deklarovaných návratech
  potřebuje alespoň jeden `ReturnNode`;
- exec uzel ani exec funkce nesmí být v datovém grafu;
- kontrolují se povinné porty, kompatibilita typů a arit, duplicitní parametry, chybějící packy a
  nedosažitelné uzly.

Exporter navíc vrací strukturované chyby pro chybějící nebo nečitelné subgrafy. Jiný formát lze
doplnit přes `IExportTranslator`; dostane stejný neutrální `ExportModel`.

Zpět na [mapu dokumentace](README.md).

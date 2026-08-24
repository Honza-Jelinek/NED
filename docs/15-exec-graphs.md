# Exec grafy — řídicí workflow

Exec graf má `GraphSettings.Flow = GraphFlow.Exec`. Nepopisuje jediný datový výraz, ale pořadí
kroků. Datové linky dál dodávají argumenty; řídicí linky typu `exec` určují, který krok následuje.

## Vstupní bod

Nový exec graf se založí s uzlem `ned/ExecEntry`. Validátor vyžaduje právě jeden a export začíná
průchod od něj. Explicitní vstupní bod je důležitý: heuristika „uzel s nepřipojeným exec vstupem“
by při větvení, slučování a smyčkách nebyla jednoznačná a po rozpojení linku by tiše změnila význam
grafu.

`ExecEntry` má jediný výstup `Then` a žádný vstup. Paleta po vložení první instance další
`ExecEntry` skryje; validátor přesto hlídá ručně upravené soubory a undo.

## Typ `exec`

`TypeIds.Exec` je uzavřený řídicí typ: kompatibilní je pouze `exec` → `exec`, ani univerzální `any`
ho nepřijme. Autor .NET packu ho zapíše například takto:

```csharp
[NodeOutput("True", typeof(Exec))]
[NodeOutput("False", typeof(Exec))]
public sealed class Branch : IGraphData
{
    [NodePort("In", Multiple = true)] public Exec? In { get; set; }
    [NodePort("Condition")] public bool Condition { get; set; }
}
```

Exec vstup je vždy port, nikdy field ani položka Details panelu. `Multiple = true` dovoluje sloučit
více předchozích větví. Exec výstup smí mít nejvýš jeden link; větvení se proto vyjadřuje více
pojmenovanými výstupy, ne dvěma linky z téhož pinu. Vizuálně je exec port řídicí štítek: dutý bez
napojení, plný po připojení. Výchozí barva je `#E8E8E8`.

## Parametry

Veřejné vstupy se deklarují `GraphInputNode`. Pole `Name`, `InputTypeName`, `DefaultValue`, `Order`
a `Description` se editují v Details panelu uzlu. `Exposure` určuje, zda vložený graf nabídne
parametr jako port nebo jako inline field.

Export seřadí deklarace podle `Order` do `inputs` a jejich konzumenty nahradí markerem `$param`:

```jsonc
{
  "inputs": [
    { "name": "Ticket", "type": "string", "default": "", "description": "Číslo ticketu" }
  ],
  "nodes": [
    { "$id": "n2", "$type": "sandbox/Branch", "Condition": { "$param": "Ticket" } }
  ]
}
```

Jméno je veřejný klíč, takže `GraphValidator` hlásí `Validation_DuplicateParameter` pro duplicity.
Více konzumentů stejného parametru dostane stejný `$param`; marker nemá `$id`.

## Návratové hodnoty a Return

Návraty se deklarují v grafovém Details panelu jako seřazený seznam `GraphSettings.Outputs`. Každá
deklarace má stabilní id, jméno, type id a příznak `Multiple`. Z těchto deklarací si každý
`ned/ReturnNode` dynamicky vytvoří hodnotové vstupy; navíc má slučovací exec vstup `In`.

Exec graf může mít:

- žádnou deklaraci a žádný Return — běžný kořenový workflow, který jen vykoná kroky;
- jednu či více deklarací a jeden či více Return uzlů — proceduru/funkci s různými konci větví.

Každá větev připojí své hodnoty do vlastního Return. V exportu jsou kořenové `outputs` pouze
deklarace bez `value`; konkrétní hodnoty jsou vstupy dosaženého Return uzlu:

```jsonc
{
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

Polový návrat (`Multiple = true`) přijme více zdrojů a nese ploché pole. Polový producer se při
exportu označí `$spread`, stejně jako u ostatních polových vstupů.

`OutputNode` do exec toku nepatří: tahá hodnoty pozpátku bez informace, která větev skončila. Paleta
ho proto skryje a validátor jeho případný výskyt hlásí jako chybu.

## Plochý export

Exec export obsahuje `entry`, ploché `nodes` a řídicí `exec` hrany. Smyčka je legální; exporter uzel
navštíví jen jednou a zpětnou hranu normálně zapíše. Cílový pin se dnes neexportuje, protože runtime
smlouva předpokládá jeden exec vstup na uzel.

Datový producer, který je zároveň exec krokem, se nesmí vnořit do konzumenta: vnoření by pro runtime
znamenalo druhé provedení stejného kroku. Datový tah z exec uzlu proto vždy používá `$ref` a u
pojmenovaného výstupu také `$output`. Čistě datový producer se může definovat inline uvnitř prvního
konzumenta. Úplný formát popisuje [05-export.md](05-export.md).

## Volání exec grafu

Exec graf lze vložit do jiného exec grafu jako funkci. `SubgraphNodeModel` dostane exec vstup a
výstup `Then`, datové parametry a deklarované datové výstupy. Volající uzel se exportuje s `$call`;
tělo funkce se jednou zapíše do kořenového pole `functions` se svým `entry`, `nodes`, `exec`,
`inputs` a deklaracemi `outputs`.

Stejná funkce může být volána vícekrát bez duplikace definice. Exporter hlídá zásobník právě
stavěných funkcí, takže ani rekurzivní reference nezpůsobí nekonečnou expanzi.

Datový subgraf lze použít i uvnitř exec grafu jako čistý výraz. Obráceně to neplatí: exec funkce v
datovém grafu je validační chyba, protože datový export řídicí hrany neobsahuje.

## Validace

Pro exec graf platí:

- právě jeden `ExecEntry`;
- žádný `OutputNode`;
- při alespoň jedné návratové deklaraci alespoň jeden `ReturnNode`;
- uzly nedosažitelné z entry po exec hranách jsou orphan;
- datoví producenti vstupů dosažitelných exec uzlů se považují za použité;
- povinné porty, typy a arity, chybějící packy a duplicitní parametry se kontrolují stejně jako u
  datového toku.

Validátor zatím nekontroluje, zda exec krok čte data z kroku, který se provede až později, ani zda
každá možná větev funkce skončí Return uzlem.

## Zatím nepodporované

- statická kontrola datových závislostí proti exec pořadí;
- více rozlišitelných cílových exec vstupů v runtime exportu;
- analýza úplnosti návratů na všech větvích.

Zpět na [mapu dokumentace](README.md).

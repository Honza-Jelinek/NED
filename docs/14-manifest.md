# Manifest — jak NED pozná cizí typy uzlů

> **NED** = Node Editor (zkratka napříč docs).

## Proč

Editor se dřív o typech packu dozvídal **reflexí** nad assembly předanou hostitelem. To znamenalo, že NED musel být zkompilovaný proti každému packu a nemohl existovat jako samostatná aplikace.

Klíčové pozorování: **doménové typy neobsahují žádný spustitelný kód, který by NED potřeboval.**
Typy jako `Add` jsou statická metadata (`[NodeInfo]`, `[NodeField]`, `[NodePort]`) plus
auto-properties. Zvláštní runtime typování vestavěných interface uzlů řeší `DataNodeModel` podle
jejich stabilního type id. Editor tedy potřebuje znát **tvar**, ne kód.

Manifest je ten tvar zapsaný jako JSON. Reflexe se přestěhovala z editoru do build-time generátoru.

**Autorský zážitek se nemění.** Dál se píší anotované C# třídy — úroveň 0 z [07-distribution.md](07-distribution.md) platí beze změny.

## Formát

```jsonc
{
  "ManifestVersion": 1,
  "Pack": { "Id": "sample", "Name": "Sample node pack", "Version": "1.0.0" },
  "Enums": [
    { "Id": "sample/TaskState", "Values": ["New", "Done"] }
  ],
  "Types": [
    {
      "Id": "sample/AdvancedTask",
      "Name": "Advanced Task", "Category": "Tasks", "Color": "#caa44b", "Icon": "★",
      "Extends": ["sample/Task"],
      "Outputs": [ { "Name": "Out", "Type": "sample/AdvancedTask" } ],
      "Inputs": [
        { "Name": "ChildName", "Label": "Child Name", "Kind": "Field",
          "Type": "string", "Default": "New task", "HasExplicitDefault": true, "Optional": true }
      ]
    }
  ]
}
```

Model je v `NED.Abstractions/Manifest/NodeManifest.cs` — čisté POCO bez závislosti na serializátoru, aby `NED.Abstractions` zůstal netstandard2.0 bez závislostí.

### Výstupy

Každý výstup má stabilní jméno. Jediný běžný výstup se jmenuje `Out`; uzel s více
výstupy je deklaruje samostatně a sink má `Outputs: []`. Jméno se ukládá do
`GraphLinkDto.FromPort`, takže save/load nikdy nemusí odhadovat, který výstup link používá.
`Multiple: true` znamená, že jeden drát z výstupu nese pole prvků deklarovaného typu.

```csharp
[NodeOutput("Index", typeof(int))]
[NodeOutput("Completed")]
public sealed class ForLoop : IGraphData { }

[NodeOutput(typeof(double), Multiple = true)]
public sealed class NumberList : IGraphData { }
```

`Extends` výstupního portu se dohledává podle typu konkrétního výstupu. Pojmenovaný
výstup typu `flow/Child` proto nese předky `flow/Child`, nikoli předky uzlu `flow/ForLoop`.

Řídicí hrana používá vyhrazené type id `exec`. Exec vstup musí být vždy `Kind: "Port"`
(nikdy field ani details); `Multiple: true` dovolí připojit více předchozích větví. Exec výstupy
jsou běžné pojmenované položky v `Outputs`, například `True` a `False` u uzlu `Branch`.

### Identifikátory

`Id` je `"<pack>/<local>"` a **záměrně se neváže na CLR `FullName`**. Lokální část je `[NodeInfo(Id = "…")]`, jinak jméno třídy — namespace se nepoužívá.

Důsledek: **přejmenování třídy nebo přesun namespace se uložených grafů nedotkne**, pokud si autor `Id` jednou připne. To je jednodušší a trvalejší než hromadící se rename aliasy.

Totéž platí pro pole: `[NodeField("Name", Id = "displayName")]` a `[NodePort("A", Id = "left")]`.

### Vokabulář typů

Skaláry mají krátká id bez packu: `string`, `bool`, `byte`, `short`, `int`, `long`, `float`, `double`, `decimal`, plus `any`. Všechno ostatní je `"pack/typ"`. Konstanty a pravidla kompatibility jsou v `NED.Abstractions/Manifest/TypeIds.cs`.

### `Extends` nese polymorfii

Bez tohohle pole by `PerkChild` nešel připojit do portu typu `Perk`. Kompatibilita portu se vyhodnocuje jako:

1. vstup je `any` → přijme cokoliv
2. `output.Id == input.Id`, nebo `output.Extends` obsahuje `input.Id`
3. číselné rozšíření podle `TypeIds.NumericRank` (int → double ano, opačně ne)

Je to jediná věc, kterou manifest musí nést navíc oproti dnešním atributům.

### Enumy

Musí být v manifestu i s hodnotami — widget z nich staví dropdown a bez seznamu není z čeho.

## Generátor

`src/NED.Manifest.Generator` (`ned-manifest`). Jediné místo v systému, kde nad doménovými typy ještě běží reflexe. Při běžném načtení manifestu editor pracuje pouze s daty; při generování z projektu spustí generátor mimo proces editoru.

```bash
ned-manifest src/Sandbox/bin/Debug/netstandard2.0/Sandbox.dll -o src/Sandbox/Sandbox.nodes.json
```

Generátor umí jako vstup také celý .NET projekt. Projekt nejdřív sestaví a cestu k výsledné
assembly zjistí z MSBuildu, takže není závislý na ručně poskládané cestě přes `bin/Debug`:

```bash
ned-manifest src/Sandbox/Sandbox.csproj --configuration Debug
ned-manifest src/MyPack/MyPack.fsproj --framework net10.0 --output out/MyPack.nodes.json
```

Podporované projektové přípony jsou `.csproj`, `.fsproj` a `.vbproj`. U multi-target projektu
je nutné zvolit TFM přes `--framework`; generátor nikdy nehádá, pro který target má manifest
vzniknout.

### Generování z NED

Dialog **Manage libraries → Node packs** umí projekt vybrat, sestavit a výsledný manifest rovnou
přidat do workspace. Před prvním spuštěním se zobrazí potvrzení, protože build projektu spouští
cizí build skripty. Průběh i chyby se zobrazují v dialogu a běh lze zrušit. U vygenerovaného packu
si workspace pamatuje provider, zdroj a jeho options, takže jej lze později přegenerovat jedním
tlačítkem.

Tohle jsou dvě odlišné hranice důvěry:

- **Přidat manifest** pouze načte JSON. Nespouští doménovou assembly ani jiný cizí kód.
- **Přidat projekt** spustí `dotnet build` nad vybraným projektem, tedy také jeho MSBuild targety.
  Proto NED předem žádá o potvrzení a build i reflexi provádí v samostatném procesu. Izolace chrání
  proces editoru, ale z buildu nedělá bezpečnou operaci nad nedůvěryhodným projektem.

Editor nemá generování svázané s C#. Rozhraní `INodePackGeneratorProvider` popisuje pouze zdroj,
options a výsledný manifest. Vestavěný provider `dotnet` přijímá všechny tři běžné .NET projektové
jazyky; další provider může stejným způsobem obsloužit například TypeScript, Rust nebo Unity
projekt a spustit jejich vlastní nástroj. Editor následně pracuje jen s přenositelným JSON
manifestem podle [schématu verze 1](ned-node-manifest-v1.schema.json), nikoli s typy daného jazyka.

Externí generátor může v režimu strojového výstupu vracet jediný JSON objekt na stdout:

```json
{
  "ProtocolVersion": 1,
  "Success": true,
  "ManifestPath": "D:/packs/example.nodes.json",
  "Pack": { "Id": "example", "Version": "1.0.0" },
  "TypeCount": 12,
  "EnumCount": 2,
  "Diagnostics": []
}
```

NED odpovědi slepě nevěří: uvedený soubor znovu načte, zkontroluje identitu packu a vyžaduje
alespoň jeden typ. Selhání používá stejný objekt s `Success: false` a polem `Error`.

Pack identitu bere z `[assembly: NodePack("sample", Version = "1.0.0")]`; bez atributu ji odvodí z názvu assembly.

Výchozí hodnoty polí čte z čerstvé instance, takže typ musí mít bezparametrický konstruktor. Typ, který ho nemá, se **ohlásí při generování** — dřív se to projevilo až pádem editoru při kliknutí v paletě.

### Když se pack nenačte

Poškozený nebo chybějící manifest nesmí shodit start editoru, ale nesmí ani zmizet beze stopy. `NedOptions` selhání posbírá (`AddNed` běží dřív, než v DI existuje `INedNotifier`, takže chyba nemá kam odejít), `NedCatalog.Issues` je převezme a UI je při startu vysype do Problems panelu jménem souboru i důvodem.

Bez toho by uživatel viděl jen placeholdery a hlášku „pack není načtený" — pravdu o následku a lež o příčině.

## Kolize

Type id je `"<pack>/<node>"`, takže dva packy můžou mít oba uzel `Add` a nekoliduje to. Zbývají tři situace a všechny se hlásí do Problems panelu:

| Situace | Co katalog udělá |
|---|---|
| **tentýž pack id dvakrát** (dvě verze ve složce, nebo dva nezávislí autoři) | packy sloučí, nekolidující typy zachová a ohlásí nejednoznačné id |
| **type id deklarují dva různé packy** (pack použil cizí prefix) | first-wins, hlásí vítěze i poraženého u každého typu |
| **stejné zobrazované jméno v různých packech** | není chyba; paleta u nich navíc ukáže pack |

Pack id není globálně registrované, proto se duplicitní packy mergují. Konkrétní kolize type id je deterministicky first-wins a vždy se ohlásí.

Pack se v paletě zobrazuje jen u jmen z `NedCatalog.AmbiguousNames`. U ostatních uzlů by ten text byl šum, který přebije signál.

### Přegenerování

```powershell
.\generate-manifests.ps1
```

Generace **není** zapojená do buildu. Zapomenuté spuštění chytí `ManifestDriftTests` — přidáš property, zapomeneš přegenerovat, `dotnet test` spadne.

> `ponytail:` automatická generace by potřebovala cross-TFM `ProjectReference` na generátor (netstandard2.0 doména → net10.0 exe) a umí zaseknout build celého řešení. Až bude `NED.Abstractions` NuGet balík, patří to do jeho `.targets` jako tool package.

## Workspace — kdo rozhoduje, který pack se načte

Volba packu patří do **konfigurace**, ne do session; jinak si ji uživatel vybírá při každém spuštění a špatná volba udělá z celého grafu placeholdery. Sídlí ve stejném souboru jako knihovní kořeny (`NedOptions.LibraryConfig`):

```json
{
  "Roots": ["D:/NED/graphs"],
  "Packs": [
    {
      "Path": "D:/NED/packs/Sandbox.nodes.json",
      "Enabled": true,
      "Generation": {
        "Provider": "dotnet",
        "Source": "D:/NED/src/Sandbox/Sandbox.csproj",
        "Options": { "Configuration": "Debug" }
      }
    }
  ]
}
```

Edituje se v dialogu **Manage libraries**, který má sekci Node packs vedle sekce knihoven. Starší formát (holé pole kořenů) se pořád načte, aby uživatel o knihovny nepřišel.
Starší objekt s `Manifests: []` se také dál načítá; při první změně packů v dialogu se převede
na `Packs`. Generační recept je záměrně obecný a jeho `Options` interpretuje pouze zvolený provider.

Pack nalezený přes `ManifestFolder` je implicitně zapnutý a v dialogu má stejné zaškrtávátko jako
ručně přidaný pack. Workspace kvůli tomu při startu nic nezapisuje: záznam podle normalizované cesty
vznikne až ve chvíli, kdy uživatel stav změní. Explicitní `Enabled: false` se aplikuje ještě před
sestavením katalogu, takže vypnutý pack se po restartu opravdu nenačte.

Hostitel může packy dodat i v kódu; oba zdroje se slučují:

```csharp
services.AddNed(o => o
    .ManifestFolder(Path.Combine(AppContext.BaseDirectory, "manifests"))   // packy dodané s aplikací
    .LibraryConfig(workspacePath));                                        // + co si přidal uživatel
```

`ManifestFolder` je konvence „packy vedle binárky" a bydlí v knihovně schválně — jinak by ten cyklus opsal každý shell zvlášť. Masku drží `ManifestFile.SearchPattern`, sdílená s generátorem.

Manifesty se sledují a po změně se katalog atomicky přestaví. Otevřené grafy projdou DTO round-tripem,
takže nové porty a typy se projeví bez restartu a odstraněné typy se změní na bezztrátové placeholdery.
Pokud se serializovaný dokument nezmění, tab zůstane čistý a zachová undo historii; výběr uzlů,
pan a zoom se obnoví podle stabilních id uzlů.

## Chybějící pack a novější schéma

`GraphSettings.RequiredPacks` se při uložení odvodí z typů použitých uzlů a zapisuje id i verzi
packu; vestavěný pack `ned` se nezapisuje, protože ho editor má vždy. Staré řetězcové položky
(`"sandbox"`) se dál načtou jako požadavek bez verze. Při otevření se id porovná s načtenými packy
a chybějící se ohlásí jménem.

Stejně tak `GraphDocument.SchemaVersion`: soubor z novější verze editoru se **načte**, ale uživatel dostane varování, že o něco může při uložení přijít.

Obojí jen hlásí, neblokuje. Uzly neznámého typu zůstanou jako placeholdery s netknutým DTO, takže uložení nic neztratí ani bez chybějícího packu.

## Vestavěné uzly

`OutputNode`, `ReturnNode`, `GraphInputNode` a `ExecEntry` procházejí **stejným generátorem** do
`src/NED.Core/Manifest/ned.builtin.nodes.json` (embedded resource) — jedna cesta kódu, žádná
zvláštní větev.

Jejich dynamické chování se v manifestu vyjádřit nedá: `GraphInputNode` typuje výstup podle
type-pickeru a `OutputNode`/`ReturnNode` dostávají hodnotové vstupy z `GraphSettings.Outputs`.
Generátor proto emituje jen statický základ a runtime ho doplní hookem klíčovaným na `Id`.

## Co to odemyká

- **NED jako samostatná aplikace** — manifest je samonosný, pošleš někomu `nodes.json` a edituje grafy pro tvou doménu bez jediné dll
- **Hostování v serveru** — server nikdy nespouští cizí kód

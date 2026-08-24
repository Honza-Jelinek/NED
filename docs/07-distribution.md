# Distribuce NEDa — monorepo, NuGet, GitHub

Týká se znovupoužitelné dvojice `NED.Abstractions` + `NED.Core`. Konkrétní node packy si vytváří a distribuuje každý uživatel samostatně.

## Monorepo teď, NuGet kdykoliv později

Co umožní pozdější vyjmutí, **není fyzické umístění, ale dodržená disciplína závislostí**. Dokud `NED.*` nikdy nereferencuje konkrétní uživatelský pack, je extrakce mechanická:

```
dnes:    <ProjectReference Include="..\NED.Core\NED.Core.csproj" />
později:  <PackageReference Include="NED.Core" Version="1.0.0" />
```

Nic jiného se nemění. Doporučení: drž oba projekty v monorepu jako `ProjectReference`, ale **chovej se k nim, jako by už byly cizí balík** — žádné rychlé zásahy do `Core` kvůli specifické potřebě jednoho packu. Ta sebekázeň je celá cena za budoucí znovupoužitelnost.

## NuGet vs GitHub — to není volba, děláš obojí

Jsou to dvě různé osy:

- **GitHub** = kde žije *zdroják*. Lidé tam čtou kód, učí se, forkují, posílají PR.
- **NuGet** = *distribuční kanál* pro hotový binární balík. `dotnet add package`, semver, čisté verzování.

Standardní open-source vzor je **obojí najednou**: zdroj na GitHubu, buildnuté artefakty publikované na NuGet (klidně automaticky přes GitHub Actions na git tag). Konzument, který to chce jen použít, sáhne po NuGetu; kdo chce vidět do střev nebo přispět, jde na GitHub.

Jediná reálná otázka je **binárka (NuGet) vs zdroj zatažený přímo** (git submodule / template repo) — trade-off mezi čistým verzováním a plnou hackovatelností. Pro NED, kde lidé typicky chtějí hloubkovou customizaci, je odpověď: dej jim NuGet pro pohodlí **a** kvalitní extension pointy (úrovně 3–4 níže), aby zdroj na GitHubu nepotřebovali sahat — ale měli ho k dispozici, když budou chtít.

## Co rozhoduje o forku — customizační úrovně

Klíčová metrika kvality knihovny: kolik se dá udělat **bez forku**. Od nejlevnějšího po nejdražší:

| Úroveň | Mechanismus | Stav dnes | Příklad |
|---|---|---|---|
| 0 — anotace | atributy na vlastních třídách | ✅ hotovo | `[NodeInfo]`, `[NodeField]` → picker + node zdarma |
| 1 — vzhled | `ned-theme.json` + CSS proměnné | ✅ hotovo | barvy kategorií/portů, ikony, mřížka plátna |
| 2 — vlastní node/widget | vlastní `NodeModel` + widget + `RegisterComponent` | ✅ vzor existuje (`SubgraphNodeModel`) | node, jehož porty nepochází z reflexe |
| 3 — vlastní editor pole | `NodeFieldInput` rozšíření / renderer per typ | ⚠️ částečně | `Color` → color picker místo text inputu |
| 4 — hooky / callbacky | `NedOptions` + host callbacky (`OnWriteFile`, `OnExport…`) | ✅ pro I/O; ⚠️ pro validaci/pipeline | vlastní save/load cesta, externí akce, branding |
| 5 — fork | zdroj na GitHubu | — | cokoliv ostatního |

Cíl: 95 % uživatelů se vejde do úrovní 0–2, k forku (5) sáhne málokdo. **Kvalita extension pointů 3–4 rozhoduje o tom, jak často je fork potřeba.**

> **Pozor — žádný `[CustomNodeWidget]` atribut neexistuje.** Vlastní node se přidá jako plnohodnotná ZBD registrace: vlastní `NodeModel` (porty staví z dat, ne reflexí) + vlastní `*.razor` widget + `diagram.RegisterComponent<Model, Widget>()`. Přesně tak je postavený `SubgraphNodeModel`/`SubgraphNodeWidget` — viz [03-ned-core-engine.md](03-ned-core-engine.md). Standardní vzor Blazor RCL (parametry komponent + `RenderFragment` sloty + CSS isolation s `::deep`) je k dispozici pro skinování.

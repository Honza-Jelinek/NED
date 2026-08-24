# Struktura projektu — vrstvy a závislosti

## Princip modularity

NED nezná konkrétní obsah node packů. Umí uzly, porty, linky, ukládání, export a generování UI z metadat. Autor packu anotuje vlastní třídy kontraktem z `NED.Abstractions`; generátor z assembly vytvoří manifest a editor čte pouze tento manifest.

## Projekty

```text
src/
├── NED.Abstractions       atributy, IGraphData a model manifestu       netstandard2.0
├── NED.Core               Blazor editor, persistence a export          net10.0
├── NED.Manifest.Generator ned-manifest: assembly/projekt → manifest    net10.0
├── NED.Shell.Wpf          WPF host s Blazor WebView                    net10.0-windows
└── Sandbox                ukázkový node pack                           netstandard2.0
```

- `NED.Abstractions` je lehký autorský kontrakt bez UI závislostí.
- `NED.Core` je znovupoužitelné editorové jádro.
- `NED.Manifest.Generator` vytváří `*.nodes.json` z anotovaných projektů.
- `NED.Shell.Wpf` je composition root a spustitelná desktopová aplikace.
- `Sandbox` demonstruje číselné, textové, logické a řídicí uzly.

## Povolené závislosti

```text
NED.Core               ──▶ NED.Abstractions
NED.Manifest.Generator ──▶ NED.Abstractions
Sandbox                ──▶ NED.Abstractions
NED.Shell.Wpf          ──▶ NED.Core
NED.Shell.Wpf          ──▶ NED.Manifest.Generator (jen build nástroj)
```

`NED.Core` ani shell nereferencují assembly uživatelských packů. Shell načte manifesty ze složky `manifests` a z cest uložených ve workspace konfiguraci.

## Composition root

`NED.Shell.Wpf` zaregistruje editor a předá mu hostitelské služby:

```csharp
services.AddWpfBlazorWebView();
services.AddNed(options => options
    .ManifestFolder(Path.Combine(AppContext.BaseDirectory, "manifests"))
    .LibraryConfig(Path.Combine(appData, "NED", "libraries.json")));
```

`Pages/Index.razor` doplní callbacky pro WPF dialogy, práci se soubory a systémové akce. Veškeré modely plátna, widgety a editorové příkazy zůstávají v `NED.Core`.

## Externí závislosti

- `NED.Core`: ASP.NET Core Components, Microsoft.Extensions, MudBlazor a Z.Blazor.Diagrams.
- `NED.Shell.Wpf`: Blazor WebView pro WPF, WebView2 a Z.Blazor.Diagrams.
- `NED.Abstractions`, `NED.Manifest.Generator` a `Sandbox`: nemají přímé externí runtime balíčky nad rámec cílového frameworku.
- Testy: xUnit.net v3 nad Microsoft Testing Platform.

Přesné verze, transitivní závislosti a licence uvádí [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md).

## Přidání nového typu

1. Vytvořte třídu implementující `IGraphData` v samostatném node packu.
2. Přidejte `[NodeInfo]`, `[NodeField]`, `[NodePort]` a případně `[NodeOutput]`.
3. Vygenerujte manifest nástrojem `ned-manifest` nebo dialogem správy packů.
4. Načtěte výsledný `*.nodes.json` v editoru.

Podrobný formát a workflow popisuje [14-manifest.md](14-manifest.md).

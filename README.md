# NED

NED (*Node Editor*) je desktopový editor typovaných uzlových grafů. Z manifestů vytvořených z anotovaných .NET projektů sestaví paletu uzlů, porty a formuláře; grafy ukládá a exportuje do verzovaného JSON formátu. Editor nezná ani nespouští kód z uživatelských node packů.

## Co repozitář obsahuje

```text
NED.Abstractions  <-  Sandbox
        ^
        +-----------  NED.Core  <-  NED.Shell.Wpf
        +-----------  NED.Manifest.Generator
```

| Projekt | Úloha |
| --- | --- |
| [`NED.Abstractions`](src/NED.Abstractions/README.md) | Lehký kontrakt pro typy uzlů, jejich metadata a manifesty. |
| [`NED.Core`](src/NED.Core/README.md) | Editorové jádro: plátno, typované porty, persistence, export, knihovny a UI. |
| [`NED.Shell.Wpf`](src/NED.Shell.Wpf/README.md) | Spustitelný WPF host s Blazor WebView. |
| `NED.Manifest.Generator` | Nástroj `ned-manifest` pro převod anotované assembly nebo projektu na `*.nodes.json`. |
| `Sandbox` | Ukázkový node pack pro čísla, text, logiku a řídicí tok. |

## Požadavky

- Windows 10 nebo novější pro desktopovou aplikaci
- .NET SDK 10

Repozitář používá Microsoft Testing Platform nastavenou v `global.json`.

## Sestavení a spuštění

```powershell
dotnet restore NED.sln
dotnet build NED.sln
dotnet run --project src/NED.Shell.Wpf/NED.Shell.Wpf.csproj
```

Testy spustíte příkazem:

```powershell
dotnet test --solution NED.sln
```

## Vlastní node pack

1. Vytvořte projekt, který referencuje `NED.Abstractions`.
2. Třídy implementující `IGraphData` popište atributy `[NodeInfo]`, `[NodeField]`, `[NodePort]` a `[NodeOutput]`.
3. Vygenerujte manifest nástrojem `ned-manifest`.
4. Přidejte `*.nodes.json` v dialogu správy packů nebo jej vložte do složky `manifests` vedle aplikace.

```csharp
[NodeInfo("Add", Category = "Math")]
[NodeOutput(typeof(double))]
public sealed class Add : IGraphData
{
    [NodePort("A")] public double A { get; set; }
    [NodePort("B")] public double B { get; set; }
}
```

Podrobnosti jsou v [dokumentaci](docs/README.md), zejména v popisu [kontraktu](docs/01-abstractions.md), [architektury](docs/06-project-structure.md), [manifestů](docs/14-manifest.md) a [ovládání editoru](docs/11-editor-ux.md).

## Licence a třetí strany

Zdrojový kód NED je dostupný pod [licencí MIT](LICENSE). Použité knihovny a staticky přibalené webové assety mají vlastní licence; jejich přehled a požadovaná oznámení jsou v [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

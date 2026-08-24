# NED.Shell.Wpf

Tenká desktopová hostitelská aplikace pro `NED.Core`. Obsahuje vstupní bod, WPF okno, Blazor WebView a platformně specifické operace; editorové funkce zůstávají v `NED.Core`.

## Spuštění

Vyžaduje Windows a .NET SDK 10.

```powershell
dotnet run --project src/NED.Shell.Wpf/NED.Shell.Wpf.csproj
```

## Složení aplikace

- `App.xaml.cs` sestaví DI kontejner, inicializuje jazyk a registruje NED.
- `MainWindow.xaml` hostuje `BlazorWebView`.
- `Pages/Index.razor` vloží `NedCanvas` a implementuje WPF dialogy, práci se soubory a systémové akce.
- `LanguageStore` a `LayoutFileStore` ukládají nastavení do `%AppData%/NED`.

Node packy se nenačítají jako assembly. Shell při startu načte manifesty ze složky `manifests` vedle aplikace a další packy lze spravovat v uživatelském rozhraní. Projekt proto referencuje pouze `NED.Core` a nástroje potřebné pro hostování a generování manifestů.

Další informace: [struktura řešení](../../docs/06-project-structure.md) a [host callbacky a ovládání editoru](../../docs/11-editor-ux.md).

using Microsoft.Extensions.DependencyInjection;
using NED.Core;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows;
using NED.Shell.Wpf.NodePacks;

namespace NED.Shell.Wpf;

public partial class App : Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    public App()
    {
        // Jazyk aplikuj PŘED stavbou WebView (na UI vlákně), aby ho použil už první render.
        var lang = LanguageStore.Load();
        if (lang is not null) ApplyCulture(lang);

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        services.AddNodePackGeneration();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif
        // Registruj NED a ukaž ho na node packy (composition root). Packy jsou manifesty
        // ve složce vedle .exe — shell nemá referenci na žádnou doménu a její typy nikdy
        // nespouští. Viz docs/14-manifest.md.
        services.AddNed(o => o
            .ManifestFolder(System.IO.Path.Combine(AppContext.BaseDirectory, "manifests"))
            .LibraryConfig(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NED", "libraries.json")));
        ServiceProvider = services.BuildServiceProvider();
        Resources.Add("services", ServiceProvider);
    }

    /// <summary>Nastaví kulturu na celý proces i aktuální (UI) vlákno.</summary>
    public static void ApplyCulture(string culture)
    {
        var ci = new CultureInfo(culture);
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;
        Thread.CurrentThread.CurrentCulture = ci;
        Thread.CurrentThread.CurrentUICulture = ci;
    }

    /// <summary>
    /// Restart procesu — jediný spolehlivý způsob jak změnit kulturu za běhu.
    /// Blazor rendery „protékají" ExecutionContextem zachyceným při bootstrapu,
    /// takže změna kultury v běžícím procesu se nepropíše. Nový proces ji ale
    /// načte z settings.json v App ctoru, ještě před prvním renderem.
    /// </summary>
    public static void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        // Stejně jako u zavírání: nečekej na pomalý WebView2 teardown. Nový proces
        // už běží samostatně, tenhle ukonči rovnou.
        Environment.Exit(0);
    }
}

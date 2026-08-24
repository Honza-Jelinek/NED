using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace NED.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registruje NED do DI. Hostitel přes <paramref name="configure"/> řekne, které
    /// manifesty načíst (viz docs/14-manifest.md).
    /// </summary>
    /// <param name="addMudServices">
    /// false, pokud hostitel volá <c>AddMudServices</c> sám — dvojí registrace zdvojí
    /// MudBlazor providery.
    /// </param>
    public static IServiceCollection AddNed(
        this IServiceCollection services, Action<NedOptions> configure, bool addMudServices = true)
    {
        var options = new NedOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton(options.Theme);

        // Katalog i index assetů jsou singletony: katalog je immutable a index drží
        // FileSystemWatchery a projde při startu celou knihovnu. Jako scoped by se
        // v hostiteli s víc scopy (Blazor Server = scope na záložku prohlížeče) obojí
        // dělalo znovu pro každou záložku.
        // Manifesty z workspace (uživatel je přidal za běhu) se připojí k těm, které
        // registroval hostitel v kódu.
        services.AddSingleton<Manifest.ManifestStore>();
        services.AddSingleton(serviceProvider =>
        {
            var fileManifests = options.LoadFileManifests();
            var catalog = new NedCatalog(
                options.Manifests.Concat(fileManifests),
                options.TranslatorAssemblies,
                options.LoadIssues);
            serviceProvider.GetRequiredService<Manifest.ManifestStore>().Start(catalog);
            return catalog;
        });
        services.AddSingleton<INedNotifier, NedNotifier>();
        services.AddSingleton<Assets.AssetIndex>();

        services.AddScoped<LayoutStore>();
        services.AddScoped<NodeInspector>();
        services.AddScoped<ProblemsService>();

        if (addMudServices) services.AddMudServices();
        services.AddLocalization();
        services.AddSingleton<Resources.LanguageRegistry>();
        services.AddSingleton<ShutdownGuard>();
        return services;
    }
}

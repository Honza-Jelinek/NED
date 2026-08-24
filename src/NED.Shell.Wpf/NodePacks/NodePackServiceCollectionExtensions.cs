using Microsoft.Extensions.DependencyInjection;
using NED.Core.NodePacks;

namespace NED.Shell.Wpf.NodePacks;

public static class NodePackServiceCollectionExtensions
{
    public static IServiceCollection AddNodePackGeneration(this IServiceCollection services)
    {
        services.AddSingleton<INodePackGeneratorProvider, DotNetNodePackGeneratorProvider>();
        return services;
    }
}

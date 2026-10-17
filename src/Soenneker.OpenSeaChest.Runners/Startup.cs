using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.Process.Registrars;
using Soenneker.OpenSeaChest.Runners.Utils;
using Soenneker.OpenSeaChest.Runners.Utils.Abstract;
using Soenneker.Managers.Runners.Registrars;


namespace Soenneker.OpenSeaChest.Runners;

/// <summary>
/// Console type startup
/// </summary>
public static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public static void ConfigureServices(IServiceCollection services)
    {
        services.SetupIoC();
    }

    public static IServiceCollection SetupIoC(this IServiceCollection services)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
                .AddProcessUtilAsSingleton()
                .AddRunnersManagerAsSingleton();

        return services;
    }
}


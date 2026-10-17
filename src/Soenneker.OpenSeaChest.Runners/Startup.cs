using Microsoft.Extensions.DependencyInjection;
using Soenneker.GitHub.Repositories.Releases.Registrars;
using Soenneker.Utils.File.Download.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Compression.Tar.XZ.Registrars;
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
                .AddGitHubRepositoriesReleasesUtilAsSingleton()
                .AddFileDownloadUtilAsSingleton()
                .AddFileUtilAsSingleton()
                .AddDirectoryUtilAsSingleton()
                .AddTarXZUtilAsSingleton()
                .AddRunnersManagerAsSingleton();

        return services;
    }
}


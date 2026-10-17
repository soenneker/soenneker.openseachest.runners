using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soenneker.OpenSeaChest.Runners.Utils.Abstract;
namespace Soenneker.OpenSeaChest.Runners;

public sealed class ConsoleHostedService(IFileOperationsUtil files, IHostApplicationLifetime lifetime,
    ILogger<ConsoleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await files.Process(stoppingToken);
            Environment.ExitCode = 0;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Environment.ExitCode = 130;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "openSeaChest update failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}

using Soenneker.OpenSeaChest.Runners.Utils.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.OpenSeaChest.Runners.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class OpenSeaChestRunnerTests : HostedUnitTest
{
    private readonly IFileOperationsUtil _runner;

    public OpenSeaChestRunnerTests(Host host) : base(host)
    {
        _runner = Resolve<IFileOperationsUtil>(true);
    }

    [Test]
    public void CanResolveUpdater()
    {

    }
}


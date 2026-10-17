using System.Threading;
using System.Threading.Tasks;
namespace Soenneker.OpenSeaChest.Runners.Utils.Abstract;

/// <summary>Downloads official openSeaChest distributions and updates the binaries package.</summary>
public interface IFileOperationsUtil
{
    /// <summary>
    /// Discovers the latest stable GitHub release on every invocation, validates and stages its binaries, and publishes only changed content.
    /// Set OpenSeaChest:OutputDirectory to an empty directory to stage locally without publishing.
    /// Archives are extracted using the shared compression utilities.
    /// </summary>
    ValueTask Process(CancellationToken cancellationToken = default);
}

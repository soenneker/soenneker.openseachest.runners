using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Soenneker.GitHub.Repositories.Releases.Abstract;
using Soenneker.GitHub.OpenApiClient.Models;
using System.Security.Cryptography;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Compression.Tar.XZ.Abstract;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Managers.Runners.Abstract;
using Soenneker.OpenSeaChest.Runners.Utils.Abstract;


namespace Soenneker.OpenSeaChest.Runners.Utils;

public sealed class FileOperationsUtil(IConfiguration configuration, IServiceProvider services, IGitHubRepositoriesReleasesUtil releasesUtil,
    IFileDownloadUtil fileDownloadUtil, IFileUtil fileUtil, IDirectoryUtil directoryUtil, ITarXZUtil tarXzUtil,
    ILogger<FileOperationsUtil> logger) : IFileOperationsUtil
{
    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Release> releases = await releasesUtil.GetAll("Seagate", "openSeaChest", cancellationToken);
        Release release = releases.Where(r => r.Draft == false && r.Prerelease == false)
            .OrderByDescending(r => r.PublishedAt ?? r.CreatedAt).FirstOrDefault()
            ?? throw new InvalidOperationException("No stable openSeaChest release was found.");
        string tag = release.TagName ?? throw new InvalidOperationException("Release tag is missing.");
        logger.LogInformation("Latest upstream openSeaChest release: {Tag}", tag);
        string temporary = await directoryUtil.CreateTempDirectory(cancellationToken);
        string staged = Path.Combine(temporary, "staged");
        try
        {
            await directoryUtil.Create(staged, cancellationToken: cancellationToken);
            foreach ((string rid, string suffix) in new[] { ("win-x64", "-win-x64.zip"), ("win-arm64", "-win-ARM64.zip"),
                         ("linux-x64", "-linux-x86_64-portable.tar.xz"), ("linux-arm64", "-linux-aarch64-portable.tar.xz") })
            {
                ReleaseAsset asset = release.Assets?.Single(a => a.Name?.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) == true)
                    ?? throw new InvalidDataException($"Release {tag} has no assets.");
                string archive = Path.Combine(temporary, rid + (suffix.EndsWith(".zip") ? ".zip" : ".tar.xz"));
                await Download(asset.BrowserDownloadUrl ?? throw new InvalidDataException("Asset URL is missing."), archive, cancellationToken);
                if (asset.Digest is { } digest)
                {
                    if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Unsupported release digest: {digest}");
                    await using FileStream stream = fileUtil.OpenRead(archive);
                    string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Checksum mismatch for {rid}.");
                }
                string extracted = Path.Combine(temporary, rid);
                await directoryUtil.Create(extracted, cancellationToken: cancellationToken);
                if (archive.EndsWith(".zip"))
                    await ZipFile.ExtractToDirectoryAsync(archive, extracted, cancellationToken);
                else
                    await tarXzUtil.DecompressAndExtract(archive, extracted, cancellationToken: cancellationToken);
                string extension = rid.StartsWith("win-") ? ".exe" : "";
                string basics = (await fileUtil.GetAllFileNamesInDirectoryRecursively(extracted, cancellationToken: cancellationToken))
                    .Single(path => Path.GetFileName(path) == "openSeaChest_Basics" + extension);
                string source = Path.GetDirectoryName(basics)!;
                string destination = Path.Combine(staged, rid);
                await directoryUtil.CopyDirectory(source, destination, cancellationToken: cancellationToken);
                foreach (string tool in new[] { "Info", "SMART", "PowerControl" })
                    if (!await fileUtil.Exists(Path.Combine(destination, "openSeaChest_" + tool + extension), cancellationToken))
                        throw new InvalidDataException($"Release {tag} is missing {tool} for {rid}.");
            }
            await Download($"https://raw.githubusercontent.com/Seagate/openSeaChest/{Uri.EscapeDataString(tag)}/LICENSE.md",
                Path.Combine(staged, "LICENSE.txt"), cancellationToken);
            await fileUtil.Write(Path.Combine(staged, "upstream.txt"),
                $"openSeaChest {tag}\nhttps://github.com/Seagate/openSeaChest/releases/tag/{tag}\nSource: https://github.com/Seagate/openSeaChest/tree/{tag}\n", cancellationToken: cancellationToken);
            string? output = configuration["OpenSeaChest:OutputDirectory"];
            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(output);
                if (await directoryUtil.Exists(output, cancellationToken) && (await directoryUtil.GetAllAsEnumerable(output, cancellationToken)).Any())
                    throw new IOException("OutputDirectory must be empty to avoid mixing releases.");
                await directoryUtil.CopyDirectory(staged, output, cancellationToken: cancellationToken);
                logger.LogInformation("Wrote {Tag} to {OutputDirectory}", tag, output);
                return;
            }
            await services.GetRequiredService<IRunnersManager>().PushIfChangesNeededForDirectory("OpenSeaChest", staged, "Soenneker.OpenSeaChest.Binaries",
                "https://github.com/soenneker/soenneker.openseachest.binaries", cancellationToken: cancellationToken, updateDetails: tag);
        }
        finally
        {
            await directoryUtil.DeleteIfExists(temporary, CancellationToken.None);
        }
    }

    private async ValueTask Download(string url, string path, CancellationToken cancellationToken)
    {
        if (await fileDownloadUtil.Download(url, filePath: path, cancellationToken: cancellationToken) is null)
            throw new IOException($"Could not download {url}.");
    }
}
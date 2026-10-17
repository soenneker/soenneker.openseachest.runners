using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Managers.Runners.Abstract;
using Soenneker.OpenSeaChest.Runners.Utils.Abstract;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.OpenSeaChest.Runners.Utils;

public sealed class FileOperationsUtil(IConfiguration configuration, IServiceProvider services, IProcessUtil processUtil,
    ILogger<FileOperationsUtil> logger) : IFileOperationsUtil
{
    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Soenneker.OpenSeaChest.Runners/1.0");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Seagate/openSeaChest/releases/latest");
        string? token = configuration["GH:Token"];
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument release = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        string tag = release.RootElement.GetProperty("tag_name").GetString() ?? throw new InvalidOperationException("Release tag is missing.");
        logger.LogInformation("Latest upstream openSeaChest release: {Tag}", tag);
        string temporary = Path.Combine(Path.GetTempPath(), $"openseachest-{Guid.NewGuid()}");
        string staged = Path.Combine(temporary, "staged");
        Directory.CreateDirectory(staged);
        try
        {
            foreach ((string rid, string suffix) in new[] { ("win-x64", "-win-x64.zip"), ("win-arm64", "-win-ARM64.zip"),
                         ("linux-x64", "-linux-x86_64-portable.tar.xz"), ("linux-arm64", "-linux-aarch64-portable.tar.xz") })
            {
                JsonElement asset = release.RootElement.GetProperty("assets").EnumerateArray()
                    .Single(a => a.GetProperty("name").GetString()!.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                string archive = Path.Combine(temporary, rid + (suffix.EndsWith(".zip") ? ".zip" : ".tar.xz"));
                await Download(client, asset.GetProperty("browser_download_url").GetString()!, archive, cancellationToken);
                if (asset.TryGetProperty("digest", out JsonElement digestElement) && digestElement.GetString() is { } digest)
                {
                    if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Unsupported release digest: {digest}");
                    await using var stream = File.OpenRead(archive);
                    string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Checksum mismatch for {rid}.");
                }
                string extracted = Path.Combine(temporary, rid);
                Directory.CreateDirectory(extracted);
                if (archive.EndsWith(".zip"))
                    ZipFile.ExtractToDirectory(archive, extracted);
                else
                    await processUtil.Start("tar", arguments: $"-xJf \"{archive}\" -C \"{extracted}\"", cancellationToken: cancellationToken);
                string extension = rid.StartsWith("win-") ? ".exe" : "";
                string basics = Directory.EnumerateFiles(extracted, "openSeaChest_Basics" + extension, SearchOption.AllDirectories).Single();
                string source = Path.GetDirectoryName(basics)!;
                string destination = Path.Combine(staged, rid);
                Directory.CreateDirectory(destination);
                // Retain upstream documentation and companion libraries alongside the executables.
                foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(destination, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                foreach (string tool in new[] { "Info", "SMART", "PowerControl" })
                    if (!File.Exists(Path.Combine(destination, "openSeaChest_" + tool + extension)))
                        throw new InvalidDataException($"Release {tag} is missing {tool} for {rid}.");
            }
            await Download(client, $"https://raw.githubusercontent.com/Seagate/openSeaChest/{Uri.EscapeDataString(tag)}/LICENSE.md",
                Path.Combine(staged, "LICENSE.txt"), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(staged, "upstream.txt"),
                $"openSeaChest {tag}\nhttps://github.com/Seagate/openSeaChest/releases/tag/{tag}\nSource: https://github.com/Seagate/openSeaChest/tree/{tag}\n", cancellationToken);
            string? output = configuration["OpenSeaChest:OutputDirectory"];
            if (!string.IsNullOrWhiteSpace(output))
            {
                output = Path.GetFullPath(output);
                if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                    throw new IOException("OutputDirectory must be empty to avoid mixing releases.");
                Directory.CreateDirectory(output);
                foreach (string file in Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(output, Path.GetRelativePath(staged, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
                logger.LogInformation("Wrote {Tag} to {OutputDirectory}", tag, output);
                return;
            }
            await services.GetRequiredService<IRunnersManager>().PushIfChangesNeededForDirectory("OpenSeaChest", staged, "Soenneker.OpenSeaChest.Binaries",
                "https://github.com/soenneker/soenneker.openseachest.binaries", cancellationToken: cancellationToken, updateDetails: tag);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task Download(HttpClient client, string url, string path, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, cancellationToken);
    }
}


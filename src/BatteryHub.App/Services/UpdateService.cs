using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BatteryHub.App.Views;

namespace BatteryHub.App.Services;

public sealed record UpdateInfo(Version Version, string Tag, string AssetUrl, string AssetName, string? ChecksumUrl, string PageUrl);

/// <summary>
/// Self-update from GitHub Releases. A release carries <c>BatteryHub-{version}-win-{arch}.exe</c> and
/// (optionally) a matching <c>.sha256</c>. Installing swaps the running exe (Windows allows renaming a
/// running executable), restarts, and the new process deletes the old copy.
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public UpdateInfo? Available { get; private set; }

    /// <summary>True only for the published single-file exe; dev builds (dotnet run) can check but not install.</summary>
    public static bool CanInstall =>
        string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location) &&
        Environment.ProcessPath is string p &&
        !Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    private static string ArchSuffix => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest";
        using var response = await Http.GetAsync(url, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Available = null; // no releases yet
            return null;
        }
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= AppInfo.Version)
        {
            Available = null;
            return null;
        }

        string? assetUrl = null, assetName = null, checksumUrl = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? string.Empty;
            string download = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
            if (name.EndsWith($"-{ArchSuffix}.exe", StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = download;
                assetName = name;
            }
            else if (name.EndsWith($"-{ArchSuffix}.exe.sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksumUrl = download;
            }
        }

        Available = assetUrl is null
            ? null
            : new UpdateInfo(version, tag, assetUrl, assetName!, checksumUrl, root.GetProperty("html_url").GetString() ?? AppInfo.RepoUrl);
        return Available;
    }

    /// <summary>Downloads, verifies and swaps in the new exe, then starts it. The caller should shut down afterwards.</summary>
    public async Task InstallAsync(UpdateInfo update, IProgress<int>? progress, CancellationToken ct = default)
    {
        if (!CanInstall || Environment.ProcessPath is not string exePath)
            throw new InvalidOperationException("Not running from a published executable.");

        string dir = Path.GetDirectoryName(exePath)!;
        string tempPath = Path.Combine(dir, "BatteryHub.update.tmp");
        string oldPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(exePath) + ".old.exe");

        using (var response = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? -1;
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tempPath);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0)
                    progress?.Report((int)(read * 100 / total));
            }
        }

        if (update.ChecksumUrl is not null)
        {
            string expected = (await Http.GetStringAsync(update.ChecksumUrl, ct)).Trim().Split(' ', '\t')[0];
            string actual;
            await using (var fs = File.OpenRead(tempPath))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                throw new InvalidDataException("Checksum mismatch.");
            }
        }

        if (File.Exists(oldPath))
            File.Delete(oldPath);
        File.Move(exePath, oldPath);
        try
        {
            File.Move(tempPath, exePath);
        }
        catch
        {
            File.Move(oldPath, exePath); // roll back
            throw;
        }

        Process.Start(new ProcessStartInfo(exePath, "--updated") { UseShellExecute = false });
    }

    /// <summary>Removes the previous exe left behind by an update.</summary>
    public static void CleanupPreviousVersion()
    {
        try
        {
            if (Environment.ProcessPath is not string exePath)
                return;
            string oldPath = Path.Combine(Path.GetDirectoryName(exePath)!, Path.GetFileNameWithoutExtension(exePath) + ".old.exe");
            if (File.Exists(oldPath))
                File.Delete(oldPath);
        }
        catch
        {
            // Still locked; try again next start.
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"BatteryHub-Updater/{AppInfo.VersionString}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

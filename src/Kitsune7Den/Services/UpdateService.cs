using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Kitsune7Den.Services;

/// <summary>
/// Checks GitHub Releases for newer versions and self-updates the exe.
///
/// Self-replacement trick on Windows:
///   1. Download new exe to temp path
///   2. Rename current exe to *.old (allowed even while running)
///   3. Copy new exe to current exe path
///   4. Relaunch the new exe
///   5. On next startup the new exe deletes the *.old file
/// </summary>
public class UpdateService
{
    // Repo slug — update if the repo moves
    public const string RepoOwner = "Kitsune-Den";
    public const string RepoName = "Kitsune7Den";
    public const string ExeName = "Kitsune7Den.exe";

    private static readonly string ReleasesApiUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public event Action<string>? StatusChanged;
    public event Action<double>? ProgressChanged; // 0.0 - 1.0

    public string CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>
    /// Check GitHub for the latest release. Returns the release info if newer than current, else null.
    /// </summary>
    public async Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            StatusChanged?.Invoke("Checking for updates...");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Kitsune7Den", CurrentVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await http.GetStringAsync(ReleasesApiUrl, ct);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tagName : tagName;
            var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var htmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";

            // Find the best asset. Preferred order:
            //   1. Exact Kitsune7Den.exe
            //   2. Any .exe whose name contains "Kitsune7Den"
            //   3. Any .zip whose name contains "Kitsune7Den" (we'll extract Kitsune7Den.exe from it)
            string? downloadUrl = null;
            string assetName = "";
            long assetSize = 0;
            var isZipAsset = false;

            if (root.TryGetProperty("assets", out var assets))
            {
                var assetList = assets.EnumerateArray().ToList();

                JsonElement? pick = null;
                // Priority 1: exact exe name
                pick = assetList.FirstOrDefault(a =>
                    string.Equals(a.GetProperty("name").GetString(), ExeName, StringComparison.OrdinalIgnoreCase));
                // Priority 2: any .exe with the product name
                if (pick is null || pick.Value.ValueKind == JsonValueKind.Undefined)
                {
                    pick = assetList.FirstOrDefault(a =>
                    {
                        var n = a.GetProperty("name").GetString() ?? "";
                        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                               n.Contains("Kitsune7Den", StringComparison.OrdinalIgnoreCase);
                    });
                }
                // Priority 3: any .zip with the product name
                if (pick is null || pick.Value.ValueKind == JsonValueKind.Undefined)
                {
                    pick = assetList.FirstOrDefault(a =>
                    {
                        var n = a.GetProperty("name").GetString() ?? "";
                        return n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                               n.Contains("Kitsune7Den", StringComparison.OrdinalIgnoreCase);
                    });
                }

                if (pick is { ValueKind: not JsonValueKind.Undefined } p)
                {
                    assetName = p.GetProperty("name").GetString() ?? "";
                    downloadUrl = p.GetProperty("browser_download_url").GetString();
                    assetSize = p.GetProperty("size").GetInt64();
                    isZipAsset = assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                }
            }

            var latestVersion = NormalizeVersion(tagName);
            var currentVersion = NormalizeVersion(CurrentVersion);

            if (CompareVersions(latestVersion, currentVersion) <= 0)
            {
                StatusChanged?.Invoke($"You're on the latest version ({CurrentVersion})");
                return null;
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                StatusChanged?.Invoke($"Update {tagName} available but no downloadable exe/zip asset was found on the release");
                return null;
            }

            StatusChanged?.Invoke($"Update available: {tagName}");
            return new ReleaseInfo
            {
                TagName = tagName,
                Name = name,
                Body = body,
                HtmlUrl = htmlUrl,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                AssetSize = assetSize,
                IsZip = isZipAsset
            };
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Update check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download the release exe and swap it in, then relaunch. This method doesn't return.
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        try
        {
            // Locate the current exe
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            {
                StatusChanged?.Invoke("Could not locate current exe path");
                return false;
            }

            // Temp paths
            var tempDownload = currentExe + ".download";
            var oldPath = currentExe + ".old";

            StatusChanged?.Invoke($"Downloading {release.TagName} ({release.AssetName})...");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Kitsune7Den", CurrentVersion));

            using var response = await http.GetAsync(release.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? release.AssetSize;
            using (var src = await response.Content.ReadAsStreamAsync(ct))
            using (var dst = File.Create(tempDownload))
            {
                var buffer = new byte[81920];
                long read = 0;
                int bytesRead;
                while ((bytesRead = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    read += bytesRead;
                    if (totalBytes > 0)
                        ProgressChanged?.Invoke(read / (double)totalBytes);
                }
            }

            // If it's a zip, extract Kitsune7Den.exe out of it
            string newExePath;
            if (release.IsZip)
            {
                StatusChanged?.Invoke("Extracting...");
                var extractDir = currentExe + ".extract";
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, recursive: true);
                Directory.CreateDirectory(extractDir);

                ZipFile.ExtractToDirectory(tempDownload, extractDir);
                File.Delete(tempDownload);

                // Find Kitsune7Den.exe inside the extracted tree
                var extractedExe = Directory.GetFiles(extractDir, ExeName, SearchOption.AllDirectories).FirstOrDefault();
                if (extractedExe is null)
                {
                    StatusChanged?.Invoke($"Zip did not contain {ExeName}");
                    Directory.Delete(extractDir, recursive: true);
                    return false;
                }
                newExePath = extractedExe;
            }
            else
            {
                newExePath = tempDownload;
            }

            StatusChanged?.Invoke("Download complete. Installing...");

            // Clean up any stale .old from a previous update
            if (File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch { /* may still be locked */ }
            }

            // Rename current -> .old (Windows allows renaming a running exe)
            File.Move(currentExe, oldPath);

            // Move the new exe into place
            File.Move(newExePath, currentExe);

            // Clean up the extract dir if we used one
            if (release.IsZip)
            {
                var extractDir = currentExe + ".extract";
                try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, recursive: true); }
                catch { /* best effort */ }
            }

            StatusChanged?.Invoke("Update installed. Restarting...");

            // Relaunch and exit this process
            Process.Start(new ProcessStartInfo
            {
                FileName = currentExe,
                UseShellExecute = true
            });

            // Give the new process a moment to start before we exit
            await Task.Delay(500, ct);
            System.Windows.Application.Current.Shutdown();
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Update failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Called on app startup — clean up any .old file from a previous update.
    /// </summary>
    public static void CleanupStaleOldExe()
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe)) return;
            var oldPath = currentExe + ".old";
            if (File.Exists(oldPath))
                File.Delete(oldPath);
        }
        catch { /* best effort */ }
    }

    // --- Version comparison helpers ---

    private static string NormalizeVersion(string v)
    {
        v = v.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
            v = v[1..];
        return v;
    }

    /// <summary>
    /// Compare two semver-ish versions. Returns negative if a &lt; b, 0 if equal, positive if a &gt; b.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var bParts = b.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var maxLen = Math.Max(aParts.Length, bParts.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var av = i < aParts.Length ? aParts[i] : 0;
            var bv = i < bParts.Length ? bParts[i] : 0;
            if (av != bv) return av - bv;
        }
        return 0;
    }
}

public class ReleaseInfo
{
    public string TagName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string AssetName { get; set; } = "";
    public long AssetSize { get; set; }
    public bool IsZip { get; set; }

    public string AssetSizeText => AssetSize switch
    {
        < 1024 * 1024 => $"{AssetSize / 1024.0:F0} KB",
        _ => $"{AssetSize / (1024.0 * 1024):F1} MB"
    };
}

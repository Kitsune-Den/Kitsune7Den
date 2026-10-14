using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public class SteamCmdService
{
    private const int SevenDaysToDieServerAppId = 294420;
    private const string SteamCmdDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    private readonly AppSettings _settings;

    public event Action<string>? OutputReceived;
    public event Action<bool>? OperationCompleted;

    public SteamCmdService(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Known branches for 7D2D dedicated server.
    /// </summary>
    public static readonly string[] KnownBranches =
    [
        "public",
        "latest_experimental",
        "alpha21.2",
        "alpha20.7"
    ];

    /// <summary>
    /// Downloads SteamCMD to the specified directory if not already present.
    /// </summary>
    public async Task<bool> DownloadSteamCmdAsync(string installDir)
    {
        try
        {
            Directory.CreateDirectory(installDir);
            var exePath = Path.Combine(installDir, "steamcmd.exe");

            if (File.Exists(exePath))
            {
                OutputReceived?.Invoke(Loc.F("Steam_AlreadyExists", exePath));
                return true;
            }

            OutputReceived?.Invoke(Loc.T("Steam_Downloading"));

            using var httpClient = new HttpClient();
            var zipBytes = await httpClient.GetByteArrayAsync(SteamCmdDownloadUrl);
            var zipPath = Path.Combine(installDir, "steamcmd.zip");
            await File.WriteAllBytesAsync(zipPath, zipBytes);

            OutputReceived?.Invoke(Loc.T("Steam_Extracting"));
            ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
            File.Delete(zipPath);

            if (File.Exists(exePath))
            {
                OutputReceived?.Invoke(Loc.F("Steam_Installed", exePath));
                _settings.SteamCmdPath = exePath;
                _settings.Save();
                return true;
            }

            OutputReceived?.Invoke(Loc.T("Steam_ExeMissingAfterExtract"));
            return false;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(Loc.F("Steam_DownloadError", ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Installs or updates the 7D2D dedicated server using SteamCMD.
    /// </summary>
    public async Task<bool> InstallOrUpdateServerAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamCmdPath) || !File.Exists(_settings.SteamCmdPath))
        {
            OutputReceived?.Invoke(Loc.T("Steam_PathNotSet"));
            OperationCompleted?.Invoke(false);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_settings.ServerInstallDirectory))
        {
            OutputReceived?.Invoke(Loc.T("Steam_InstallDirNotSet"));
            OperationCompleted?.Invoke(false);
            return false;
        }

        Directory.CreateDirectory(_settings.ServerInstallDirectory);

        var branch = _settings.ServerBranch;
        var validate = _settings.ValidateOnUpdate ? " validate" : "";

        // Build SteamCMD arguments
        var betaArg = branch != "public" ? $" -beta {branch}" : "";
        var args = $"+force_install_dir \"{_settings.ServerInstallDirectory}\" " +
                   $"+login anonymous " +
                   $"+app_update {SevenDaysToDieServerAppId}{betaArg}{validate} " +
                   $"+quit";

        OutputReceived?.Invoke(Loc.F("Steam_Running", args));
        OutputReceived?.Invoke(Loc.F("Steam_BranchValidate", branch, _settings.ValidateOnUpdate));
        OutputReceived?.Invoke(Loc.T("Steam_FirstInstallSlow"));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _settings.SteamCmdPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_settings.SteamCmdPath)!
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) OutputReceived?.Invoke(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) OutputReceived?.Invoke($"[ERR] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            var success = process.ExitCode == 0;
            if (success)
            {
                // Auto-detect server exe path
                var exePath = Path.Combine(_settings.ServerInstallDirectory, "7DaysToDieServer.exe");
                if (File.Exists(exePath))
                {
                    _settings.ServerExePath = exePath;
                    _settings.ServerDirectory = _settings.ServerInstallDirectory;
                    OutputReceived?.Invoke(Loc.F("Steam_ServerExeFound", exePath));
                }

                _settings.Save();
                OutputReceived?.Invoke(Loc.T("Steam_UpdateSucceeded"));
            }
            else
            {
                OutputReceived?.Invoke(Loc.F("Steam_ExitCode", process.ExitCode));
            }

            OperationCompleted?.Invoke(success);
            return success;
        }
        catch (OperationCanceledException)
        {
            OutputReceived?.Invoke(Loc.T("Steam_Cancelled"));
            OperationCompleted?.Invoke(false);
            return false;
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(Loc.F("Steam_RunError", ex.Message));
            OperationCompleted?.Invoke(false);
            return false;
        }
    }

    /// <summary>
    /// Checks if SteamCMD is available and configured.
    /// </summary>
    public bool IsSteamCmdAvailable =>
        !string.IsNullOrWhiteSpace(_settings.SteamCmdPath) && File.Exists(_settings.SteamCmdPath);
}

using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using Kitsune7Den.Localization;
using Kitsune7Den.Models;

namespace Kitsune7Den.Services;

public class ModManagerService
{
    private readonly AppSettings _settings;

    public event Action<string>? StatusChanged;

    public ModManagerService(AppSettings settings)
    {
        _settings = settings;
    }

    private string ModsPath => Path.Combine(_settings.ServerDirectory, "Mods");

    public List<ModInfo> GetInstalledMods()
    {
        if (!Directory.Exists(ModsPath))
            return [];

        var mods = new List<ModInfo>();

        foreach (var dir in Directory.GetDirectories(ModsPath))
        {
            var folderName = Path.GetFileName(dir);
            var isEnabled = !folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var modInfoPath = Path.Combine(dir, "ModInfo.xml");

            var mod = new ModInfo
            {
                FolderName = folderName,
                IsEnabled = isEnabled,
                FolderSizeBytes = GetDirectorySize(dir)
            };

            if (File.Exists(modInfoPath))
            {
                try
                {
                    var doc = XDocument.Load(modInfoPath);
                    var root = doc.Root;
                    mod.DisplayName = root?.Element("DisplayName")?.Attribute("value")?.Value
                                     ?? root?.Element("Name")?.Attribute("value")?.Value
                                     ?? folderName;
                    mod.Version = root?.Element("Version")?.Attribute("value")?.Value ?? "";
                    mod.Author = root?.Element("Author")?.Attribute("value")?.Value ?? "";
                    mod.Description = root?.Element("Description")?.Attribute("value")?.Value ?? "";
                    mod.Website = root?.Element("Website")?.Attribute("value")?.Value ?? "";
                }
                catch
                {
                    mod.DisplayName = folderName;
                }
            }
            else
            {
                mod.DisplayName = folderName;
            }

            mods.Add(mod);
        }

        return mods.OrderBy(m => m.DisplayName).ToList();
    }

    public bool ToggleMod(string folderName)
    {
        var currentPath = Path.Combine(ModsPath, folderName);
        if (!Directory.Exists(currentPath)) return false;

        string newName;
        if (folderName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            newName = folderName[..^".disabled".Length];
        else
            newName = folderName + ".disabled";

        var newPath = Path.Combine(ModsPath, newName);
        if (Directory.Exists(newPath)) return false;

        Directory.Move(currentPath, newPath);
        return true;
    }

    public bool DeleteMod(string folderName)
    {
        var path = Path.Combine(ModsPath, folderName);

        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(ModsPath)))
            return false;

        if (!Directory.Exists(path)) return false;

        Directory.Delete(path, recursive: true);
        return true;
    }

    /// <summary>
    /// Install a mod from a zip file.
    /// </summary>
    public string? InstallFromZip(string zipPath)
    {
        if (!File.Exists(zipPath) || !Directory.Exists(ModsPath))
            return null;

        var tempDir = Path.Combine(Path.GetTempPath(), "Kitsune7Den_mod_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            // Extract to temp
            ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Detect structure: single root folder with ModInfo.xml, or loose files
            var extractedDirs = Directory.GetDirectories(tempDir);
            var extractedFiles = Directory.GetFiles(tempDir);

            string sourceDir;
            string modFolderName;

            if (extractedDirs.Length == 1 && extractedFiles.Length == 0)
            {
                // Single root folder — use it directly
                sourceDir = extractedDirs[0];
                modFolderName = Path.GetFileName(sourceDir);
            }
            else if (extractedDirs.Length == 1 && File.Exists(Path.Combine(extractedDirs[0], "ModInfo.xml")))
            {
                // Single root folder with ModInfo.xml
                sourceDir = extractedDirs[0];
                modFolderName = Path.GetFileName(sourceDir);
            }
            else if (File.Exists(Path.Combine(tempDir, "ModInfo.xml")))
            {
                // Files directly in the zip root — wrap in folder named after zip
                sourceDir = tempDir;
                modFolderName = Path.GetFileNameWithoutExtension(zipPath);
            }
            else
            {
                // Can't detect structure
                StatusChanged?.Invoke(Loc.T("Mods_ErrNoModStructure"));
                return null;
            }

            // Security: prevent path traversal
            if (modFolderName.Contains(".."))
            {
                StatusChanged?.Invoke(Loc.T("Mods_ErrInvalidFolderName"));
                return null;
            }

            var destPath = Path.Combine(ModsPath, modFolderName);

            // If mod already exists, remove it first
            if (Directory.Exists(destPath))
                Directory.Delete(destPath, recursive: true);

            if (sourceDir == tempDir)
            {
                // Move the temp dir itself
                Directory.Move(tempDir, destPath);
            }
            else
            {
                // Move the inner folder
                Directory.Move(sourceDir, destPath);
                // Clean up temp
                Directory.Delete(tempDir, recursive: true);
            }

            StatusChanged?.Invoke(Loc.F("Mods_Installed", modFolderName));
            return modFolderName;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(Loc.F("Mods_InstallError", ex.Message));

            // Clean up temp on failure
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }

            return null;
        }
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}

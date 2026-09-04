using System.IO;
using System.Security;

namespace MFAWPF.Services.Characters;

internal sealed class CharacterStoragePaths
{
    private const string AssetsDirectoryName = "Pets";
    private readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private CharacterStoragePaths(string assetRoot, string configurationPath)
    {
        AssetRoot = Path.GetFullPath(assetRoot);
        ConfigurationPath = Path.GetFullPath(configurationPath);
    }

    public string AssetRoot { get; }

    public string ConfigurationPath { get; }

    public static CharacterStoragePaths CreateDefault()
    {
        var portableRoot = Path.Combine(AppContext.BaseDirectory, AssetsDirectoryName);
        var canUsePortableStorage = CanWriteTo(portableRoot);
        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rino");
        var assetRoot = canUsePortableStorage
            ? portableRoot
            : Path.Combine(localRoot, AssetsDirectoryName);

        Directory.CreateDirectory(assetRoot);
        var configurationRoot = canUsePortableStorage
            ? Path.Combine(AppContext.BaseDirectory, "config")
            : Path.Combine(localRoot, "config");
        var configurationPath = Path.Combine(configurationRoot, "character.json");
        return new CharacterStoragePaths(assetRoot, configurationPath);
    }

    internal static CharacterStoragePaths CreateForTesting(
        string assetRoot,
        string configurationPath)
    {
        Directory.CreateDirectory(assetRoot);
        return new CharacterStoragePaths(assetRoot, configurationPath);
    }

    public bool TryCreateRelativePath(string path, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        if (!IsSafeAssetPath(fullPath))
        {
            return false;
        }

        relativePath = Path.GetRelativePath(AssetRoot, fullPath);
        return relativePath is not "." && !relativePath.StartsWith("..", StringComparison.Ordinal);
    }

    public string? ResolveAssetPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(AssetRoot, relativePath));
            return IsSafeAssetPath(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private bool IsSafeAssetPath(string fullPath)
    {
        var rootWithSeparator =
            AssetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, _pathComparison) &&
            !string.Equals(fullPath, AssetRoot, _pathComparison))
        {
            return false;
        }

        var currentPath = Path.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(currentPath) &&
               !string.Equals(currentPath, AssetRoot, _pathComparison))
        {
            if (Path.Exists(currentPath) &&
                File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            currentPath = Path.GetDirectoryName(currentPath);
        }

        return true;
    }

    private static bool CanWriteTo(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".rino-write-probe-{Guid.NewGuid():N}");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}

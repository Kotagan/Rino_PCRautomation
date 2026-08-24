using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageStoragePaths
{
	public string RootDirectory { get; }

	public string DownloadsDirectory { get; }

	public string StagingDirectory { get; }

	public string PackagesDirectory { get; }

	public string StateDirectory { get; }

	public string CatalogDirectory { get; }

	public RinoPackageStoragePaths(string rootDirectory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory, "rootDirectory");
		RootDirectory = Path.GetFullPath(rootDirectory);
		string? pathRoot = Path.GetPathRoot(RootDirectory);
		if (pathRoot != null && pathRoot.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Package storage cannot use a filesystem root.", "rootDirectory");
		}
		DownloadsDirectory = Path.Combine(RootDirectory, "package-downloads");
		StagingDirectory = Path.Combine(RootDirectory, "package-staging");
		PackagesDirectory = Path.Combine(RootDirectory, "packages");
		StateDirectory = Path.Combine(RootDirectory, "package-state");
		CatalogDirectory = Path.Combine(RootDirectory, "package-catalog");
	}

	public static RinoPackageStoragePaths CreateDefault()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			throw new InvalidOperationException("The per-user local application-data directory is unavailable.");
		}
		return new RinoPackageStoragePaths(Path.Combine(folderPath, "MFAWPF", "RinoPackages"));
	}

	public string GetPackageVersionDirectory(string packageId, string version)
	{
		string path = ComputeOpaqueKey(packageId);
		string path2 = ComputeOpaqueKey(packageId + "\0" + version);
		return Path.Combine(PackagesDirectory, path, path2);
	}

	private static string ComputeOpaqueKey(string value)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageInstallationService
{
	private readonly RinoGitHubReleaseClient _releaseClient;

	private readonly RinoPackageArchiveValidator _archiveValidator;

	private readonly RinoPackageStoragePaths _storagePaths;

	private readonly RinoPackageCompatibilityPolicy _compatibilityPolicy;

	public RinoPackageInstallationService(RinoGitHubReleaseClient releaseClient, RinoPackageArchiveValidator archiveValidator, RinoPackageStoragePaths storagePaths, RinoPackageCompatibilityPolicy compatibilityPolicy)
	{
		_releaseClient = releaseClient;
		_archiveValidator = archiveValidator;
		_storagePaths = storagePaths;
		_compatibilityPolicy = compatibilityPolicy;
	}

	public async Task<RinoPackageInstallationResult> InstallAsync(ValidatedRinoPackageCatalog validatedCatalog, string packageId, string version, IProgress<RinoPackageDownloadProgress>? progress, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(validatedCatalog, "validatedCatalog");
		ArgumentException.ThrowIfNullOrWhiteSpace(packageId, "packageId");
		ArgumentException.ThrowIfNullOrWhiteSpace(version, "version");
		if (validatedCatalog.IsExpired)
		{
			throw new RinoPackageValidationException("catalog.expired", "An expired package catalog cannot authorize a new download.");
		}
		RinoCatalogPackageRecordV1 package = validatedCatalog.Catalog.Packages.SingleOrDefault((RinoCatalogPackageRecordV1 candidate) => candidate.PackageId.Equals(packageId, StringComparison.Ordinal) && candidate.Version.Equals(version, StringComparison.Ordinal)) ?? throw new RinoPackageValidationException("catalog.packageMissing", "The requested package version is not present in the signed catalog.");
		if (!validatedCatalog.PublisherKeys.TryGetValue(package.PublisherKeyId, out RinoPackageSigningPublicKey publisherKey))
		{
			throw new RinoPackageValidationException("catalog.packagePublisher", "The requested package publisher key is unavailable.");
		}
		RinoCatalogPublisherKeyV1 publisher = validatedCatalog.Catalog.PublisherKeys.Single((RinoCatalogPublisherKeyV1 key) => key.KeyId.Equals(package.PublisherKeyId, StringComparison.Ordinal));
		_compatibilityPolicy.EnsureCompatible(package.Compatibility);
		EnsureStorageDirectories();
		string text = Guid.NewGuid().ToString("N");
		string downloadPath = Path.Combine(_storagePaths.DownloadsDirectory, text + ".partial");
		string stagingPath = Path.Combine(_storagePaths.StagingDirectory, text);
		string targetPath = _storagePaths.GetPackageVersionDirectory(package.PackageId, package.Version);
		try
		{
			if (Directory.Exists(targetPath))
			{
				throw new RinoPackageValidationException("install.exists", "The requested package version is already installed.");
			}
			await using (FileStream downloadStream = new FileStream(downloadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
			{
				await _releaseClient.DownloadPackageAsync(package, downloadStream, progress, cancellationToken);
				await downloadStream.FlushAsync(cancellationToken);
			}
			ValidatedRinoPackageArchive validatedRinoPackageArchive = await _archiveValidator.ValidateAsync(downloadPath, package.Sha256, publisherKey, cancellationToken);
			ValidateCatalogBinding(validatedRinoPackageArchive, package, publisher);
			CreateAndValidateDirectory(stagingPath);
			await ExtractValidatedArchiveAsync(downloadPath, stagingPath, validatedRinoPackageArchive, cancellationToken);
			DeleteExactFileIfPresent(downloadPath, _storagePaths.DownloadsDirectory);
			CreateAndValidateDirectory(Path.GetDirectoryName(targetPath) ?? throw new InvalidOperationException("Package target parent is unavailable."));
			if (Directory.Exists(targetPath))
			{
				throw new RinoPackageValidationException("install.exists", "The requested package version was installed concurrently.");
			}
			Directory.Move(stagingPath, targetPath);
			return new RinoPackageInstallationResult(package.PackageId, package.Version, targetPath);
		}
		finally
		{
			DeleteExactFileIfPresent(downloadPath, _storagePaths.DownloadsDirectory);
			DeleteExactDirectoryIfPresent(stagingPath, _storagePaths.StagingDirectory);
		}
	}

	private void EnsureStorageDirectories()
	{
		CreateAndValidateDirectory(_storagePaths.RootDirectory);
		CreateAndValidateDirectory(_storagePaths.DownloadsDirectory);
		CreateAndValidateDirectory(_storagePaths.StagingDirectory);
		CreateAndValidateDirectory(_storagePaths.PackagesDirectory);
		CreateAndValidateDirectory(_storagePaths.StateDirectory);
		CreateAndValidateDirectory(_storagePaths.CatalogDirectory);
	}

	internal static void ValidateCatalogBinding(ValidatedRinoPackageArchive archive, RinoCatalogPackageRecordV1 package, RinoCatalogPublisherKeyV1 publisher)
	{
		RinoPackageManifestV1 manifest = archive.Manifest;
		if (!manifest.PackageId.Equals(package.PackageId, StringComparison.Ordinal) || !manifest.Version.Equals(package.Version, StringComparison.Ordinal) || !manifest.PackageType.Equals(package.PackageType, StringComparison.Ordinal) || !manifest.Signing.KeyId.Equals(package.PublisherKeyId, StringComparison.Ordinal) || !manifest.Publisher.PublisherId.Equals(publisher.PublisherId, StringComparison.Ordinal) || !manifest.Source.Owner.Equals(package.Owner, StringComparison.Ordinal) || !manifest.Source.Repository.Equals(package.Repository, StringComparison.Ordinal) || !manifest.Source.ReleaseTag.Equals(package.ReleaseTag, StringComparison.Ordinal) || !manifest.Source.AssetName.Equals(package.AssetName, StringComparison.Ordinal) || !manifest.ReleasedAt.Equals(package.ReleasedAt, StringComparison.Ordinal) || archive.ArchiveLength != package.ByteLength || !archive.ArchiveSha256.Equals(package.Sha256, StringComparison.Ordinal) || !CompatibilityMatches(manifest.Compatibility, package.Compatibility))
		{
			throw new RinoPackageValidationException("install.catalogBinding", "The package manifest does not match its signed catalog record.");
		}
	}

	private static bool CompatibilityMatches(RinoPackageCompatibilityV1 manifest, RinoPackageCompatibilityV1 catalog)
	{
		if (manifest.MinimumClientVersion.Equals(catalog.MinimumClientVersion, StringComparison.Ordinal) && string.Equals(manifest.MaximumClientVersion, catalog.MaximumClientVersion, StringComparison.Ordinal) && manifest.SidecarProtocolVersion == catalog.SidecarProtocolVersion && manifest.ProjectSchemaVersions.SequenceEqual(catalog.ProjectSchemaVersions) && manifest.OperatingSystem.Equals(catalog.OperatingSystem, StringComparison.Ordinal))
		{
			return manifest.Architectures.SequenceEqual(catalog.Architectures);
		}
		return false;
	}

	private static async Task ExtractValidatedArchiveAsync(string archivePath, string stagingPath, ValidatedRinoPackageArchive validatedArchive, CancellationToken cancellationToken)
	{
		await using FileStream archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
		if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(await ComputeStreamSha256Async(archiveStream, cancellationToken)), Convert.FromHexString(validatedArchive.ArchiveSha256)))
		{
			throw new RinoPackageValidationException("install.archiveChanged", "The downloaded package changed before extraction.");
		}
		archiveStream.Position = 0L;
		using ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
		Dictionary<string, RinoPackageFileRecordV1> fileRecords = validatedArchive.Manifest.Files.ToDictionary<RinoPackageFileRecordV1, string>((RinoPackageFileRecordV1 file) => file.Path, StringComparer.Ordinal);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string text = ResolveStagingTarget(stagingPath, entry.FullName);
			Directory.CreateDirectory(Path.GetDirectoryName(text) ?? throw new InvalidOperationException("Package entry target parent is unavailable."));
			if (entry.FullName == "package.rino.json")
			{
				await CopyEntryAsync(entry, text, entry.Length, null, cancellationToken);
				continue;
			}
			if (entry.FullName == "signature.ed25519")
			{
				await CopyEntryAsync(entry, text, 64L, null, cancellationToken);
				continue;
			}
			if (!fileRecords.TryGetValue(entry.FullName, out var value))
			{
				throw new RinoPackageValidationException("install.unlistedEntry", "The package changed to include an unlisted entry before extraction.");
			}
			await CopyEntryAsync(entry, text, value.ByteLength, value.Sha256, cancellationToken);
		}
	}

	private static string ResolveStagingTarget(string stagingPath, string archivePath)
	{
		string text = Path.GetFullPath(stagingPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string path = archivePath.Replace('/', Path.DirectorySeparatorChar);
		string fullPath = Path.GetFullPath(Path.Combine(text, path));
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new RinoPackageValidationException("install.path", "A package entry escaped the staging directory.");
		}
		return fullPath;
	}

	private static async Task CopyEntryAsync(ZipArchiveEntry entry, string targetPath, long expectedBytes, string? expectedSha256, CancellationToken cancellationToken)
	{
		await using Stream source = entry.Open();
		await using FileStream destination = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
		using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = new byte[131072];
		long totalBytes = 0L;
		while (true)
		{
			int num = await source.ReadAsync(buffer, cancellationToken);
			if (num == 0)
			{
				break;
			}
			totalBytes = checked(totalBytes + num);
			if (totalBytes > expectedBytes)
			{
				throw new RinoPackageValidationException("install.fileSize", "A package entry expanded beyond its validated size during extraction.");
			}
			hasher.AppendData(buffer, 0, num);
			await destination.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
		}
		if (totalBytes != expectedBytes)
		{
			throw new RinoPackageValidationException("install.fileSize", "A package entry ended before its validated size during extraction.");
		}
		if (expectedSha256 != null && !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant()), Convert.FromHexString(expectedSha256)))
		{
			throw new RinoPackageValidationException("install.fileHash", "A package entry digest changed during extraction.");
		}
		await destination.FlushAsync(cancellationToken);
	}

	private static async Task<string> ComputeStreamSha256Async(Stream stream, CancellationToken cancellationToken)
	{
		using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = new byte[131072];
		long totalBytes = 0L;
		while (true)
		{
			int num = await stream.ReadAsync(buffer, cancellationToken);
			if (num == 0)
			{
				break;
			}
			totalBytes = checked(totalBytes + num);
			if (totalBytes > 1073741824)
			{
				throw new RinoPackageValidationException("install.archiveSize", "The downloaded package exceeded the archive-size limit before extraction.");
			}
			hasher.AppendData(buffer, 0, num);
		}
		return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
	}

	private static void DeleteExactFileIfPresent(string path, string expectedParent)
	{
		string path2 = ValidateExactChildPath(path, expectedParent);
		if (File.Exists(path2))
		{
			File.Delete(path2);
		}
	}

	private static void DeleteExactDirectoryIfPresent(string path, string expectedParent)
	{
		string path2 = ValidateExactChildPath(path, expectedParent);
		if (Directory.Exists(path2))
		{
			bool flag = (File.GetAttributes(path2) & FileAttributes.ReparsePoint) != 0;
			Directory.Delete(path2, !flag);
		}
	}

	private static void CreateAndValidateDirectory(string path)
	{
		Directory.CreateDirectory(path);
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("install.storagePath", "Package storage cannot use a reparse-point directory.");
		}
	}

	private static string ValidateExactChildPath(string path, string expectedParent)
	{
		string text = Path.GetFullPath(expectedParent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(path);
		if (fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			string? directoryName = Path.GetDirectoryName(fullPath);
			if (directoryName != null && directoryName.Equals(text.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				return fullPath;
			}
		}
		throw new InvalidOperationException("Refusing cleanup outside the expected package directory: " + fullPath);
	}
}

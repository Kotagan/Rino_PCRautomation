using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoInstalledPackageVerifier
{
	private const string ManifestFileName = "package.rino.json";

	private const string SignatureFileName = "signature.ed25519";

	private const int SignatureBytes = 64;

	private readonly RinoPackageStoragePaths _storagePaths;

	private readonly RinoPackageManifestReader _manifestReader;

	private readonly RinoPackageCompatibilityPolicy _compatibilityPolicy;

	public RinoInstalledPackageVerifier(RinoPackageStoragePaths storagePaths, RinoPackageManifestReader manifestReader, RinoPackageCompatibilityPolicy compatibilityPolicy)
	{
		_storagePaths = storagePaths;
		_manifestReader = manifestReader;
		_compatibilityPolicy = compatibilityPolicy;
	}

	public async Task<VerifiedInstalledRinoPackage> VerifyAsync(ValidatedRinoPackageCatalog validatedCatalog, string packageId, string version, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(validatedCatalog, "validatedCatalog");
		ArgumentException.ThrowIfNullOrWhiteSpace(packageId, "packageId");
		ArgumentException.ThrowIfNullOrWhiteSpace(version, "version");
		RinoCatalogPackageRecordV1 catalogPackage = validatedCatalog.Catalog.Packages.SingleOrDefault((RinoCatalogPackageRecordV1 candidate) => candidate.PackageId.Equals(packageId, StringComparison.Ordinal) && candidate.Version.Equals(version, StringComparison.Ordinal)) ?? throw new RinoPackageValidationException("import.catalogPackageMissing", "The installed package is no longer represented by the validated catalog.");
		RinoCatalogPublisherKeyV1 publisher = validatedCatalog.Catalog.PublisherKeys.SingleOrDefault((RinoCatalogPublisherKeyV1 candidate) => candidate.KeyId.Equals(catalogPackage.PublisherKeyId, StringComparison.Ordinal)) ?? throw new RinoPackageValidationException("import.publisherMissing", "The installed package publisher is unavailable in the validated catalog.");
		if (!validatedCatalog.PublisherKeys.TryGetValue(catalogPackage.PublisherKeyId, out RinoPackageSigningPublicKey publisherKey))
		{
			throw new RinoPackageValidationException("import.publisherMissing", "The installed package publisher key is unavailable.");
		}
		_compatibilityPolicy.EnsureCompatible(catalogPackage.Compatibility);
		string installedDirectory = _storagePaths.GetPackageVersionDirectory(packageId, version);
		EnsureSafeInstalledDirectory(installedDirectory);
		string path = ResolveInstalledPath(installedDirectory, "package.rino.json");
		string signaturePath = ResolveInstalledPath(installedDirectory, "signature.ed25519");
		byte[] manifestBytes = await ReadBoundedFileAsync(path, 4194304, cancellationToken);
		byte[] array = await ReadBoundedFileAsync(signaturePath, 64, cancellationToken);
		if (array.Length != 64 || !VerifyEd25519(manifestBytes, array, publisherKey.PublicKeyBytes.Span))
		{
			throw new RinoPackageValidationException("import.signature", "The installed package signature is invalid.");
		}
		RinoPackageManifestV1 manifest = _manifestReader.Read(manifestBytes);
		RinoPackageInstallationService.ValidateCatalogBinding(new ValidatedRinoPackageArchive(manifest, catalogPackage.Sha256, catalogPackage.ByteLength), catalogPackage, publisher);
		_compatibilityPolicy.EnsureCompatible(manifest.Compatibility);
		await VerifyInstalledFilesAsync(installedDirectory, manifest, cancellationToken);
		return new VerifiedInstalledRinoPackage(installedDirectory, manifest);
	}

	private static async Task VerifyInstalledFilesAsync(string installedDirectory, RinoPackageManifestV1 manifest, CancellationToken cancellationToken)
	{
		Dictionary<string, RinoPackageFileRecordV1> dictionary = manifest.Files.ToDictionary<RinoPackageFileRecordV1, string>((RinoPackageFileRecordV1 file) => file.Path, StringComparer.Ordinal);
		HashSet<string> hashSet = dictionary.Keys.Append("package.rino.json").Append("signature.ed25519").ToHashSet<string>(StringComparer.Ordinal);
		if (!EnumerateInstalledFiles(installedDirectory, hashSet).SetEquals(hashSet))
		{
			throw new RinoPackageValidationException("import.fileSet", "The installed package file set changed after installation.");
		}
		foreach (RinoPackageFileRecordV1 file2 in dictionary.Values)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string text = ResolveInstalledPath(installedDirectory, file2.Path);
			FileInfo fileInfo = new FileInfo(text);
			if (!fileInfo.Exists || fileInfo.Length != file2.ByteLength)
			{
				throw new RinoPackageValidationException("import.fileSize", "An installed package file has an unexpected size.");
			}
			if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(await ComputeFileSha256Async(text, file2.ByteLength, cancellationToken)), Convert.FromHexString(file2.Sha256)))
			{
				throw new RinoPackageValidationException("import.fileHash", "An installed package file failed integrity verification.");
			}
		}
	}

	private static HashSet<string> EnumerateInstalledFiles(string installedDirectory, IReadOnlySet<string> allowedPaths)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		Queue<string> queue = new Queue<string>();
		queue.Enqueue(installedDirectory);
		int num = 0;
		while (queue.Count > 0)
		{
			foreach (string item2 in Directory.EnumerateFileSystemEntries(queue.Dequeue()))
			{
				num++;
				if (num > 4096)
				{
					throw new RinoPackageValidationException("import.fileCount", "The installed package exceeded the file-count limit.");
				}
				FileAttributes attributes = File.GetAttributes(item2);
				if ((attributes & FileAttributes.ReparsePoint) != 0)
				{
					throw new RinoPackageValidationException("import.reparsePoint", "The installed package contains a reparse point.");
				}
				if ((attributes & FileAttributes.Directory) != 0)
				{
					string relativeDirectory = Path.GetRelativePath(installedDirectory, item2).Replace(Path.DirectorySeparatorChar, '/') + "/";
					if (!allowedPaths.Any((string path) => path.StartsWith(relativeDirectory, StringComparison.Ordinal)))
					{
						throw new RinoPackageValidationException("import.fileSet", "The installed package contains an unexpected directory.");
					}
					queue.Enqueue(item2);
				}
				else
				{
					string item = Path.GetRelativePath(installedDirectory, item2).Replace(Path.DirectorySeparatorChar, '/');
					if (!hashSet.Add(item))
					{
						throw new RinoPackageValidationException("import.fileSet", "The installed package contains duplicate paths.");
					}
				}
			}
		}
		return hashSet;
	}

	private static async Task<byte[]> ReadBoundedFileAsync(string path, int maximumBytes, CancellationToken cancellationToken)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists || fileInfo.Length <= 0 || fileInfo.Length > maximumBytes)
		{
			throw new RinoPackageValidationException("import.fileSize", "An installed package control file has an invalid size.");
		}
		byte[] bytes = new byte[checked((int)fileInfo.Length)];
		byte[] result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
			await stream.ReadExactlyAsync(bytes, cancellationToken);
			result = bytes;
		}
		return result;
	}

	private static async Task<string> ComputeFileSha256Async(string path, long expectedBytes, CancellationToken cancellationToken)
	{
		using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		string result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
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
				if (totalBytes > expectedBytes)
				{
					throw new RinoPackageValidationException("import.fileSize", "An installed package file grew during verification.");
				}
				hasher.AppendData(buffer, 0, num);
			}
			if (totalBytes != expectedBytes)
			{
				throw new RinoPackageValidationException("import.fileSize", "An installed package file changed during verification.");
			}
			result = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
		}
		return result;
	}

	private static void EnsureSafeInstalledDirectory(string installedDirectory)
	{
		if (!Directory.Exists(installedDirectory) || (File.GetAttributes(installedDirectory) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("import.packageMissing", "The installed package directory is missing or unsafe.");
		}
	}

	private static string ResolveInstalledPath(string installedDirectory, string relativePath)
	{
		string text = Path.GetFullPath(installedDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new RinoPackageValidationException("import.path", "An installed package path escaped its package directory.");
		}
		return fullPath;
	}

	private static bool VerifyEd25519(byte[] message, byte[] signature, ReadOnlySpan<byte> publicKey)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		Ed25519Signer val = new Ed25519Signer();
		val.Init(false, (ICipherParameters)new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
		val.BlockUpdate(message, 0, message.Length);
		return val.VerifySignature(signature);
	}
}

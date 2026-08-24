using System;
using System.IO;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCatalogCache
{
	private sealed class CacheMetadata
	{
		public int FormatVersion { get; init; }

		public string? CatalogSha256 { get; init; }

		public string? CatalogRootKeyId { get; init; }

		public string? EntityTag { get; init; }

		public DateTimeOffset FetchedAt { get; init; }
	}

	private const int CacheFormatVersion = 1;

	private const int MaximumMetadataBytes = 16384;

	private const int SignatureBytes = 64;

	private const string MetadataFileName = "current.json";

	private static readonly Regex Sha256Pattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100.0));

	private static readonly Regex CacheContentFilePattern = new Regex("^catalog-[0-9a-f]{64}\\.(json|sig)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100.0));

	private static readonly Regex PartialFilePattern = new Regex("^\\.[0-9a-f]{32}\\.partial$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100.0));

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		WriteIndented = false
	};

	private readonly string _catalogDirectory;

	private readonly RinoPackageCatalogReader _catalogReader;

	private readonly RinoPackageSigningPublicKey _catalogRootKey;

	public RinoPackageCatalogCache(RinoPackageStoragePaths storagePaths, RinoPackageCatalogReader catalogReader, RinoPackageSigningPublicKey catalogRootKey)
	{
		ArgumentNullException.ThrowIfNull(storagePaths, "storagePaths");
		_catalogReader = catalogReader;
		_catalogRootKey = catalogRootKey;
		_catalogDirectory = storagePaths.CatalogDirectory;
	}

	public async Task<RinoPackageCatalogCacheEntry?> TryLoadAsync(DateTimeOffset currentTime, CancellationToken cancellationToken)
	{
		string path = Path.Combine(_catalogDirectory, "current.json");
		if (!File.Exists(path))
		{
			return null;
		}
		EnsureSafeCatalogDirectory(createIfMissing: false);
		CacheMetadata metadata = DeserializeMetadata(await ReadBoundedFileAsync(path, 16384, cancellationToken));
		ValidateMetadata(metadata);
		string catalogPath = GetCatalogPath(metadata.CatalogSha256);
		string signaturePath = GetSignaturePath(metadata.CatalogSha256);
		byte[] catalogBytes = await ReadBoundedFileAsync(catalogPath, 4194304, cancellationToken);
		byte[] array = await ReadBoundedFileAsync(signaturePath, 64, cancellationToken);
		if (!Convert.ToHexString(SHA256.HashData(catalogBytes)).ToLowerInvariant().Equals(metadata.CatalogSha256, StringComparison.Ordinal))
		{
			throw new RinoPackageValidationException("catalog.cacheDigest", "The cached package catalog digest is invalid.");
		}
		return new RinoPackageCatalogCacheEntry(_catalogReader.Read(catalogBytes, array, _catalogRootKey, currentTime), metadata.EntityTag, metadata.FetchedAt);
	}

	public async Task SaveAsync(RinoPackageCatalogFetchResult fetchResult, DateTimeOffset fetchedAt, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fetchResult, "fetchResult");
		byte[] array = fetchResult.CatalogBytes.ToArray();
		byte[] signatureBytes = fetchResult.SignatureBytes.ToArray();
		_catalogReader.Read(array, signatureBytes, _catalogRootKey, fetchedAt);
		EnsureSafeCatalogDirectory(createIfMissing: true);
		string catalogSha256 = Convert.ToHexString(SHA256.HashData(array)).ToLowerInvariant();
		await WriteAtomicAsync(GetCatalogPath(catalogSha256), array, cancellationToken);
		await WriteAtomicAsync(GetSignaturePath(catalogSha256), signatureBytes, cancellationToken);
		byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new CacheMetadata
		{
			FormatVersion = 1,
			CatalogSha256 = catalogSha256,
			CatalogRootKeyId = _catalogRootKey.KeyId,
			EntityTag = fetchResult.EntityTag,
			FetchedAt = fetchedAt
		}, SerializerOptions);
		await WriteAtomicAsync(Path.Combine(_catalogDirectory, "current.json"), bytes, cancellationToken);
		PruneOldCacheFiles(catalogSha256);
	}

	private void ValidateMetadata(CacheMetadata metadata)
	{
		if (metadata.FormatVersion == 1 && metadata.CatalogSha256 != null && Sha256Pattern.IsMatch(metadata.CatalogSha256) && _catalogRootKey.KeyId.Equals(metadata.CatalogRootKeyId, StringComparison.Ordinal))
		{
			string? entityTag = metadata.EntityTag;
			if ((entityTag == null || entityTag.Length <= 512) && (metadata.EntityTag == null || EntityTagHeaderValue.TryParse(metadata.EntityTag, out EntityTagHeaderValue _)) && !(metadata.FetchedAt == default(DateTimeOffset)))
			{
				return;
			}
		}
		throw new RinoPackageValidationException("catalog.cacheMetadata", "The package catalog cache metadata is invalid.");
	}

	private static CacheMetadata DeserializeMetadata(byte[] metadataBytes)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(metadataBytes, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 8
			});
			RinoPackageManifestReader.EnsureNoDuplicateProperties(jsonDocument.RootElement);
			return jsonDocument.RootElement.Deserialize<CacheMetadata>(SerializerOptions) ?? throw new RinoPackageValidationException("catalog.cacheMetadata", "The package catalog cache metadata is empty.");
		}
		catch (RinoPackageValidationException)
		{
			throw;
		}
		catch (JsonException innerException)
		{
			throw new RinoPackageValidationException("catalog.cacheMetadata", "The package catalog cache metadata is not valid strict JSON.", innerException);
		}
	}

	private void EnsureSafeCatalogDirectory(bool createIfMissing)
	{
		if (createIfMissing)
		{
			Directory.CreateDirectory(_catalogDirectory);
		}
		if (!Directory.Exists(_catalogDirectory) || (File.GetAttributes(_catalogDirectory) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("catalog.cachePath", "The package catalog cache directory is unavailable or unsafe.");
		}
	}

	private string GetCatalogPath(string catalogSha256)
	{
		return Path.Combine(_catalogDirectory, "catalog-" + catalogSha256 + ".json");
	}

	private string GetSignaturePath(string catalogSha256)
	{
		return Path.Combine(_catalogDirectory, "catalog-" + catalogSha256 + ".sig");
	}

	private static async Task<byte[]> ReadBoundedFileAsync(string path, int maximumBytes, CancellationToken cancellationToken)
	{
		byte[] result;
		await using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
			if (stream.Length <= 0 || stream.Length > maximumBytes)
			{
				throw new RinoPackageValidationException("catalog.cacheSize", "A package catalog cache file has an invalid size.");
			}
			byte[] bytes = new byte[stream.Length];
			await stream.ReadExactlyAsync(bytes, cancellationToken);
			result = bytes;
		}
		return result;
	}

	private static async Task WriteAtomicAsync(string destinationPath, byte[] bytes, CancellationToken cancellationToken, bool overwrite = true)
	{
		string path = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("The cache destination has no parent directory.");
		string temporaryPath = Path.Combine(path, $".{Guid.NewGuid():N}.partial");
		try
		{
			await using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
			{
				await stream.WriteAsync(bytes, cancellationToken);
				await stream.FlushAsync(cancellationToken);
			}
			File.Move(temporaryPath, destinationPath, overwrite);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private void PruneOldCacheFiles(string currentCatalogSha256)
	{
		string value = "catalog-" + currentCatalogSha256 + ".json";
		string value2 = "catalog-" + currentCatalogSha256 + ".sig";
		foreach (string item in Directory.EnumerateFiles(_catalogDirectory))
		{
			string fileName = Path.GetFileName(item);
			if ((CacheContentFilePattern.IsMatch(fileName) || PartialFilePattern.IsMatch(fileName)) && !fileName.Equals(value, StringComparison.Ordinal) && !fileName.Equals(value2, StringComparison.Ordinal))
			{
				try
				{
					File.Delete(item);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageArchiveValidator
{
	internal const long MaximumArchiveBytes = 1073741824L;

	internal const long MaximumExpandedBytes = 2147483648L;

	internal const int MaximumArchiveEntries = 4096;

	private const int Ed25519PublicKeyBytes = 32;

	private const int Ed25519SignatureBytes = 64;

	private const string ManifestPath = "package.rino.json";

	private const string SignaturePath = "signature.ed25519";

	private static readonly HashSet<string> WindowsReservedNames = new HashSet<string>(new _003C_003Ez__ReadOnlyArray<string>(new string[22]
	{
		"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
		"COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
		"LPT8", "LPT9"
	}), StringComparer.OrdinalIgnoreCase);

	private readonly RinoPackageManifestReader _manifestReader;

	public RinoPackageArchiveValidator(RinoPackageManifestReader manifestReader)
	{
		_manifestReader = manifestReader;
	}

	public async Task<ValidatedRinoPackageArchive> ValidateAsync(string archivePath, string expectedArchiveSha256, RinoPackageSigningPublicKey publisherKey, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(archivePath, "archivePath");
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedArchiveSha256, "expectedArchiveSha256");
		ArgumentNullException.ThrowIfNull(publisherKey, "publisherKey");
		if (expectedArchiveSha256.Length != 64 || expectedArchiveSha256.Any((char character) => !char.IsAsciiHexDigit(character) || char.IsAsciiLetterUpper(character)))
		{
			throw new RinoPackageValidationException("catalog.archiveDigest", "The signed catalog archive digest is not a lowercase SHA-256 value.");
		}
		if (publisherKey.PublicKeyBytes.Length != 32)
		{
			throw new RinoPackageValidationException("signature.publicKeyLength", "The approved publisher public key has an invalid length.");
		}
		FileInfo archiveInfo = new FileInfo(archivePath);
		if (!archiveInfo.Exists)
		{
			throw new RinoPackageValidationException("archive.missing", "The package archive does not exist.");
		}
		if ((archiveInfo.Attributes & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("archive.reparsePoint", "Package archives reached through reparse points are not accepted.");
		}
		if (archiveInfo.Length <= 0 || archiveInfo.Length > 1073741824)
		{
			throw new RinoPackageValidationException("archive.size", $"Package archive must contain between 1 and {1073741824} bytes.");
		}
		try
		{
			ValidatedRinoPackageArchive result;
			await using (FileStream archiveStream = new FileStream(archiveInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
			{
				string archiveSha256 = await ComputeArchiveSha256Async(archiveStream, cancellationToken);
				if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(archiveSha256), Convert.FromHexString(expectedArchiveSha256)))
				{
					throw new RinoPackageValidationException("archive.digest", "The package archive digest does not match the signed catalog.");
				}
				archiveStream.Position = 0L;
				using ZipArchive archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
				Dictionary<string, ZipArchiveEntry> entries = ValidateEntryTable(archive);
				ZipArchiveEntry requiredEntry = GetRequiredEntry(entries, "package.rino.json");
				ZipArchiveEntry signatureEntry = GetRequiredEntry(entries, "signature.ed25519");
				byte[] manifestBytes = await ReadBoundedEntryAsync(requiredEntry, 4194304, cancellationToken);
				RinoPackageManifestV1 manifest = _manifestReader.Read(manifestBytes);
				if (!manifest.Signing.KeyId.Equals(publisherKey.KeyId, StringComparison.Ordinal))
				{
					throw new RinoPackageValidationException("signature.keyId", "The package signing key does not match the approved publisher key.");
				}
				byte[] array = await ReadBoundedEntryAsync(signatureEntry, 64, cancellationToken);
				if (array.Length != 64 || !VerifyEd25519(manifestBytes, array, publisherKey.PublicKeyBytes.Span))
				{
					throw new RinoPackageValidationException("signature.invalid", "The package manifest signature is invalid.");
				}
				ValidateExactEntrySet(entries, manifest);
				await ValidateFileRecordsAsync(entries, manifest, cancellationToken);
				result = new ValidatedRinoPackageArchive(manifest, archiveSha256, archiveInfo.Length);
			}
			return result;
		}
		catch (RinoPackageValidationException)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex3) when (((ex3 is IOException || ex3 is InvalidDataException) ? 1 : 0) != 0)
		{
			throw new RinoPackageValidationException("archive.invalid", "The package archive is malformed or cannot be read safely.", ex3);
		}
	}

	private static Dictionary<string, ZipArchiveEntry> ValidateEntryTable(ZipArchive archive)
	{
		if (archive.Entries.Count == 0 || archive.Entries.Count > 4096)
		{
			throw new RinoPackageValidationException("archive.entryCount", $"Package archive must contain between 1 and {4096} entries.");
		}
		Dictionary<string, ZipArchiveEntry> dictionary = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
		long num = 0L;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			ValidateEntryPath(entry);
			if (!dictionary.TryAdd(entry.FullName, entry))
			{
				throw new RinoPackageValidationException("archive.duplicatePath", "Package archive contains duplicate or case-colliding entry paths.");
			}
			try
			{
				num = checked(num + entry.Length);
			}
			catch (OverflowException innerException)
			{
				throw new RinoPackageValidationException("archive.expandedSize", "Package archive expanded size is invalid.", innerException);
			}
			if (num > 2147483648u)
			{
				throw new RinoPackageValidationException("archive.expandedSize", $"Package archive exceeds the {2147483648u}-byte expanded-size limit.");
			}
		}
		return dictionary;
	}

	private static void ValidateEntryPath(ZipArchiveEntry entry)
	{
		string fullName = entry.FullName;
		if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 255 || fullName.EndsWith("/", StringComparison.Ordinal) || fullName.Contains('\\') || fullName.Contains(':') || fullName.StartsWith("/", StringComparison.Ordinal) || fullName.Any(char.IsControl))
		{
			throw new RinoPackageValidationException("archive.path", "Package archive contains an unsafe entry path.");
		}
		string[] array = fullName.Split('/');
		foreach (string text in array)
		{
			bool flag = text.Length == 0;
			if (!flag)
			{
				bool flag2 = ((text == "." || text == "..") ? true : false);
				flag = flag2;
			}
			if (flag || text.EndsWith(' ') || text.EndsWith('.'))
			{
				throw new RinoPackageValidationException("archive.path", "Package archive contains an unsafe entry path segment.");
			}
			string item = text.Split('.')[0];
			if (WindowsReservedNames.Contains(item))
			{
				throw new RinoPackageValidationException("archive.reservedPath", "Package archive contains a Windows reserved device name.");
			}
		}
		int num = (entry.ExternalAttributes >> 16) & 0xF000;
		bool num2 = num != 0 && num != 32768;
		bool flag3 = (entry.ExternalAttributes & 0x400) != 0;
		if (num2 || flag3)
		{
			throw new RinoPackageValidationException("archive.link", "Package archive links and special filesystem entries are not accepted.");
		}
	}

	private static ZipArchiveEntry GetRequiredEntry(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string path)
	{
		if (!entries.TryGetValue(path, out ZipArchiveEntry value))
		{
			throw new RinoPackageValidationException("archive.requiredEntry", "Package archive is missing required entry '" + path + "'.");
		}
		return value;
	}

	private static void ValidateExactEntrySet(IReadOnlyDictionary<string, ZipArchiveEntry> entries, RinoPackageManifestV1 manifest)
	{
		HashSet<string> expectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "package.rino.json", "signature.ed25519" };
		foreach (RinoPackageFileRecordV1 file in manifest.Files)
		{
			expectedPaths.Add(file.Path);
		}
		if (expectedPaths.Count != entries.Count || entries.Keys.Any((string path) => !expectedPaths.Contains(path)))
		{
			throw new RinoPackageValidationException("archive.unlistedEntry", "Package archive entries do not exactly match the signed manifest.");
		}
	}

	private static async Task ValidateFileRecordsAsync(IReadOnlyDictionary<string, ZipArchiveEntry> entries, RinoPackageManifestV1 manifest, CancellationToken cancellationToken)
	{
		foreach (RinoPackageFileRecordV1 file in manifest.Files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ZipArchiveEntry requiredEntry = GetRequiredEntry(entries, file.Path);
			if (requiredEntry.Length != file.ByteLength)
			{
				throw new RinoPackageValidationException("archive.fileSize", "A package file length does not match the signed manifest.");
			}
			if (!(await ComputeEntrySha256Async(requiredEntry, file.ByteLength, cancellationToken)).Equals(file.Sha256, StringComparison.Ordinal))
			{
				throw new RinoPackageValidationException("archive.fileHash", "A package file digest does not match the signed manifest.");
			}
		}
	}

	private static async Task<byte[]> ReadBoundedEntryAsync(ZipArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
	{
		if (entry.Length < 0 || entry.Length > maximumBytes)
		{
			throw new RinoPackageValidationException("archive.entrySize", "A package archive entry exceeds its permitted size.");
		}
		byte[] result;
		await using (Stream entryStream = entry.Open())
		{
			using MemoryStream output = new MemoryStream((int)entry.Length);
			byte[] buffer = new byte[65536];
			int totalBytes = 0;
			while (true)
			{
				int num = await entryStream.ReadAsync(buffer, cancellationToken);
				if (num == 0)
				{
					break;
				}
				totalBytes = checked(totalBytes + num);
				if (totalBytes > maximumBytes || totalBytes > entry.Length)
				{
					throw new RinoPackageValidationException("archive.entrySize", "A package archive entry expanded beyond its declared size.");
				}
				await output.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
			}
			if (totalBytes != entry.Length)
			{
				throw new RinoPackageValidationException("archive.entrySize", "A package archive entry ended before its declared size.");
			}
			result = output.ToArray();
		}
		return result;
	}

	private static async Task<string> ComputeEntrySha256Async(ZipArchiveEntry entry, long expectedBytes, CancellationToken cancellationToken)
	{
		string result;
		await using (Stream entryStream = entry.Open())
		{
			using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			byte[] buffer = new byte[131072];
			long totalBytes = 0L;
			while (true)
			{
				int num = await entryStream.ReadAsync(buffer, cancellationToken);
				if (num == 0)
				{
					break;
				}
				totalBytes = checked(totalBytes + num);
				if (totalBytes > expectedBytes)
				{
					throw new RinoPackageValidationException("archive.fileSize", "A package file expanded beyond its signed size.");
				}
				hasher.AppendData(buffer, 0, num);
			}
			if (totalBytes != expectedBytes)
			{
				throw new RinoPackageValidationException("archive.fileSize", "A package file ended before its signed size.");
			}
			result = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
		}
		return result;
	}

	private static async Task<string> ComputeArchiveSha256Async(Stream archiveStream, CancellationToken cancellationToken)
	{
		using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = new byte[131072];
		long totalBytes = 0L;
		while (true)
		{
			int num = await archiveStream.ReadAsync(buffer, cancellationToken);
			if (num == 0)
			{
				break;
			}
			totalBytes = checked(totalBytes + num);
			if (totalBytes > 1073741824)
			{
				throw new RinoPackageValidationException("archive.size", $"Package archive exceeds the {1073741824}-byte limit.");
			}
			hasher.AppendData(buffer, 0, num);
		}
		return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
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

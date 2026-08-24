using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCatalogReader
{
	internal const int MaximumCatalogBytes = 4194304;

	private const int Ed25519PublicKeyBytes = 32;

	private const int Ed25519SignatureBytes = 64;

	private const string SchemaResourceName = "MFAWPF.Contracts.rino-catalog-v1.schema.json";

	private static readonly Lazy<JsonSchema> CatalogSchema = new Lazy<JsonSchema>((Func<JsonSchema>)LoadSchema);

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		MaxDepth = 64,
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	public ValidatedRinoPackageCatalog Read(ReadOnlyMemory<byte> catalogBytes, ReadOnlyMemory<byte> signatureBytes, RinoPackageSigningPublicKey catalogRootKey, DateTimeOffset currentTime)
	{
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Expected O, but got Unknown
		if (catalogBytes.IsEmpty || catalogBytes.Length > 4194304)
		{
			throw new RinoPackageValidationException("catalog.size", $"Package catalog must contain between 1 and {4194304} bytes.");
		}
		if (catalogRootKey.PublicKeyBytes.Length != 32 || signatureBytes.Length != 64 || !VerifyEd25519(catalogBytes.Span, signatureBytes.Span, catalogRootKey.PublicKeyBytes.Span))
		{
			throw new RinoPackageValidationException("catalog.signature", "The package catalog signature is invalid.");
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(catalogBytes, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 64
			});
			RinoPackageManifestReader.EnsureNoDuplicateProperties(jsonDocument.RootElement);
			if (!CatalogSchema.Value.Evaluate(jsonDocument.RootElement, new EvaluationOptions
			{
				OutputFormat = (OutputFormat)0,
				RequireFormatValidation = true
			}).IsValid)
			{
				throw new RinoPackageValidationException("catalog.schema", "Package catalog does not conform to Rino Package Catalog V1.");
			}
			return ValidateSemanticRules(jsonDocument.RootElement.Deserialize<RinoPackageCatalogV1>(SerializerOptions) ?? throw new RinoPackageValidationException("catalog.deserialize", "Package catalog could not be materialized after schema validation."), catalogRootKey, currentTime);
		}
		catch (RinoPackageValidationException)
		{
			throw;
		}
		catch (JsonException innerException)
		{
			throw new RinoPackageValidationException("catalog.json", "Package catalog is not valid strict JSON.", innerException);
		}
	}

	private static JsonSchema LoadSchema()
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected O, but got Unknown
		using Stream utf8Json = Assembly.GetExecutingAssembly().GetManifestResourceStream("MFAWPF.Contracts.rino-catalog-v1.schema.json") ?? throw new InvalidOperationException("Embedded package catalog schema 'MFAWPF.Contracts.rino-catalog-v1.schema.json' was not found.");
		using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
		return JsonSchema.Build(jsonDocument.RootElement.Clone(), new BuildOptions
		{
			Dialect = Dialect.Draft202012
		}, (Uri)null);
	}

	private static ValidatedRinoPackageCatalog ValidateSemanticRules(RinoPackageCatalogV1 catalog, RinoPackageSigningPublicKey catalogRootKey, DateTimeOffset currentTime)
	{
		if (!catalog.Signing.KeyId.Equals(catalogRootKey.KeyId, StringComparison.Ordinal))
		{
			throw new RinoPackageValidationException("catalog.keyId", "The catalog signing key does not match the pinned catalog root key.");
		}
		DateTimeOffset dateTimeOffset = ParseTimestamp(catalog.GeneratedAt, "generatedAt");
		DateTimeOffset dateTimeOffset2 = ParseTimestamp(catalog.ExpiresAt, "expiresAt");
		if (dateTimeOffset2 <= dateTimeOffset)
		{
			throw new RinoPackageValidationException("catalog.validityWindow", "The catalog expiration must be later than its generation time.");
		}
		if (dateTimeOffset > currentTime.AddMinutes(5.0))
		{
			throw new RinoPackageValidationException("catalog.future", "The catalog generation time is too far in the future.");
		}
		Dictionary<string, RinoPackageSigningPublicKey> dictionary = new Dictionary<string, RinoPackageSigningPublicKey>(StringComparer.Ordinal);
		foreach (RinoCatalogPublisherKeyV1 publisherKey in catalog.PublisherKeys)
		{
			byte[] array;
			try
			{
				array = Convert.FromBase64String(publisherKey.PublicKeyBase64);
			}
			catch (FormatException innerException)
			{
				throw new RinoPackageValidationException("catalog.publisherKey", "A publisher public key is not valid Base64.", innerException);
			}
			if (array.Length != 32 || !dictionary.TryAdd(publisherKey.KeyId, new RinoPackageSigningPublicKey(publisherKey.KeyId, array)))
			{
				throw new RinoPackageValidationException("catalog.publisherKey", "Publisher key IDs must be unique and contain 32-byte Ed25519 public keys.");
			}
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (RinoCatalogPackageRecordV1 package in catalog.Packages)
		{
			if (!dictionary.ContainsKey(package.PublisherKeyId))
			{
				throw new RinoPackageValidationException("catalog.packagePublisher", "A catalog package references an unknown publisher key.");
			}
			if (!hashSet.Add(package.PackageId + "\0" + package.Version))
			{
				throw new RinoPackageValidationException("catalog.duplicatePackage", "The catalog contains a duplicate package ID and version.");
			}
			string item = $"{package.Owner}/{package.Repository}/{package.ReleaseAssetId}";
			if (!hashSet2.Add(item))
			{
				throw new RinoPackageValidationException("catalog.duplicateAsset", "The catalog assigns one GitHub release asset to more than one package version.");
			}
		}
		return new ValidatedRinoPackageCatalog(catalog, new ReadOnlyDictionary<string, RinoPackageSigningPublicKey>(dictionary), dateTimeOffset2 <= currentTime);
	}

	private static DateTimeOffset ParseTimestamp(string value, string fieldName)
	{
		if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
		{
			throw new RinoPackageValidationException("catalog.timestamp", "The catalog " + fieldName + " timestamp is invalid.");
		}
		return result;
	}

	private static bool VerifyEd25519(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected O, but got Unknown
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		Ed25519Signer val = new Ed25519Signer();
		val.Init(false, (ICipherParameters)new Ed25519PublicKeyParameters(publicKey.ToArray(), 0));
		byte[] array = message.ToArray();
		val.BlockUpdate(array, 0, array.Length);
		return val.VerifySignature(signature.ToArray());
	}
}

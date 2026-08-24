using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageManifestReader
{
	internal const int MaximumManifestBytes = 4194304;

	private const string SchemaResourceName = "MFAWPF.Contracts.rino-package-v1.schema.json";

	private static readonly Lazy<JsonSchema> ManifestSchema = new Lazy<JsonSchema>((Func<JsonSchema>)LoadSchema);

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		MaxDepth = 64,
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	public RinoPackageManifestV1 Read(ReadOnlyMemory<byte> manifestBytes)
	{
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected O, but got Unknown
		if (manifestBytes.IsEmpty || manifestBytes.Length > 4194304)
		{
			throw new RinoPackageValidationException("manifest.size", $"Package manifest must contain between 1 and {4194304} bytes.");
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(manifestBytes, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 64
			});
			EnsureNoDuplicateProperties(jsonDocument.RootElement);
			if (!ManifestSchema.Value.Evaluate(jsonDocument.RootElement, new EvaluationOptions
			{
				OutputFormat = (OutputFormat)0,
				RequireFormatValidation = true
			}).IsValid)
			{
				throw new RinoPackageValidationException("manifest.schema", "Package manifest does not conform to Rino Package Manifest V1.");
			}
			RinoPackageManifestV1? obj = jsonDocument.RootElement.Deserialize<RinoPackageManifestV1>(SerializerOptions) ?? throw new RinoPackageValidationException("manifest.deserialize", "Package manifest could not be materialized after schema validation.");
			ValidateSemanticRules(obj);
			return obj;
		}
		catch (RinoPackageValidationException)
		{
			throw;
		}
		catch (JsonException innerException)
		{
			throw new RinoPackageValidationException("manifest.json", "Package manifest is not valid strict JSON.", innerException);
		}
	}

	private static JsonSchema LoadSchema()
	{
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Expected O, but got Unknown
		using Stream utf8Json = Assembly.GetExecutingAssembly().GetManifestResourceStream("MFAWPF.Contracts.rino-package-v1.schema.json") ?? throw new InvalidOperationException("Embedded package schema 'MFAWPF.Contracts.rino-package-v1.schema.json' was not found.");
		using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
		return JsonSchema.Build(jsonDocument.RootElement.Clone(), new BuildOptions
		{
			Dialect = Dialect.Draft202012
		}, (Uri)null);
	}

	internal static void EnsureNoDuplicateProperties(JsonElement element)
	{
		EnsureNoDuplicateProperties(element, "$", 0);
	}

	private static void EnsureNoDuplicateProperties(JsonElement element, string path, int depth)
	{
		if (depth > 64)
		{
			throw new RinoPackageValidationException("manifest.depth", "Package manifest nesting exceeds the supported depth.");
		}
		if (element.ValueKind == JsonValueKind.Object)
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			{
				foreach (JsonProperty item in element.EnumerateObject())
				{
					if (!hashSet.Add(item.Name))
					{
						throw new RinoPackageValidationException("manifest.duplicateProperty", "Package manifest contains a duplicate property at " + path + ".");
					}
					EnsureNoDuplicateProperties(item.Value, path + "/" + item.Name, depth + 1);
				}
				return;
			}
		}
		if (element.ValueKind != JsonValueKind.Array)
		{
			return;
		}
		int num = 0;
		foreach (JsonElement item2 in element.EnumerateArray())
		{
			EnsureNoDuplicateProperties(item2, $"{path}/{num}", depth + 1);
			num++;
		}
	}

	private static void ValidateSemanticRules(RinoPackageManifestV1 manifest)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (RinoPackageFileRecordV1 file in manifest.Files)
		{
			if (!hashSet.Add(file.Path))
			{
				throw new RinoPackageValidationException("manifest.duplicatePath", "Package manifest contains duplicate or case-colliding file paths.");
			}
			if (file.Role == "image" && !Path.GetFileNameWithoutExtension(file.Path).Equals(file.Sha256, StringComparison.Ordinal))
			{
				throw new RinoPackageValidationException("manifest.imageIdentity", "A content-addressed image path does not match its SHA-256 digest.");
			}
		}
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> packageCapabilities = manifest.RequiredCapabilities.ToHashSet<string>(StringComparer.Ordinal);
		foreach (RinoPackageEntrypointV1 entrypoint in manifest.Payload.Entrypoints)
		{
			if (!hashSet2.Add(entrypoint.EntrypointId))
			{
				throw new RinoPackageValidationException("manifest.duplicateEntrypoint", "Package manifest contains duplicate entrypoint IDs.");
			}
			if (entrypoint.RequiredCapabilities != null && entrypoint.RequiredCapabilities.Any((string capability) => !packageCapabilities.Contains(capability)))
			{
				throw new RinoPackageValidationException("manifest.entrypointCapability", "An entrypoint requires a capability not declared by the package.");
			}
		}
		string defaultProjectEntrypointId = manifest.Payload.DefaultProjectEntrypointId;
		if (defaultProjectEntrypointId != null && !hashSet2.Contains(defaultProjectEntrypointId))
		{
			throw new RinoPackageValidationException("manifest.defaultEntrypoint", "The default project entrypoint is not declared by the package.");
		}
		if (manifest.License.LicenseFile != null && !manifest.Files.Any((RinoPackageFileRecordV1 file) => file.Role == "license" && file.Path.Equals(manifest.License.LicenseFile, StringComparison.Ordinal)))
		{
			throw new RinoPackageValidationException("manifest.licenseFile", "The declared license file is not present in the package file records.");
		}
		string value = "https://github.com/" + manifest.Source.Owner + "/" + manifest.Source.Repository;
		if (!manifest.Source.RepositoryUrl.TrimEnd('/').Equals(value, StringComparison.OrdinalIgnoreCase))
		{
			throw new RinoPackageValidationException("manifest.repositoryIdentity", "The source repository URL does not match the declared GitHub owner and repository.");
		}
	}
}

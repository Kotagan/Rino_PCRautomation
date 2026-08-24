using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoProjectDocumentAssembler
{
	private const int MaximumProjectManifestBytes = 4194304;

	private const int MaximumGraphBytes = 16777216;

	public async Task<JsonElement> AssembleAsync(VerifiedInstalledRinoPackage installedPackage, string entrypointId, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(installedPackage, "installedPackage");
		ArgumentException.ThrowIfNullOrWhiteSpace(entrypointId, "entrypointId");
		RinoPackageEntrypointV1 entrypoint = installedPackage.Manifest.Payload.Entrypoints.SingleOrDefault((RinoPackageEntrypointV1 candidate) => candidate.EntrypointId.Equals(entrypointId, StringComparison.Ordinal)) ?? throw new RinoPackageValidationException("preflight.entrypointMissing", "The selected package entry point is unavailable.");
		RinoPackageFileRecordV1 file2 = installedPackage.Manifest.Files.SingleOrDefault((RinoPackageFileRecordV1 file) => file.Role.Equals("projectManifest", StringComparison.Ordinal) && file.Path.Equals(installedPackage.Manifest.Payload.ProjectManifestPath, StringComparison.Ordinal)) ?? throw new RinoPackageValidationException("preflight.projectManifestMissing", "The package project manifest is unavailable.");
		JsonNode jsonNode = ParseStrictObject(await ReadAndVerifyAsync(installedPackage.InstalledDirectory, file2, 4194304, cancellationToken), "preflight.projectManifestJson");
		JsonObject projectObject = jsonNode.AsObject();
		string documentId = GetRequiredString(projectObject, "documentId");
		JsonArray jsonArray = (projectObject["graphs"] as JsonArray) ?? throw new RinoPackageValidationException("preflight.projectManifestShape", "The package project manifest does not declare graph records.");
		int count = jsonArray.Count;
		if ((count < 1 || count > 64) ? true : false)
		{
			throw new RinoPackageValidationException("preflight.projectManifestShape", "The package project manifest graph count is invalid.");
		}
		JsonArray graphDocuments = new JsonArray((JsonNodeOptions?)null);
		HashSet<string> graphIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (JsonNode item in jsonArray)
		{
			cancellationToken.ThrowIfCancellationRequested();
			JsonObject value = (item as JsonObject) ?? throw new RinoPackageValidationException("preflight.projectManifestShape", "The package project manifest contains an invalid graph record.");
			string graphId = GetRequiredString(value, "graphId");
			string requiredString = GetRequiredString(value, "fileName");
			if (!graphIds.Add(graphId) || Path.GetFileName(requiredString) != requiredString || requiredString.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
			{
				throw new RinoPackageValidationException("preflight.projectGraphIdentity", "The package project manifest contains an unsafe or duplicate graph record.");
			}
			string packagePath = "payload/graphs/" + requiredString;
			RinoPackageFileRecordV1 file3 = installedPackage.Manifest.Files.SingleOrDefault((RinoPackageFileRecordV1 file) => file.Role.Equals("graph", StringComparison.Ordinal) && file.Path.Equals(packagePath, StringComparison.Ordinal)) ?? throw new RinoPackageValidationException("preflight.graphFileMissing", "A graph declared by the project manifest is unavailable.");
			JsonObject jsonObject = ParseStrictObject(await ReadAndVerifyAsync(installedPackage.InstalledDirectory, file3, 16777216, cancellationToken), "preflight.graphJson").AsObject();
			if (!GetRequiredString(jsonObject, "documentId").Equals(documentId, StringComparison.Ordinal) || !(jsonObject["graph"] is JsonObject jsonObject2) || !GetRequiredString(jsonObject2, "graphId").Equals(graphId, StringComparison.Ordinal))
			{
				throw new RinoPackageValidationException("preflight.projectGraphIdentity", "A graph file does not match its project manifest record.");
			}
			graphDocuments.Add(jsonObject2.DeepClone());
		}
		if (!graphIds.Contains(entrypoint.GraphId))
		{
			throw new RinoPackageValidationException("preflight.entrypointGraphMissing", "The selected package entry point does not reference a declared graph.");
		}
		projectObject["graphs"] = graphDocuments;
		JsonElement jsonElement = JsonSerializer.SerializeToElement(projectObject);
		if (JsonSerializer.SerializeToUtf8Bytes(jsonElement).Length > 16777216)
		{
			throw new RinoPackageValidationException("preflight.documentTooLarge", "The assembled project document exceeds the runtime protocol limit.");
		}
		return jsonElement;
	}

	private static async Task<byte[]> ReadAndVerifyAsync(string installedDirectory, RinoPackageFileRecordV1 file, int maximumBytes, CancellationToken cancellationToken)
	{
		if (file.ByteLength <= 0 || file.ByteLength > maximumBytes)
		{
			throw new RinoPackageValidationException("preflight.fileSize", "A package project file exceeds its preflight size limit.");
		}
		string text = Path.GetFullPath(installedDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, file.Path.Replace('/', Path.DirectorySeparatorChar)));
		if (!fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new RinoPackageValidationException("preflight.path", "A package project file escaped the installed package directory.");
		}
		FileInfo fileInfo = new FileInfo(fullPath);
		if (!fileInfo.Exists || fileInfo.Length != file.ByteLength || (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("preflight.fileSize", "A package project file changed before runtime preflight.");
		}
		byte[] bytes = new byte[checked((int)file.ByteLength)];
		byte[] result;
		await using (FileStream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
		{
			await stream.ReadExactlyAsync(bytes, cancellationToken);
			if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(file.Sha256)))
			{
				throw new RinoPackageValidationException("preflight.fileHash", "A package project file changed before runtime preflight.");
			}
			result = bytes;
		}
		return result;
	}

	private static JsonNode ParseStrictObject(byte[] bytes, string errorCode)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(bytes, new JsonDocumentOptions
			{
				AllowTrailingCommas = false,
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 64
			});
			RinoPackageManifestReader.EnsureNoDuplicateProperties(jsonDocument.RootElement);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				throw new RinoPackageValidationException(errorCode, "A package project document must be a JSON object.");
			}
			return JsonNode.Parse(jsonDocument.RootElement.GetRawText(), null) ?? throw new RinoPackageValidationException(errorCode, "A package project document could not be materialized.");
		}
		catch (RinoPackageValidationException)
		{
			throw;
		}
		catch (JsonException innerException)
		{
			throw new RinoPackageValidationException(errorCode, "A package project document is not valid strict JSON.", innerException);
		}
	}

	private static string GetRequiredString(JsonObject value, string propertyName)
	{
		if (value[propertyName] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value2) && !string.IsNullOrWhiteSpace(value2))
		{
			return value2;
		}
		throw new RinoPackageValidationException("preflight.projectShape", "A package project document is missing a required string field.");
	}
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageImportStateStore
{
	internal const int MaximumStateBytes = 4194304;

	internal const string MyScriptsConfigurationId = "00000000-0000-4000-8000-000000000001";

	private const int MaximumProjectImports = 256;

	private const int MaximumFeatureImports = 2048;

	private const string StateFileName = "installed-packages-v1.json";

	private const string LockFileName = "installed-packages-v1.lock";

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = false,
		ReadCommentHandling = JsonCommentHandling.Disallow,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		WriteIndented = true
	};

	private readonly RinoPackageStoragePaths _storagePaths;

	private readonly SemaphoreSlim _processLock = new SemaphoreSlim(1, 1);

	public RinoPackageImportStateStore(RinoPackageStoragePaths storagePaths)
	{
		_storagePaths = storagePaths;
	}

	public async Task<T> MutateAsync<T>(Func<RinoPackageImportStateV1, (RinoPackageImportStateV1 State, T Result, bool Changed)> mutation, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(mutation, "mutation");
		await _processLock.WaitAsync(cancellationToken);
		try
		{
			EnsureStateDirectory();
			string lockPath = Path.Combine(_storagePaths.StateDirectory, "installed-packages-v1.lock");
			T item;
			await using (OpenExclusiveLock(lockPath))
			{
				(RinoPackageImportStateV1 State, T Result, bool Changed) mutationResult = mutation(await ReadCoreAsync(cancellationToken));
				Validate(mutationResult.State);
				if (mutationResult.Changed)
				{
					await WriteCoreAsync(mutationResult.State, cancellationToken);
				}
				item = mutationResult.Result;
			}
			return item;
		}
		finally
		{
			_processLock.Release();
		}
	}

	public async Task<RinoPackageImportStateV1> ReadAsync(CancellationToken cancellationToken)
	{
		await _processLock.WaitAsync(cancellationToken);
		try
		{
			EnsureStateDirectory();
			return await ReadCoreAsync(cancellationToken);
		}
		finally
		{
			_processLock.Release();
		}
	}

	private async Task<RinoPackageImportStateV1> ReadCoreAsync(CancellationToken cancellationToken)
	{
		string text = Path.Combine(_storagePaths.StateDirectory, "installed-packages-v1.json");
		if (!File.Exists(text))
		{
			return new RinoPackageImportStateV1();
		}
		FileInfo fileInfo = new FileInfo(text);
		long length = fileInfo.Length;
		if ((length <= 0 || length > 4194304) ? true : false)
		{
			throw new RinoPackageValidationException("import.stateSize", "The local package import state has an invalid size.");
		}
		try
		{
			byte[] bytes = new byte[checked((int)fileInfo.Length)];
			RinoPackageImportStateV1 result;
			await using (FileStream stream = new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
			{
				await stream.ReadExactlyAsync(bytes, cancellationToken);
				using JsonDocument jsonDocument = JsonDocument.Parse(bytes, new JsonDocumentOptions
				{
					AllowTrailingCommas = false,
					CommentHandling = JsonCommentHandling.Disallow,
					MaxDepth = 64
				});
				RinoPackageManifestReader.EnsureNoDuplicateProperties(jsonDocument.RootElement);
				RinoPackageImportStateV1 rinoPackageImportStateV = jsonDocument.RootElement.Deserialize<RinoPackageImportStateV1>(SerializerOptions) ?? throw new RinoPackageValidationException("import.stateJson", "The local package import state is empty.");
				Validate(rinoPackageImportStateV);
				result = rinoPackageImportStateV;
			}
			return result;
		}
		catch (RinoPackageValidationException)
		{
			throw;
		}
		catch (JsonException innerException)
		{
			throw new RinoPackageValidationException("import.stateJson", "The local package import state is not valid strict JSON.", innerException);
		}
	}

	private async Task WriteCoreAsync(RinoPackageImportStateV1 state, CancellationToken cancellationToken)
	{
		byte[] array = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
		if (array.Length > 4194304)
		{
			throw new RinoPackageValidationException("import.stateSize", "The local package import state exceeded its size limit.");
		}
		string statePath = Path.Combine(_storagePaths.StateDirectory, "installed-packages-v1.json");
		string temporaryPath = Path.Combine(_storagePaths.StateDirectory, $"{"installed-packages-v1.json"}.{Guid.NewGuid():N}.partial");
		try
		{
			await using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough | FileOptions.Asynchronous))
			{
				await stream.WriteAsync(array, cancellationToken);
				await stream.FlushAsync(cancellationToken);
				stream.Flush(flushToDisk: true);
			}
			File.Move(temporaryPath, statePath, overwrite: true);
		}
		finally
		{
			DeleteExactTemporaryFile(temporaryPath);
		}
	}

	private void EnsureStateDirectory()
	{
		Directory.CreateDirectory(_storagePaths.RootDirectory);
		Directory.CreateDirectory(_storagePaths.StateDirectory);
		if ((File.GetAttributes(_storagePaths.RootDirectory) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(_storagePaths.StateDirectory) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("import.storagePath", "Package import state cannot use a reparse-point directory.");
		}
	}

	private static FileStream OpenExclusiveLock(string lockPath)
	{
		try
		{
			return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
		}
		catch (IOException innerException)
		{
			throw new RinoPackageValidationException("import.busy", "Another package import operation is already updating local state.", innerException);
		}
	}

	private void DeleteExactTemporaryFile(string path)
	{
		string text = Path.GetFullPath(_storagePaths.StateDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(path);
		if (fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			string? directoryName = Path.GetDirectoryName(fullPath);
			if (directoryName != null && directoryName.Equals(text.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				if (File.Exists(fullPath))
				{
					File.Delete(fullPath);
				}
				return;
			}
		}
		throw new InvalidOperationException("Refusing to clean up outside package state storage.");
	}

	private static void Validate(RinoPackageImportStateV1 state)
	{
		if (state.SchemaVersion != 1 || state.ProjectImports.Count > 256 || state.FeatureImports.Count > 2048)
		{
			throw new RinoPackageValidationException("import.stateSchema", "The local package import state uses unsupported bounds or schema version.");
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> hashSet2 = new HashSet<string>(StringComparer.Ordinal);
		foreach (RinoProjectImportBindingV1 projectImport in state.ProjectImports)
		{
			ValidateBindingIdentity(projectImport.ConfigurationId, projectImport.PackageId, projectImport.PackageVersion, projectImport.EntrypointId, projectImport.ImportedAt, projectImport.ActivationState);
			if (string.IsNullOrWhiteSpace(projectImport.ConfigurationName) || projectImport.ConfigurationName.Length > 128 || !hashSet.Add(projectImport.ConfigurationId) || !hashSet2.Add($"{projectImport.PackageId}\0{projectImport.PackageVersion}\0{projectImport.EntrypointId}"))
			{
				throw new RinoPackageValidationException("import.stateSchema", "The local project import state contains duplicate or invalid identities.");
			}
		}
		HashSet<string> hashSet3 = new HashSet<string>(StringComparer.Ordinal);
		foreach (RinoFeatureImportBindingV1 featureImport in state.FeatureImports)
		{
			ValidateBindingIdentity(featureImport.ConfigurationId, featureImport.PackageId, featureImport.PackageVersion, featureImport.EntrypointId, featureImport.ImportedAt, featureImport.ActivationState);
			if (!featureImport.ConfigurationId.Equals("00000000-0000-4000-8000-000000000001", StringComparison.Ordinal) || !hashSet3.Add($"{featureImport.PackageId}\0{featureImport.PackageVersion}\0{featureImport.EntrypointId}"))
			{
				throw new RinoPackageValidationException("import.stateSchema", "The local feature import state contains duplicate or invalid identities.");
			}
		}
	}

	private static void ValidateBindingIdentity(string configurationId, string packageId, string packageVersion, string entrypointId, string importedAt, string activationState)
	{
		if (!Guid.TryParseExact(configurationId, "D", out var _) || string.IsNullOrWhiteSpace(packageId) || packageId.Length > 128 || string.IsNullOrWhiteSpace(packageVersion) || packageVersion.Length > 64 || string.IsNullOrWhiteSpace(entrypointId) || entrypointId.Length > 128 || !DateTimeOffset.TryParse(importedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var _) || !activationState.Equals("pendingSidecar", StringComparison.Ordinal))
		{
			throw new RinoPackageValidationException("import.stateSchema", "The local package import state contains an invalid binding.");
		}
	}
}

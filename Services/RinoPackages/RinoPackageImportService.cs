using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MFAWPF.Configuration;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageImportService
{
	private const int MaximumConfigurationNameLength = 80;

	private static readonly HashSet<string> WindowsReservedNames = new HashSet<string>(new _003C_003Ez__ReadOnlyArray<string>(new string[22]
	{
		"CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6",
		"COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7",
		"LPT8", "LPT9"
	}), StringComparer.OrdinalIgnoreCase);

	private readonly RinoInstalledPackageVerifier _installedPackageVerifier;

	private readonly RinoPackageImportStateStore _stateStore;

	private readonly string _configurationDirectory;

	public RinoPackageImportService(RinoInstalledPackageVerifier installedPackageVerifier, RinoPackageImportStateStore stateStore)
		: this(installedPackageVerifier, stateStore, Path.Combine(AppContext.BaseDirectory, "config"))
	{
	}

	internal RinoPackageImportService(RinoInstalledPackageVerifier installedPackageVerifier, RinoPackageImportStateStore stateStore, string configurationDirectory)
	{
		_installedPackageVerifier = installedPackageVerifier;
		_stateStore = stateStore;
		_configurationDirectory = Path.GetFullPath(configurationDirectory);
	}

	public async Task<RinoPackageImportDescriptor> GetDescriptorAsync(ValidatedRinoPackageCatalog validatedCatalog, string packageId, string version, CancellationToken cancellationToken)
	{
		RinoPackageManifestV1 manifest = (await _installedPackageVerifier.VerifyAsync(validatedCatalog, packageId, version, cancellationToken)).Manifest;
		return new RinoPackageImportDescriptor(manifest.PackageId, manifest.Version, manifest.PackageType, manifest.Payload.DefaultProjectEntrypointId, manifest.Payload.SuggestedConfigurationName, manifest.Payload.Entrypoints);
	}

	public async Task<RinoProjectImportResult> ImportProjectAsync(ValidatedRinoPackageCatalog validatedCatalog, string packageId, string version, string suggestedConfigurationName, CancellationToken cancellationToken)
	{
		RinoPackageImportDescriptor descriptor = await GetDescriptorAsync(validatedCatalog, packageId, version, cancellationToken);
		string packageType = descriptor.PackageType;
		bool flag = ((packageType == "project" || packageType == "hybrid") ? true : false);
		if (!flag || string.IsNullOrWhiteSpace(descriptor.DefaultProjectEntrypointId))
		{
			throw new RinoPackageValidationException("import.projectUnsupported", "This package does not declare a complete-project import.");
		}
		string createdConfigurationPath = null;
		try
		{
			return await _stateStore.MutateAsync(delegate(RinoPackageImportStateV1 state)
			{
				RinoProjectImportBindingV1 rinoProjectImportBindingV = state.ProjectImports.SingleOrDefault((RinoProjectImportBindingV1 binding) => binding.PackageId.Equals(packageId, StringComparison.Ordinal) && binding.PackageVersion.Equals(version, StringComparison.Ordinal) && binding.EntrypointId.Equals(descriptor.DefaultProjectEntrypointId, StringComparison.Ordinal));
				if (rinoProjectImportBindingV != null)
				{
					EnsureExistingConfiguration(rinoProjectImportBindingV);
					return (State: state, Result: new RinoProjectImportResult(rinoProjectImportBindingV, AlreadyImported: true), Changed: false);
				}
				string configurationId = Guid.NewGuid().ToString("D");
				string configurationName = CreateConfigurationFile(suggestedConfigurationName, configurationId, out createdConfigurationPath);
				RinoProjectImportBindingV1 rinoProjectImportBindingV2 = new RinoProjectImportBindingV1
				{
					ConfigurationId = configurationId,
					ConfigurationName = configurationName,
					PackageId = packageId,
					PackageVersion = version,
					EntrypointId = descriptor.DefaultProjectEntrypointId,
					ImportedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
				};
				state.ProjectImports.Add(rinoProjectImportBindingV2);
				return (State: state, Result: new RinoProjectImportResult(rinoProjectImportBindingV2, AlreadyImported: false), Changed: true);
			}, cancellationToken);
		}
		catch
		{
			if (createdConfigurationPath != null)
			{
				DeleteExactConfigurationFile(createdConfigurationPath);
			}
			throw;
		}
	}

	public async Task<RinoFeatureImportResult> ImportFeatureAsync(ValidatedRinoPackageCatalog validatedCatalog, string packageId, string version, string entrypointId, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entrypointId, "entrypointId");
		RinoPackageImportDescriptor rinoPackageImportDescriptor = await GetDescriptorAsync(validatedCatalog, packageId, version, cancellationToken);
		string packageType = rinoPackageImportDescriptor.PackageType;
		bool flag = ((packageType == "feature" || packageType == "hybrid") ? true : false);
		if (!flag || rinoPackageImportDescriptor.Entrypoints.All((RinoPackageEntrypointV1 entrypoint) => !entrypoint.EntrypointId.Equals(entrypointId, StringComparison.Ordinal)))
		{
			throw new RinoPackageValidationException("import.featureUnsupported", "This package does not declare the selected feature entry point.");
		}
		return await _stateStore.MutateAsync(delegate(RinoPackageImportStateV1 state)
		{
			RinoFeatureImportBindingV1 rinoFeatureImportBindingV = state.FeatureImports.SingleOrDefault((RinoFeatureImportBindingV1 binding) => binding.PackageId.Equals(packageId, StringComparison.Ordinal) && binding.PackageVersion.Equals(version, StringComparison.Ordinal) && binding.EntrypointId.Equals(entrypointId, StringComparison.Ordinal));
			if (rinoFeatureImportBindingV != null)
			{
				return (State: state, Result: new RinoFeatureImportResult(rinoFeatureImportBindingV, AlreadyImported: true), Changed: false);
			}
			RinoFeatureImportBindingV1 rinoFeatureImportBindingV2 = new RinoFeatureImportBindingV1
			{
				ConfigurationId = "00000000-0000-4000-8000-000000000001",
				PackageId = packageId,
				PackageVersion = version,
				EntrypointId = entrypointId,
				ImportedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
			};
			state.FeatureImports.Add(rinoFeatureImportBindingV2);
			return (State: state, Result: new RinoFeatureImportResult(rinoFeatureImportBindingV2, AlreadyImported: false), Changed: true);
		}, cancellationToken);
	}

	public Task<RinoPackageImportStateV1> ReadStateAsync(CancellationToken cancellationToken)
	{
		return _stateStore.ReadAsync(cancellationToken);
	}

	private string CreateConfigurationFile(string suggestedConfigurationName, string configurationId, out string createdPath)
	{
		EnsureConfigurationDirectory();
		string text = SanitizeConfigurationName(suggestedConfigurationName);
		for (int i = 1; i <= 999; i++)
		{
			string text2 = ((i == 1) ? string.Empty : $" ({i})");
			int num = 80 - text2.Length;
			string text3 = ((text.Length > num) ? text.Substring(0, num).TrimEnd() : text) + text2;
			string text4 = ResolveConfigurationPath(text3);
			try
			{
				byte[] array = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["rinoPackageConfigurationId"] = configurationId });
				using FileStream fileStream = new FileStream(text4, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
				fileStream.Write(array);
				fileStream.Flush(flushToDisk: true);
				createdPath = text4;
				return text3;
			}
			catch (IOException) when (File.Exists(text4))
			{
			}
		}
		throw new RinoPackageValidationException("import.configurationCollision", "A unique configuration name could not be allocated.");
	}

	private void EnsureExistingConfiguration(RinoProjectImportBindingV1 binding)
	{
		if (!File.Exists(ResolveConfigurationPath(binding.ConfigurationName)))
		{
			throw new RinoPackageValidationException("import.configurationMissing", "The configuration belonging to this package import is missing.");
		}
	}

	private void EnsureConfigurationDirectory()
	{
		Directory.CreateDirectory(_configurationDirectory);
		if ((File.GetAttributes(_configurationDirectory) & FileAttributes.ReparsePoint) != 0)
		{
			throw new RinoPackageValidationException("import.configurationPath", "Package configurations cannot use a reparse-point directory.");
		}
	}

	private string ResolveConfigurationPath(string configurationName)
	{
		string text = _configurationDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(Path.Combine(text, configurationName + ".json"));
		if (fullPath.StartsWith(text, StringComparison.OrdinalIgnoreCase))
		{
			string? directoryName = Path.GetDirectoryName(fullPath);
			if (directoryName != null && directoryName.Equals(text.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
			{
				return fullPath;
			}
		}
		throw new RinoPackageValidationException("import.configurationPath", "A package configuration name escaped the configuration directory.");
	}

	private void DeleteExactConfigurationFile(string path)
	{
		string text = ResolveConfigurationPath(Path.GetFileNameWithoutExtension(path));
		if (!text.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Refusing to clean up an unexpected configuration path.");
		}
		if (File.Exists(text))
		{
			File.Delete(text);
		}
	}

	private static string SanitizeConfigurationName(string suggestedName)
	{
		string text = (suggestedName ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
		HashSet<char> hashSet = Path.GetInvalidFileNameChars().ToHashSet();
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		bool flag = false;
		string text2 = text;
		foreach (char c in text2)
		{
			bool flag2 = hashSet.Contains(c) || char.IsControl(c);
			if (!flag2)
			{
				bool flag3 = ((c == '/' || c == '\\') ? true : false);
				flag2 = flag3;
			}
			if (flag2 || char.IsWhiteSpace(c))
			{
				if (!flag && stringBuilder.Length > 0)
				{
					stringBuilder.Append(' ');
				}
				flag = true;
			}
			else
			{
				stringBuilder.Append(c);
				flag = false;
			}
		}
		string text3 = stringBuilder.ToString().Trim(' ', '.');
		if (text3.Length == 0 || WindowsReservedNames.Contains(text3) || text3.Equals("Default", StringComparison.OrdinalIgnoreCase) || text3.Equals("config", StringComparison.OrdinalIgnoreCase) || ConfigurationHelper.IsProtectedConfigurationName(text3))
		{
			text3 = "Rino Project";
		}
		if (text3.Length > 80)
		{
			return text3.Substring(0, 80).TrimEnd(' ', '.');
		}
		return text3;
	}
}

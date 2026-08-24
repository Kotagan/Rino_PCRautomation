using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackagePreflightService
{
	private readonly RinoInstalledPackageVerifier _installedPackageVerifier;

	private readonly RinoProjectDocumentAssembler _documentAssembler;

	private readonly RinoSidecarClient _sidecarClient;

	public bool IsBundledRuntimePresent => _sidecarClient.IsBundledRuntimePresent;

	public RinoPackagePreflightService(RinoInstalledPackageVerifier installedPackageVerifier, RinoProjectDocumentAssembler documentAssembler, RinoSidecarClient sidecarClient)
	{
		_installedPackageVerifier = installedPackageVerifier;
		_documentAssembler = documentAssembler;
		_sidecarClient = sidecarClient;
	}

	public async Task<RinoPackagePreflightResult> PreflightAsync(ValidatedRinoPackageCatalog validatedCatalog, string packageId, string version, string entrypointId, CancellationToken cancellationToken)
	{
		VerifiedInstalledRinoPackage installedPackage = await _installedPackageVerifier.VerifyAsync(validatedCatalog, packageId, version, cancellationToken);
		JsonElement document = await _documentAssembler.AssembleAsync(installedPackage, entrypointId, cancellationToken);
		return new RinoPackagePreflightResult(packageId, version, entrypointId, (await _sidecarClient.ValidateGraphAsync(document, cancellationToken)).Executable);
	}
}

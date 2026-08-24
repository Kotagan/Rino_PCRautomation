using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackagePayloadV1
{
	public required string ProjectManifestPath { get; init; }

	public string? DefaultProjectEntrypointId { get; init; }

	public RinoPackageLocalizedTextV1? SuggestedConfigurationName { get; init; }

	public required IReadOnlyList<RinoPackageEntrypointV1> Entrypoints { get; init; }
}

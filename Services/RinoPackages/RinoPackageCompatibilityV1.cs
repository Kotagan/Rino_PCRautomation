using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCompatibilityV1
{
	public required string MinimumClientVersion { get; init; }

	public string? MaximumClientVersion { get; init; }

	public int SidecarProtocolVersion { get; init; }

	public required IReadOnlyList<int> ProjectSchemaVersions { get; init; }

	public required string OperatingSystem { get; init; }

	public required IReadOnlyList<string> Architectures { get; init; }
}

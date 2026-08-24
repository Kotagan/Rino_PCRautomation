using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageManifestV1
{
	public int SchemaVersion { get; init; }

	public required string PackageId { get; init; }

	public required string Version { get; init; }

	public required string PackageType { get; init; }

	public required RinoPackageMetadataV1 Metadata { get; init; }

	public required RinoPackagePublisherV1 Publisher { get; init; }

	public required RinoPackageLicenseV1 License { get; init; }

	public required RinoPackageSourceV1 Source { get; init; }

	public required string ReleasedAt { get; init; }

	public required RinoPackageCompatibilityV1 Compatibility { get; init; }

	public required IReadOnlyList<string> RequiredCapabilities { get; init; }

	public required RinoPackagePayloadV1 Payload { get; init; }

	public required IReadOnlyList<RinoPackageFileRecordV1> Files { get; init; }

	public required RinoPackageSigningV1 Signing { get; init; }
}

using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoCatalogPackageRecordV1
{
	public required string PackageId { get; init; }

	public required string Version { get; init; }

	public required string PackageType { get; init; }

	public required RinoPackageLocalizedTextV1 DisplayName { get; init; }

	public required RinoPackageLocalizedTextV1 Summary { get; init; }

	public RinoPackageLocalizedTextV1? GameName { get; init; }

	public IReadOnlyList<string>? Tags { get; init; }

	public RinoPackageLocalizedTextV1? ReleaseNotes { get; init; }

	public required string PublisherKeyId { get; init; }

	public required string Owner { get; init; }

	public required string Repository { get; init; }

	public required string ReleaseTag { get; init; }

	public long ReleaseAssetId { get; init; }

	public required string AssetName { get; init; }

	public long ByteLength { get; init; }

	public required string Sha256 { get; init; }

	public required string ReleasedAt { get; init; }

	public required RinoPackageCompatibilityV1 Compatibility { get; init; }
}

using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCatalogV1
{
	public int SchemaVersion { get; init; }

	public required string GeneratedAt { get; init; }

	public required string ExpiresAt { get; init; }

	public required IReadOnlyList<RinoCatalogPublisherKeyV1> PublisherKeys { get; init; }

	public required IReadOnlyList<RinoCatalogPackageRecordV1> Packages { get; init; }

	public required RinoCatalogSigningV1 Signing { get; init; }
}

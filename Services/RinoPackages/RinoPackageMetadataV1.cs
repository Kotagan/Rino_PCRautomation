using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageMetadataV1
{
	public required RinoPackageLocalizedTextV1 DisplayName { get; init; }

	public required RinoPackageLocalizedTextV1 Summary { get; init; }

	public RinoPackageLocalizedTextV1? Description { get; init; }

	public RinoPackageGameMetadataV1? Game { get; init; }

	public IReadOnlyList<string>? Tags { get; init; }
}

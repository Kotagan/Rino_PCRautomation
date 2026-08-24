using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageEntrypointV1
{
	public required string EntrypointId { get; init; }

	public required string GraphId { get; init; }

	public required RinoPackageLocalizedTextV1 DisplayName { get; init; }

	public RinoPackageLocalizedTextV1? Description { get; init; }

	public IReadOnlyList<string>? RequiredCapabilities { get; init; }
}

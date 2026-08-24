using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageLocalizedTextV1
{
	public required string Default { get; init; }

	public IReadOnlyDictionary<string, string>? Translations { get; init; }
}

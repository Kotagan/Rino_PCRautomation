using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoPackageImportDescriptor(string PackageId, string Version, string PackageType, string? DefaultProjectEntrypointId, RinoPackageLocalizedTextV1? SuggestedConfigurationName, IReadOnlyList<RinoPackageEntrypointV1> Entrypoints);

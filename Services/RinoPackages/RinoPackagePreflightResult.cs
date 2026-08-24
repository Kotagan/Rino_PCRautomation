namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoPackagePreflightResult(string PackageId, string Version, string EntrypointId, bool Executable);

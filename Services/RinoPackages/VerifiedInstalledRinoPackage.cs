namespace MFAWPF.Services.RinoPackages;

internal sealed record VerifiedInstalledRinoPackage(string InstalledDirectory, RinoPackageManifestV1 Manifest);

namespace MFAWPF.Services.RinoPackages;

internal sealed record ValidatedRinoPackageArchive(RinoPackageManifestV1 Manifest, string ArchiveSha256, long ArchiveLength);

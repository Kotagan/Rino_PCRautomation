namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageSourceV1
{
	public required string Provider { get; init; }

	public required string Owner { get; init; }

	public required string Repository { get; init; }

	public required string ReleaseTag { get; init; }

	public required string AssetName { get; init; }

	public required string RepositoryUrl { get; init; }
}

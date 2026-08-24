namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageGameMetadataV1
{
	public required string GameId { get; init; }

	public required RinoPackageLocalizedTextV1 DisplayName { get; init; }
}

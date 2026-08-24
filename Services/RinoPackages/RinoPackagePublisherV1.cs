namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackagePublisherV1
{
	public required string PublisherId { get; init; }

	public required string DisplayName { get; init; }

	public string? Homepage { get; init; }
}

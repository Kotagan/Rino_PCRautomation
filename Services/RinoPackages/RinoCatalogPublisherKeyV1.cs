namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoCatalogPublisherKeyV1
{
	public required string PublisherId { get; init; }

	public required string PublisherName { get; init; }

	public required string KeyId { get; init; }

	public required string Algorithm { get; init; }

	public required string PublicKeyBase64 { get; init; }
}

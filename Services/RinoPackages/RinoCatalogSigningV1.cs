namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoCatalogSigningV1
{
	public required string Algorithm { get; init; }

	public required string KeyId { get; init; }

	public required string SignatureFile { get; init; }
}

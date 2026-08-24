namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageFileRecordV1
{
	public required string Path { get; init; }

	public required string Role { get; init; }

	public required string MediaType { get; init; }

	public long ByteLength { get; init; }

	public required string Sha256 { get; init; }
}

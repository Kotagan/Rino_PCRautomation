namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageLicenseV1
{
	public required string Identifier { get; init; }

	public string? LicenseFile { get; init; }
}

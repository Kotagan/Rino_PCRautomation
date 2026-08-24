namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoFeatureImportBindingV1
{
	public required string ConfigurationId { get; init; }

	public required string PackageId { get; init; }

	public required string PackageVersion { get; init; }

	public required string EntrypointId { get; init; }

	public required string ImportedAt { get; init; }

	public string ActivationState { get; init; } = "pendingSidecar";
}

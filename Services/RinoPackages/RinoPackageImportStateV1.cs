using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageImportStateV1
{
	public int SchemaVersion { get; init; } = 1;

	public List<RinoProjectImportBindingV1> ProjectImports { get; init; } = new List<RinoProjectImportBindingV1>();

	public List<RinoFeatureImportBindingV1> FeatureImports { get; init; } = new List<RinoFeatureImportBindingV1>();
}

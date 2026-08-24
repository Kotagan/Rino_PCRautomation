using System;

namespace MFAWPF.Services.RinoPackages;

internal static class RinoPackageDistributionTrust
{
	private const string CatalogOwner = "SweetSmellFox";

	private const string CatalogRepository = "MFAWPF";

	private const string CatalogRootKeyId = "catalog-root-2026-08";

	private const string CatalogRootPublicKeyBase64 = "b/mnIdLx/YgETUfoAKW6TDGKM/aeDFWiiLXiMCQoMmg=";

	public static RinoGitHubCatalogSource CreateCatalogSource()
	{
		return new RinoGitHubCatalogSource("SweetSmellFox", "MFAWPF");
	}

	public static RinoPackageSigningPublicKey CreateCatalogRootKey()
	{
		return new RinoPackageSigningPublicKey("catalog-root-2026-08", Convert.FromBase64String("b/mnIdLx/YgETUfoAKW6TDGKM/aeDFWiiLXiMCQoMmg="));
	}

	public static RinoPackageCompatibilityPolicy CreateCompatibilityPolicy()
	{
		Version version = typeof(App).Assembly.GetName().Version ?? throw new InvalidOperationException("The client assembly version is unavailable.");
		int value = Math.Max(version.Build, 0);
		return new RinoPackageCompatibilityPolicy($"{version.Major}.{version.Minor}.{value}", 1, new _003C_003Ez__ReadOnlySingleElementList<int>(1));
	}
}

using System.Collections.Generic;

namespace MFAWPF.Services.RinoPackages;

internal sealed record ValidatedRinoPackageCatalog(RinoPackageCatalogV1 Catalog, IReadOnlyDictionary<string, RinoPackageSigningPublicKey> PublisherKeys, bool IsExpired);

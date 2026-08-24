using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoPackageCatalogCacheEntry(ValidatedRinoPackageCatalog Catalog, string? EntityTag, DateTimeOffset FetchedAt);

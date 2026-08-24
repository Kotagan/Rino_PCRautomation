using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoPackageCatalogFetchResult(ValidatedRinoPackageCatalog Catalog, string? EntityTag, ReadOnlyMemory<byte> CatalogBytes, ReadOnlyMemory<byte> SignatureBytes);

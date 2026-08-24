using System;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCatalogUnavailableException : Exception
{
	public RinoPackageValidationException CacheFailure { get; }

	public RinoPackageTransportException TransportFailure { get; }

	public RinoPackageCatalogUnavailableException(RinoPackageValidationException cacheFailure, RinoPackageTransportException transportFailure)
		: base("No valid package catalog is available from the cache or network.", transportFailure)
	{
		CacheFailure = cacheFailure;
		TransportFailure = transportFailure;
	}
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageCatalogCoordinator
{
	private readonly RinoGitHubReleaseClient _releaseClient;

	private readonly RinoPackageCatalogCache _catalogCache;

	public RinoPackageCatalogCoordinator(RinoGitHubReleaseClient releaseClient, RinoPackageCatalogCache catalogCache)
	{
		_releaseClient = releaseClient;
		_catalogCache = catalogCache;
	}

	public async Task<RinoPackageCatalogRefreshResult> RefreshAsync(DateTimeOffset currentTime, CancellationToken cancellationToken)
	{
		RinoPackageCatalogCacheEntry cachedCatalog = null;
		RinoPackageValidationException cacheFailure = null;
		try
		{
			cachedCatalog = await _catalogCache.TryLoadAsync(currentTime, cancellationToken);
		}
		catch (RinoPackageValidationException ex)
		{
			cacheFailure = ex;
		}
		try
		{
			RinoPackageCatalogFetchResult fetchedCatalog = await _releaseClient.FetchCatalogAsync(cachedCatalog?.EntityTag, currentTime, cancellationToken);
			await _catalogCache.SaveAsync(fetchedCatalog, currentTime, cancellationToken);
			return new RinoPackageCatalogRefreshResult(new RinoPackageCatalogCacheEntry(fetchedCatalog.Catalog, fetchedCatalog.EntityTag, currentTime), RinoPackageCatalogRefreshSource.Network, null);
		}
		catch (RinoPackageTransportException ex2) when (ex2.Code == "catalog.notModified" && (object)cachedCatalog != null)
		{
			return new RinoPackageCatalogRefreshResult(cachedCatalog, RinoPackageCatalogRefreshSource.CacheNotModified, null);
		}
		catch (RinoPackageTransportException refreshFailure) when ((object)cachedCatalog != null)
		{
			return new RinoPackageCatalogRefreshResult(cachedCatalog, RinoPackageCatalogRefreshSource.CacheAfterRefreshFailure, refreshFailure);
		}
		catch (RinoPackageTransportException transportFailure) when (cacheFailure != null)
		{
			throw new RinoPackageCatalogUnavailableException(cacheFailure, transportFailure);
		}
	}
}

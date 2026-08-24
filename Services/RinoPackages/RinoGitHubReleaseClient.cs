using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoGitHubReleaseClient : IDisposable
{
	private sealed class GitHubRelease
	{
		[JsonPropertyName("draft")]
		public bool Draft { get; init; }

		[JsonPropertyName("prerelease")]
		public bool Prerelease { get; init; }

		[JsonPropertyName("assets")]
		public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = Array.Empty<GitHubReleaseAsset>();
	}

	private sealed class GitHubReleaseAsset
	{
		[JsonPropertyName("id")]
		public long Id { get; init; }

		[JsonPropertyName("name")]
		public string Name { get; init; } = string.Empty;

		[JsonPropertyName("state")]
		public string State { get; init; } = string.Empty;

		[JsonPropertyName("size")]
		public long Size { get; init; }
	}

	private const int MaximumRedirects = 5;

	private const int MaximumReleaseMetadataBytes = 2097152;

	private const string GitHubApiVersion = "2026-03-10";

	private const string CatalogAssetName = "catalog-v1.json";

	private const string CatalogSignatureAssetName = "catalog-v1.sig";

	private static readonly HashSet<string> ApprovedDownloadHosts = new HashSet<string>(new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com" }), StringComparer.OrdinalIgnoreCase);

	private static readonly JsonSerializerOptions GitHubJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = false
	};

	private readonly HttpClient _httpClient;

	private readonly RinoPackageCatalogReader _catalogReader;

	private readonly RinoGitHubCatalogSource _catalogSource;

	private readonly RinoPackageSigningPublicKey _catalogRootKey;

	private bool _disposed;

	public RinoGitHubReleaseClient(HttpMessageHandler handler, RinoPackageCatalogReader catalogReader, RinoGitHubCatalogSource catalogSource, RinoPackageSigningPublicKey catalogRootKey)
	{
		ArgumentNullException.ThrowIfNull(handler, "handler");
		_catalogReader = catalogReader;
		_catalogSource = catalogSource;
		_catalogRootKey = catalogRootKey;
		_httpClient = new HttpClient(handler, disposeHandler: true)
		{
			Timeout = Timeout.InfiniteTimeSpan
		};
	}

	public static HttpMessageHandler CreateDefaultHandler()
	{
		return new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate),
			ConnectTimeout = TimeSpan.FromSeconds(15.0),
			PooledConnectionLifetime = TimeSpan.FromMinutes(10.0)
		};
	}

	public async Task<RinoPackageCatalogFetchResult> FetchCatalogAsync(string? entityTag, DateTimeOffset currentTime, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		Uri uri = CreateApiUri($"repos/{_catalogSource.Owner}/{_catalogSource.Repository}/releases/latest");
		using HttpRequestMessage releaseRequest = CreateRequest(uri, "application/vnd.github+json");
		if (!string.IsNullOrWhiteSpace(entityTag) && EntityTagHeaderValue.TryParse(entityTag, out EntityTagHeaderValue parsedValue))
		{
			releaseRequest.Headers.IfNoneMatch.Add(parsedValue);
		}
		using HttpResponseMessage releaseResponse = await SendFollowingRedirectsAsync(releaseRequest, allowNotModified: true, cancellationToken);
		if (releaseResponse.StatusCode == HttpStatusCode.NotModified)
		{
			throw new RinoPackageTransportException("catalog.notModified", "The package catalog release metadata has not changed.", (DateTimeOffset?)null);
		}
		GitHubRelease release = DeserializeRelease(await ReadBoundedContentAsync(releaseResponse, 2097152, cancellationToken));
		GitHubReleaseAsset gitHubReleaseAsset = FindRequiredAsset(release, "catalog-v1.json", 4194304);
		GitHubReleaseAsset signatureAsset = FindRequiredAsset(release, "catalog-v1.sig", 64);
		byte[] catalogBytes = await DownloadAssetBytesAsync(gitHubReleaseAsset.Id, 4194304, cancellationToken);
		byte[] array = await DownloadAssetBytesAsync(signatureAsset.Id, 64, cancellationToken);
		return new RinoPackageCatalogFetchResult(_catalogReader.Read(catalogBytes, array, _catalogRootKey, currentTime), releaseResponse.Headers.ETag?.ToString(), catalogBytes, array);
	}

	public async Task DownloadPackageAsync(RinoCatalogPackageRecordV1 package, Stream destination, IProgress<RinoPackageDownloadProgress>? progress, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(package, "package");
		ArgumentNullException.ThrowIfNull(destination, "destination");
		if (!destination.CanWrite)
		{
			throw new ArgumentException("Download destination must be writable.", "destination");
		}
		Uri uri = CreateApiUri($"repos/{package.Owner}/{package.Repository}/releases/assets/{package.ReleaseAssetId}");
		using HttpRequestMessage request = CreateRequest(uri, "application/octet-stream");
		using HttpResponseMessage response = await SendFollowingRedirectsAsync(request, allowNotModified: false, cancellationToken);
		long? contentLength = response.Content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault != package.ByteLength)
			{
				throw new RinoPackageTransportException("download.length", "The release asset length does not match the signed catalog.", (DateTimeOffset?)null);
			}
		}
		await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
		using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] buffer = new byte[131072];
		Stopwatch stopwatch = Stopwatch.StartNew();
		TimeSpan lastProgressAt = TimeSpan.Zero;
		long totalBytes = 0L;
		while (true)
		{
			int num = await source.ReadAsync(buffer, cancellationToken);
			if (num == 0)
			{
				break;
			}
			totalBytes = checked(totalBytes + num);
			if (totalBytes > package.ByteLength || totalBytes > 1073741824)
			{
				throw new RinoPackageTransportException("download.length", "The release asset exceeded its signed byte length.", (DateTimeOffset?)null);
			}
			hasher.AppendData(buffer, 0, num);
			await destination.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
			if (progress != null && stopwatch.Elapsed - lastProgressAt >= TimeSpan.FromMilliseconds(100.0))
			{
				progress.Report(new RinoPackageDownloadProgress(totalBytes, package.ByteLength));
				lastProgressAt = stopwatch.Elapsed;
			}
		}
		if (totalBytes != package.ByteLength)
		{
			throw new RinoPackageTransportException("download.length", "The release asset ended before its signed byte length.", (DateTimeOffset?)null);
		}
		if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant()), Convert.FromHexString(package.Sha256)))
		{
			throw new RinoPackageTransportException("download.digest", "The release asset digest does not match the signed catalog.", (DateTimeOffset?)null);
		}
		progress?.Report(new RinoPackageDownloadProgress(totalBytes, package.ByteLength));
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_httpClient.Dispose();
		}
	}

	private async Task<byte[]> DownloadAssetBytesAsync(long assetId, int maximumBytes, CancellationToken cancellationToken)
	{
		Uri uri = CreateApiUri($"repos/{_catalogSource.Owner}/{_catalogSource.Repository}/releases/assets/{assetId}");
		using HttpRequestMessage request = CreateRequest(uri, "application/octet-stream");
		using HttpResponseMessage response = await SendFollowingRedirectsAsync(request, allowNotModified: false, cancellationToken);
		return await ReadBoundedContentAsync(response, maximumBytes, cancellationToken);
	}

	private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(HttpRequestMessage initialRequest, bool allowNotModified, CancellationToken cancellationToken)
	{
		Uri currentUri = initialRequest.RequestUri ?? throw new InvalidOperationException("HTTP request URI is missing.");
		string accept = initialRequest.Headers.Accept.Single().MediaType ?? throw new InvalidOperationException("HTTP Accept header is missing.");
		string ifNoneMatch = initialRequest.Headers.IfNoneMatch.FirstOrDefault()?.ToString();
		for (int redirectCount = 0; redirectCount <= 5; redirectCount++)
		{
			using HttpRequestMessage request = CreateRequest(currentUri, accept);
			if (redirectCount == 0 && ifNoneMatch != null && EntityTagHeaderValue.TryParse(ifNoneMatch, out EntityTagHeaderValue parsedValue))
			{
				request.Headers.IfNoneMatch.Add(parsedValue);
			}
			HttpResponseMessage httpResponseMessage;
			try
			{
				httpResponseMessage = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (HttpRequestException innerException)
			{
				throw new RinoPackageTransportException("network.request", "The GitHub request failed.", innerException, null);
			}
			if (allowNotModified && httpResponseMessage.StatusCode == HttpStatusCode.NotModified)
			{
				return httpResponseMessage;
			}
			if (!IsRedirect(httpResponseMessage.StatusCode))
			{
				if (httpResponseMessage.IsSuccessStatusCode)
				{
					return httpResponseMessage;
				}
				HttpStatusCode statusCode = httpResponseMessage.StatusCode;
				if (IsRateLimited(httpResponseMessage, out var retryAt))
				{
					httpResponseMessage.Dispose();
					throw new RinoPackageTransportException("network.rateLimit", "GitHub rate-limited the package request.", retryAt);
				}
				httpResponseMessage.Dispose();
				throw new RinoPackageTransportException("network.status", $"GitHub returned HTTP status {statusCode}.", (DateTimeOffset?)null);
			}
			if (redirectCount == 5)
			{
				httpResponseMessage.Dispose();
				throw new RinoPackageTransportException("network.redirectLimit", "The GitHub request exceeded the redirect limit.", (DateTimeOffset?)null);
			}
			Uri location = httpResponseMessage.Headers.Location;
			httpResponseMessage.Dispose();
			if ((object)location == null)
			{
				throw new RinoPackageTransportException("network.redirect", "GitHub returned a redirect without a destination.", (DateTimeOffset?)null);
			}
			currentUri = (location.IsAbsoluteUri ? location : new Uri(currentUri, location));
			ValidateDownloadUri(currentUri);
		}
		throw new InvalidOperationException("Unreachable redirect state.");
	}

	private static HttpRequestMessage CreateRequest(Uri uri, string accept)
	{
		ValidateDownloadUri(uri);
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
		httpRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
		httpRequestMessage.Headers.UserAgent.ParseAdd("MFAWPF-RinoPackages/1.0");
		httpRequestMessage.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
		return httpRequestMessage;
	}

	private static Uri CreateApiUri(string relativePath)
	{
		return new Uri("https://api.github.com/" + relativePath, UriKind.Absolute);
	}

	private static void ValidateDownloadUri(Uri uri)
	{
		if (!uri.IsAbsoluteUri || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !ApprovedDownloadHosts.Contains(uri.Host))
		{
			throw new RinoPackageTransportException("network.redirectHost", "The GitHub request resolved to an unapproved HTTPS host.", (DateTimeOffset?)null);
		}
	}

	private static bool IsRedirect(HttpStatusCode statusCode)
	{
		if ((uint)(statusCode - 301) <= 2u || (uint)(statusCode - 307) <= 1u)
		{
			return true;
		}
		return false;
	}

	private static bool IsRateLimited(HttpResponseMessage response, out DateTimeOffset? retryAt)
	{
		retryAt = null;
		bool flag = response.Headers.RetryAfter != null;
		IEnumerable<string> values;
		bool flag2 = response.Headers.TryGetValues("X-RateLimit-Remaining", out values) && values.Any((string value) => value == "0");
		if (response.StatusCode != HttpStatusCode.TooManyRequests && (response.StatusCode != HttpStatusCode.Forbidden || (!flag && !flag2)))
		{
			return false;
		}
		DateTimeOffset? dateTimeOffset = response.Headers.RetryAfter?.Date;
		if (dateTimeOffset.HasValue)
		{
			DateTimeOffset valueOrDefault = dateTimeOffset.GetValueOrDefault();
			retryAt = valueOrDefault;
			return true;
		}
		TimeSpan? timeSpan = response.Headers.RetryAfter?.Delta;
		if (timeSpan.HasValue)
		{
			TimeSpan valueOrDefault2 = timeSpan.GetValueOrDefault();
			retryAt = DateTimeOffset.UtcNow.Add(valueOrDefault2);
			return true;
		}
		if (response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string> values2) && long.TryParse(values2.FirstOrDefault(), out var result))
		{
			try
			{
				retryAt = DateTimeOffset.FromUnixTimeSeconds(result);
			}
			catch (ArgumentOutOfRangeException)
			{
				retryAt = null;
			}
		}
		return true;
	}

	private static async Task<byte[]> ReadBoundedContentAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
	{
		long? contentLength = response.Content.Headers.ContentLength;
		if (contentLength.HasValue)
		{
			long valueOrDefault = contentLength.GetValueOrDefault();
			if (valueOrDefault > maximumBytes)
			{
				throw new RinoPackageTransportException("network.responseSize", "The GitHub response exceeds its permitted size.", (DateTimeOffset?)null);
			}
		}
		byte[] result;
		await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
		{
			using MemoryStream output = new MemoryStream();
			byte[] buffer = new byte[65536];
			int totalBytes = 0;
			while (true)
			{
				int num = await source.ReadAsync(buffer, cancellationToken);
				if (num == 0)
				{
					break;
				}
				totalBytes = checked(totalBytes + num);
				if (totalBytes > maximumBytes)
				{
					throw new RinoPackageTransportException("network.responseSize", "The GitHub response exceeded its permitted size.", (DateTimeOffset?)null);
				}
				await output.WriteAsync(buffer.AsMemory(0, num), cancellationToken);
			}
			result = output.ToArray();
		}
		return result;
	}

	private static GitHubRelease DeserializeRelease(byte[] bytes)
	{
		try
		{
			GitHubRelease gitHubRelease = JsonSerializer.Deserialize<GitHubRelease>(bytes, GitHubJsonOptions) ?? throw new RinoPackageTransportException("catalog.releaseMetadata", "GitHub release metadata is empty.", (DateTimeOffset?)null);
			if (gitHubRelease.Draft || gitHubRelease.Prerelease || gitHubRelease.Assets.Count > 256)
			{
				throw new RinoPackageTransportException("catalog.releaseMetadata", "GitHub release metadata is not an approved stable release.", (DateTimeOffset?)null);
			}
			return gitHubRelease;
		}
		catch (JsonException innerException)
		{
			throw new RinoPackageTransportException("catalog.releaseMetadata", "GitHub release metadata is invalid.", innerException, null);
		}
	}

	private static GitHubReleaseAsset FindRequiredAsset(GitHubRelease release, string assetName, int maximumBytes)
	{
		List<GitHubReleaseAsset> list = release.Assets.Where((GitHubReleaseAsset asset) => asset.Name.Equals(assetName, StringComparison.Ordinal)).ToList();
		if (list.Count != 1 || list[0].Id <= 0 || !list[0].State.Equals("uploaded", StringComparison.Ordinal) || list[0].Size <= 0 || list[0].Size > maximumBytes)
		{
			throw new RinoPackageTransportException("catalog.releaseAsset", "GitHub release must contain one valid '" + assetName + "' asset.", (DateTimeOffset?)null);
		}
		return list[0];
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MFAWPF.Configuration;
using MFAWPF.Helper;

namespace MFAWPF.Services;

public static class GitHubMirrorService
{
    public const string DefaultPrimaryMirror = "https://gh-proxy.com/";
    public const string DefaultDownloadBackupMirror = "https://gh.ddlc.top/";

    private static readonly TimeSpan DefaultDirectApiTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultMirrorApiTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultDirectDownloadConnectTimeout = TimeSpan.FromSeconds(8);

    public static IReadOnlyList<string> GetCandidateMirrors(bool forApi = false)
    {
        var mirrors = new List<string>();

        var customMirror = ConfigurationHelper.GetValue(ConfigurationKeys.GitHubMirrorUrl, string.Empty)?.Trim();
        if (!string.IsNullOrWhiteSpace(customMirror))
        {
            mirrors.Add(NormalizeMirrorPrefix(customMirror));
        }

        if (!mirrors.Contains(DefaultPrimaryMirror, StringComparer.OrdinalIgnoreCase))
        {
            mirrors.Add(DefaultPrimaryMirror);
        }

        if (!forApi && !mirrors.Contains(DefaultDownloadBackupMirror, StringComparer.OrdinalIgnoreCase))
        {
            mirrors.Add(DefaultDownloadBackupMirror);
        }

        return mirrors;
    }

    public static string NormalizeMirrorPrefix(string mirror)
    {
        if (string.IsNullOrWhiteSpace(mirror))
            return string.Empty;

        var normalized = mirror.Trim();
        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }
        return normalized;
    }

    public static string ToMirrorUrl(string originalUrl, string mirrorPrefix)
    {
        if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(mirrorPrefix))
            return originalUrl;

        var normalizedMirror = NormalizeMirrorPrefix(mirrorPrefix);
        if (originalUrl.StartsWith(normalizedMirror, StringComparison.OrdinalIgnoreCase))
            return originalUrl;

        return $"{normalizedMirror}{originalUrl}";
    }

    public static async Task<string?> GetStringWithFallbackAsync(
        string originalUrl,
        TimeSpan? directTimeout = null,
        TimeSpan? mirrorTimeout = null,
        CancellationToken cancellationToken = default)
    {
        Exception? directException = null;
        var directTimeoutValue = directTimeout ?? DefaultDirectApiTimeout;

        try
        {
            using var directCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            directCts.CancelAfter(directTimeoutValue);

            using var client = CreateHttpClient(directTimeoutValue);
            using var response = await client.GetAsync(originalUrl, directCts.Token);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }

            directException = new HttpRequestException($"直连返回状态码 {(int)response.StatusCode} ({response.ReasonPhrase})");
            LoggerService.LogWarning($"直连 GitHub 失败: {directException.Message}，正在切换至加速镜像重试: {originalUrl}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or TimeoutException)
        {
            directException = ex;
            LoggerService.LogWarning($"直连 GitHub 超时或异常: {ex.Message}，正在切换至加速镜像重试: {originalUrl}");
        }

        var candidateMirrors = GetCandidateMirrors(forApi: true);
        var mirrorTimeoutValue = mirrorTimeout ?? DefaultMirrorApiTimeout;

        foreach (var mirror in candidateMirrors)
        {
            var mirrorUrl = ToMirrorUrl(originalUrl, mirror);
            try
            {
                using var mirrorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                mirrorCts.CancelAfter(mirrorTimeoutValue);

                using var client = CreateHttpClient(mirrorTimeoutValue);
                using var response = await client.GetAsync(mirrorUrl, mirrorCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    LoggerService.LogInfo($"通过国内镜像 {mirror} 请求成功: {originalUrl}");
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }

                LoggerService.LogWarning($"镜像 {mirror} 返回非成功状态码: {(int)response.StatusCode} ({response.ReasonPhrase})");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or TimeoutException)
            {
                LoggerService.LogWarning($"通过镜像 {mirror} 请求失败: {ex.Message}");
            }
        }

        if (directException != null)
        {
            throw new HttpRequestException($"直连与所有加速镜像均不可用，最后错误: {directException.Message}", directException);
        }

        return null;
    }

    public static async Task<bool> DownloadFileWithFallbackAsync(
        string originalUrl,
        string destinationFilePath,
        Action<long, long?, int, double>? progressCallback = null,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        var tempDestination = destinationFilePath;

        try
        {
            var directSuccess = await TryDownloadInternalAsync(
                originalUrl,
                tempDestination,
                DefaultDirectDownloadConnectTimeout,
                progressCallback,
                cancellationToken);

            if (directSuccess)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogWarning($"直连 GitHub 下载失败: {ex.Message}，准备使用加速镜像重试");
        }

        SafeDeleteFile(tempDestination);
        statusCallback?.Invoke("UsingMirrorFallback");

        var candidateMirrors = GetCandidateMirrors(forApi: false);
        foreach (var mirror in candidateMirrors)
        {
            var mirrorUrl = ToMirrorUrl(originalUrl, mirror);
            LoggerService.LogInfo($"尝试使用国内加速镜像下载: {mirrorUrl}");

            try
            {
                var success = await TryDownloadInternalAsync(
                    mirrorUrl,
                    tempDestination,
                    TimeSpan.FromSeconds(30),
                    progressCallback,
                    cancellationToken);

                if (success)
                {
                    LoggerService.LogInfo($"通过国内加速镜像下载完成: {mirror}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogWarning($"镜像 {mirror} 下载失败: {ex.Message}");
            }

            SafeDeleteFile(tempDestination);
        }

        return false;
    }

    private static async Task<bool> TryDownloadInternalAsync(
        string url,
        string filePath,
        TimeSpan connectTimeout,
        Action<long, long?, int, double>? progressCallback,
        CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient(Timeout.InfiniteTimeSpan);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(connectTimeout);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
        response.EnsureSuccessStatusCode();

        var startTime = DateTime.Now;
        long totalBytesRead = 0;
        long bytesPerSecond = 0;
        long? totalBytes = response.Content.Headers.ContentLength;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        var stopwatch = Stopwatch.StartNew();
        var lastSpeedUpdateTime = startTime;
        long lastTotalBytesRead = 0;

        while (true)
        {
            var bytesRead = await contentStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            var currentTime = DateTime.Now;
            var timeSinceLastUpdate = currentTime - lastSpeedUpdateTime;
            if (timeSinceLastUpdate.TotalSeconds >= 1)
            {
                bytesPerSecond = (long)((totalBytesRead - lastTotalBytesRead) / timeSinceLastUpdate.TotalSeconds);
                lastTotalBytesRead = totalBytesRead;
                lastSpeedUpdateTime = currentTime;
            }

            if (stopwatch.ElapsedMilliseconds >= 100)
            {
                progressCallback?.Invoke(
                    totalBytesRead,
                    totalBytes,
                    (int)bytesPerSecond,
                    (currentTime - startTime).TotalSeconds);
                stopwatch.Restart();
            }
        }

        progressCallback?.Invoke(
            totalBytesRead,
            totalBytes ?? totalBytesRead,
            (int)bytesPerSecond,
            (DateTime.Now - startTime).TotalSeconds);

        return true;
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient
        {
            Timeout = timeout
        };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");
        client.DefaultRequestHeaders.Accept.TryParseAdd("application/json");
        return client;
    }

    private static void SafeDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogWarning($"删除临时文件失败: {path}, {ex.Message}");
        }
    }
}

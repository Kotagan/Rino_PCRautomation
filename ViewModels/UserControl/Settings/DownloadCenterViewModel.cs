using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.Services.RinoPackages;
using MFAWPF.ViewModels.Tool;

namespace MFAWPF.ViewModels.UserControl.Settings;

internal partial class DownloadCenterViewModel : ViewModel
{
    private readonly RinoPackageCatalogCoordinator _catalogCoordinator;
    private readonly RinoPackageInstallationService _installationService;
    private readonly RinoPackageImportService _importService;
    private readonly RinoPackagePreflightService _preflightService;
    private readonly RinoPackageStoragePaths _storagePaths;

    private ValidatedRinoPackageCatalog? _validatedCatalog;
    private bool _hasLoaded;
    private string _statusResourceKey = string.Empty;
    private string[] _statusArguments = Array.Empty<string>();
    private CancellationTokenSource? _searchDebounce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(IsEmptyVisible))]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedFilterIndex;

    public ObservableCollection<RinoDownloadPackageItemViewModel> Packages { get; } = new();
    public ObservableCollection<RinoDownloadPackageItemViewModel> VisiblePackages { get; } = new();
    public IReadOnlyList<LocalizationViewModel> FilterOptions { get; }

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsNotLoading => !IsLoading;
    public bool IsListVisible => !IsLoading && VisiblePackages.Count > 0;
    public bool IsEmptyVisible => !IsLoading && VisiblePackages.Count == 0;

    public DownloadCenterViewModel(
        RinoPackageCatalogCoordinator catalogCoordinator,
        RinoPackageInstallationService installationService,
        RinoPackageImportService importService,
        RinoPackagePreflightService preflightService,
        RinoPackageStoragePaths storagePaths)
    {
        _catalogCoordinator = catalogCoordinator;
        _installationService = installationService;
        _importService = importService;
        _preflightService = preflightService;
        _storagePaths = storagePaths;
        FilterOptions = new List<LocalizationViewModel>
        {
            new("PackageFilterAll"),
            new("PackageFilterProjects"),
            new("PackageFilterFeatures")
        }.AsReadOnly();
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    public Task EnsureLoadedAsync()
    {
        if (_hasLoaded) return Task.CompletedTask;
        _hasLoaded = true;
        return RefreshCatalogCommand.ExecuteAsync(null);
    }

    private async Task RefreshSearchAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
            RefreshVisiblePackages();
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand]
    private async Task RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        SetStatus("PackageCatalogLoading");
        try
        {
            var refreshed = await _catalogCoordinator.RefreshAsync(DateTimeOffset.UtcNow, cancellationToken);
            _validatedCatalog = refreshed.Entry.Catalog;
            await ReplacePackagesAsync(refreshed.Entry.Catalog, cancellationToken);
            SetStatus(GetCatalogStatusResourceKey(refreshed));
        }
        catch (RinoPackageCatalogUnavailableException)
        {
            _validatedCatalog = null;
            Packages.Clear();
            RefreshVisiblePackages();
            SetStatus("PackageCatalogUnavailable");
        }
        catch (RinoPackageTransportException ex)
        {
            _validatedCatalog = null;
            Packages.Clear();
            RefreshVisiblePackages();
            if (ex.Code == "network.rateLimit" && ex.RetryAt.HasValue)
            {
                SetStatus("PackageCatalogRateLimited", ex.RetryAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                return;
            }
            SetStatus("PackageCatalogUnavailable");
        }
        catch (RinoPackageValidationException ex)
        {
            _validatedCatalog = null;
            Packages.Clear();
            RefreshVisiblePackages();
            bool isSignature = ex.Code is "catalog.signature" or "catalog.cacheDigest";
            SetStatus(isSignature ? "PackageSignatureInvalid" : "PackageCatalogUnavailable");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _validatedCatalog = null;
            Packages.Clear();
            RefreshVisiblePackages();
            SetStatus("PackageCatalogUnavailable");
        }
        catch (OperationCanceledException)
        {
            SetStatus("PackageCatalogCancelled");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsListVisible));
            OnPropertyChanged(nameof(IsEmptyVisible));
        }
    }

    [RelayCommand]
    private void CancelRefresh()
    {
        RefreshCatalogCommand.Cancel();
    }

    private async Task ReplacePackagesAsync(ValidatedRinoPackageCatalog catalog, CancellationToken cancellationToken)
    {
        var publisherDict = catalog.Catalog.PublisherKeys.ToDictionary(p => p.KeyId, p => p.PublisherName, StringComparer.Ordinal);
        Packages.Clear();
        var installedItems = new List<RinoDownloadPackageItemViewModel>();

        foreach (var package in catalog.Catalog.Packages)
        {
            string packageVersionDir = _storagePaths.GetPackageVersionDirectory(package.PackageId, package.Version);
            string publisherName = publisherDict.TryGetValue(package.PublisherKeyId, out var pub) ? pub : package.PublisherKeyId;
            var item = new RinoDownloadPackageItemViewModel(
                package,
                publisherName,
                Directory.Exists(packageVersionDir),
                !catalog.IsExpired,
                InstallPackageAsync,
                ImportProjectAsync,
                ImportFeatureAsync);
            Packages.Add(item);
            if (item.IsInstalled)
            {
                installedItems.Add(item);
            }
        }
        RefreshVisiblePackages();

        foreach (var item in installedItems)
        {
            await PrepareImportAsync(item, cancellationToken);
        }
    }

    private async Task InstallPackageAsync(RinoDownloadPackageItemViewModel item)
    {
        if (_validatedCatalog == null)
        {
            item.MarkFailed();
            return;
        }

        var cancellationToken = item.BeginDownload();
        var progress = new Progress<RinoPackageDownloadProgress>(p =>
        {
            item.ReportProgress(p.BytesReceived, p.TotalBytes);
        });

        try
        {
            await _installationService.InstallAsync(_validatedCatalog, item.PackageId, item.Version, progress, cancellationToken);
            item.MarkInstalled();
            await PrepareImportAsync(item, cancellationToken);
        }
        catch (RinoPackageValidationException ex) when (ex.Code == "install.exists")
        {
            item.MarkInstalled();
        }
        catch (Exception ex) when (ex is RinoPackageValidationException or RinoPackageTransportException or IOException or UnauthorizedAccessException)
        {
            item.MarkFailed();
        }
        catch (OperationCanceledException)
        {
            item.MarkCancelled();
        }
    }

    private async Task PrepareImportAsync(RinoDownloadPackageItemViewModel item, CancellationToken cancellationToken)
    {
        if (_validatedCatalog == null)
        {
            item.MarkImportUnavailable();
            return;
        }
        item.MarkImportChecking();
        try
        {
            var descriptor = await _importService.GetDescriptorAsync(_validatedCatalog, item.PackageId, item.Version, cancellationToken);
            var state = await _importService.ReadStateAsync(cancellationToken);
            item.PrepareImport(descriptor, state);
        }
        catch (Exception ex) when (ex is RinoPackageValidationException or IOException or UnauthorizedAccessException)
        {
            item.MarkImportUnavailable();
        }
    }

    private async Task ImportProjectAsync(RinoDownloadPackageItemViewModel item)
    {
        if (_validatedCatalog == null)
        {
            item.MarkImportFailed();
            return;
        }
        item.BeginImport();
        try
        {
            var result = await _importService.ImportProjectAsync(_validatedCatalog, item.PackageId, item.Version, item.SuggestedConfigurationName, CancellationToken.None);
            if (!ConfigurationHelper.Configs.Any(c => c.Name.Equals(result.Binding.ConfigurationName, StringComparison.OrdinalIgnoreCase)))
            {
                ConfigurationHelper.AddNewConfig(result.Binding.ConfigurationName);
            }
            item.MarkProjectImported(result.Binding.ConfigurationName);
            await TryPreflightAsync(item, result.Binding.EntrypointId);
        }
        catch (Exception ex) when (ex is RinoPackageValidationException or IOException or UnauthorizedAccessException)
        {
            item.MarkImportFailed();
        }
    }

    private async Task ImportFeatureAsync(RinoDownloadPackageItemViewModel item)
    {
        if (_validatedCatalog == null || item.SelectedFeatureEntrypoint == null)
        {
            item.MarkImportFailed();
            return;
        }
        item.BeginImport();
        try
        {
            var result = await _importService.ImportFeatureAsync(_validatedCatalog, item.PackageId, item.Version, item.SelectedFeatureEntrypoint.EntrypointId, CancellationToken.None);
            item.MarkFeatureImported(item.SelectedFeatureEntrypoint.DisplayName);
            await TryPreflightAsync(item, result.Binding.EntrypointId);
        }
        catch (Exception ex) when (ex is RinoPackageValidationException or IOException or UnauthorizedAccessException)
        {
            item.MarkImportFailed();
        }
    }

    private async Task TryPreflightAsync(RinoDownloadPackageItemViewModel item, string entrypointId)
    {
        if (_validatedCatalog == null || !_preflightService.IsBundledRuntimePresent) return;
        item.MarkPreflightChecking();
        try
        {
            var result = await _preflightService.PreflightAsync(_validatedCatalog, item.PackageId, item.Version, entrypointId, CancellationToken.None);
            if (result.Executable)
            {
                item.MarkPreflightPassed();
            }
            else
            {
                item.MarkPreflightNotExecutable();
            }
        }
        catch (Exception ex) when (ex is RinoPackageValidationException or RinoSidecarProtocolException or RinoSidecarResponseException or IOException or UnauthorizedAccessException)
        {
            item.MarkPreflightFailed();
        }
    }

    private void RefreshVisiblePackages()
    {
        string search = SearchText.Trim();
        var query = Packages.Where(p =>
            MatchesFilter(p.PackageType) &&
            (search.Length == 0 ||
             p.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             p.Summary.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
             p.Tags.Any(t => t.Contains(search, StringComparison.CurrentCultureIgnoreCase))));

        VisiblePackages.Clear();
        foreach (var item in query)
        {
            VisiblePackages.Add(item);
        }
        OnPropertyChanged(nameof(IsListVisible));
        OnPropertyChanged(nameof(IsEmptyVisible));
    }

    private bool MatchesFilter(string packageType) => SelectedFilterIndex switch
    {
        1 => packageType is "project" or "hybrid",
        2 => packageType is "feature" or "hybrid",
        _ => true,
    };

    private static string GetCatalogStatusResourceKey(RinoPackageCatalogRefreshResult refreshed)
    {
        if (refreshed.Entry.Catalog.IsExpired) return "PackageCatalogExpired";
        return refreshed.Source switch
        {
            RinoPackageCatalogRefreshSource.Network => "PackageCatalogOnline",
            RinoPackageCatalogRefreshSource.CacheNotModified => "PackageCatalogCached",
            RinoPackageCatalogRefreshSource.CacheAfterRefreshFailure => "PackageCatalogOffline",
            _ => "PackageCatalogUnavailable",
        };
    }

    private void SetStatus(string resourceKey, params string[] arguments)
    {
        _statusResourceKey = resourceKey;
        _statusArguments = arguments;
        UpdateLocalizedStatus();
    }

    private void UpdateLocalizedStatus()
    {
        StatusMessage = _statusArguments.Length == 0
            ? _statusResourceKey.ToLocalization()
            : _statusResourceKey.ToLocalizationFormatted(false, _statusArguments);
    }

    private void OnLanguageChanged(object? sender, EventArgs eventArgs)
    {
        foreach (var package in Packages)
        {
            package.RefreshLocalization();
        }
        UpdateLocalizedStatus();
        RefreshVisiblePackages();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = new CancellationTokenSource();
        _ = RefreshSearchAfterDelayAsync(_searchDebounce.Token);
    }

    partial void OnSelectedFilterIndexChanged(int value)
    {
        RefreshVisiblePackages();
    }
}

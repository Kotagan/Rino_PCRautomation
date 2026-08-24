using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAWPF.Extensions;
using MFAWPF.Services.RinoPackages;
using WPFLocalizeExtension.Engine;

namespace MFAWPF.ViewModels.UserControl.Settings;

internal partial class RinoDownloadPackageItemViewModel : ViewModel
{
    private enum DownloadState
    {
        Available,
        Downloading,
        Installed,
        Failed,
        Cancelled
    }

    private readonly RinoCatalogPackageRecordV1 _package;
    private readonly bool _canInstall;
    private readonly AsyncRelayCommand _installCommand;
    private readonly AsyncRelayCommand _importProjectCommand;
    private readonly AsyncRelayCommand _importFeatureCommand;
    private readonly RelayCommand _cancelCommand;

    private RinoPackageImportDescriptor? _importDescriptor;
    private bool _isInstalled;
    private bool _isDownloading;
    private bool _isImporting;
    private bool _isImportReady;
    private bool _isProjectImported;
    private readonly HashSet<string> _importedFeatureEntrypoints = new(StringComparer.Ordinal);
    private string _importStatusResourceKey = string.Empty;
    private string[] _importStatusArguments = Array.Empty<string>();
    private CancellationTokenSource? _downloadCancellation;
    private DownloadState _downloadState;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _packageTypeLabel = string.Empty;

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    private string _publisherLabel = string.Empty;

    [ObservableProperty]
    private string _actionText = string.Empty;

    [ObservableProperty]
    private string _itemStatus = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private RinoPackageEntrypointItemViewModel? _selectedFeatureEntrypoint;

    public string PackageId => _package.PackageId;
    public string Version => _package.Version;
    public string PackageType => _package.PackageType;
    public string PublisherName { get; }
    public IReadOnlyList<string> Tags => _package.Tags ?? Array.Empty<string>();

    public IAsyncRelayCommand InstallCommand => _installCommand;
    public IAsyncRelayCommand ImportProjectCommand => _importProjectCommand;
    public IAsyncRelayCommand ImportFeatureCommand => _importFeatureCommand;
    public IRelayCommand CancelCommand => _cancelCommand;

    public bool CanInstall => _canInstall && !_isInstalled && !_isDownloading;
    public bool IsInstalled => _isInstalled;
    public bool IsNotInstalled => !_isInstalled;
    public bool IsDownloadActionVisible => !_isInstalled && !_isDownloading;
    public bool IsImportReady => _isImportReady;

    public bool HasProjectImportOption
    {
        get
        {
            string? text = _importDescriptor?.PackageType;
            return text is "project" or "hybrid";
        }
    }

    public bool HasFeatureImportOption
    {
        get
        {
            string? text = _importDescriptor?.PackageType;
            return text is "feature" or "hybrid";
        }
    }

    public bool CanImportProject =>
        _isInstalled && _isImportReady && !_isImporting && !_isProjectImported && HasProjectImportOption;

    public bool CanImportFeature =>
        _isInstalled && _isImportReady && !_isImporting &&
        SelectedFeatureEntrypoint != null &&
        !_importedFeatureEntrypoints.Contains(SelectedFeatureEntrypoint.EntrypointId) &&
        HasFeatureImportOption;

    public ObservableCollection<RinoPackageEntrypointItemViewModel> FeatureEntrypoints { get; } = new();

    public string SuggestedConfigurationName => GetLocalizedPackageText(_importDescriptor?.SuggestedConfigurationName ?? _package.DisplayName);

    public bool IsDownloading => _isDownloading;
    public bool IsNotDownloading => !_isDownloading;

    public RinoDownloadPackageItemViewModel(
        RinoCatalogPackageRecordV1 package,
        string publisherName,
        bool isInstalled,
        bool canInstall,
        Func<RinoDownloadPackageItemViewModel, Task> install,
        Func<RinoDownloadPackageItemViewModel, Task> importProject,
        Func<RinoDownloadPackageItemViewModel, Task> importFeature)
    {
        _package = package;
        PublisherName = publisherName;
        _isInstalled = isInstalled;
        _downloadState = isInstalled ? DownloadState.Installed : DownloadState.Available;
        _canInstall = canInstall;
        _installCommand = new AsyncRelayCommand(() => install(this), () => CanInstall);
        _importProjectCommand = new AsyncRelayCommand(() => importProject(this), () => CanImportProject);
        _importFeatureCommand = new AsyncRelayCommand(() => importFeature(this), () => CanImportFeature);
        _cancelCommand = new RelayCommand(CancelDownload, () => _isDownloading);
        RefreshLocalization();
    }

    public void RefreshLocalization()
    {
        DisplayName = GetLocalizedPackageText(_package.DisplayName);
        Summary = GetLocalizedPackageText(_package.Summary);
        PackageTypeLabel = $"PackageType{char.ToUpperInvariant(PackageType[0])}{PackageType[1..]}".ToLocalization();
        VersionLabel = "PackageVersionFormat".ToLocalizationFormatted(false, Version);
        PublisherLabel = "PackagePublisherFormat".ToLocalizationFormatted(false, PublisherName);
        ActionText = _isDownloading ? "Downloading".ToLocalization() : "PackageDownloadAndVerify".ToLocalization();

        ItemStatus = (!_isInstalled || _importStatusResourceKey.Length == 0)
            ? _downloadState switch
            {
                DownloadState.Downloading => ProgressPercent > 0.0
                    ? "PackageDownloadProgress".ToLocalizationFormatted(false, ProgressPercent.ToString("0", CultureInfo.CurrentCulture))
                    : "Downloading".ToLocalization(),
                DownloadState.Installed => "PackageDownloaded".ToLocalization(),
                DownloadState.Failed => "PackageDownloadFailed".ToLocalization(),
                DownloadState.Cancelled => "PackageDownloadCancelled".ToLocalization(),
                _ => string.Empty,
            }
            : (_importStatusArguments.Length == 0
                ? _importStatusResourceKey.ToLocalization()
                : _importStatusResourceKey.ToLocalizationFormatted(false, _importStatusArguments));

        RefreshEntrypointLocalization();
    }

    public CancellationToken BeginDownload()
    {
        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        _isDownloading = true;
        _downloadState = DownloadState.Downloading;
        ProgressPercent = 0.0;
        RefreshCommandState();
        return _downloadCancellation.Token;
    }

    public void ReportProgress(long bytesReceived, long totalBytes)
    {
        ProgressPercent = totalBytes <= 0 ? 0.0 : Math.Clamp((double)bytesReceived * 100.0 / totalBytes, 0.0, 100.0);
        RefreshLocalization();
    }

    public void MarkInstalled()
    {
        CompleteDownload();
        _isInstalled = true;
        _downloadState = DownloadState.Installed;
        ProgressPercent = 100.0;
        RefreshCommandState();
    }

    public void MarkImportChecking()
    {
        _isImportReady = false;
        SetImportStatus("PackageImportChecking");
        RefreshCommandState();
    }

    public void PrepareImport(RinoPackageImportDescriptor descriptor, RinoPackageImportStateV1 state)
    {
        _importDescriptor = descriptor;
        _isImportReady = true;
        _isProjectImported = state.ProjectImports.Any(binding =>
            binding.PackageId.Equals(PackageId, StringComparison.Ordinal) &&
            binding.PackageVersion.Equals(Version, StringComparison.Ordinal) &&
            binding.EntrypointId.Equals(descriptor.DefaultProjectEntrypointId, StringComparison.Ordinal));
        _importedFeatureEntrypoints.Clear();
        foreach (var item in state.FeatureImports.Where(binding =>
                     binding.PackageId.Equals(PackageId, StringComparison.Ordinal) &&
                     binding.PackageVersion.Equals(Version, StringComparison.Ordinal)))
        {
            _importedFeatureEntrypoints.Add(item.EntrypointId);
        }
        RebuildEntrypoints();
        SetImportStatus((_isProjectImported || _importedFeatureEntrypoints.Count > 0)
            ? "PackageImportPendingRuntime"
            : "PackageDownloaded");
        RefreshCommandState();
    }

    public void BeginImport()
    {
        _isImporting = true;
        SetImportStatus("PackageImporting");
        RefreshCommandState();
    }

    public void MarkProjectImported(string configurationName)
    {
        _isImporting = false;
        _isProjectImported = true;
        SetImportStatus("PackageProjectImportedFormat", configurationName);
        RefreshCommandState();
    }

    public void MarkFeatureImported(string featureName)
    {
        _isImporting = false;
        if (SelectedFeatureEntrypoint != null)
        {
            _importedFeatureEntrypoints.Add(SelectedFeatureEntrypoint.EntrypointId);
        }
        SetImportStatus("PackageFeatureImportedFormat", featureName);
        RefreshCommandState();
    }

    public void MarkImportFailed()
    {
        _isImporting = false;
        SetImportStatus("PackageImportFailed");
        RefreshCommandState();
    }

    public void MarkImportUnavailable()
    {
        _isImporting = false;
        _isImportReady = false;
        SetImportStatus("PackageImportUnavailable");
        RefreshCommandState();
    }

    public void MarkPreflightChecking()
    {
        SetImportStatus("PackagePreflightChecking");
        RefreshCommandState();
    }

    public void MarkPreflightPassed()
    {
        SetImportStatus("PackagePreflightPassed");
        RefreshCommandState();
    }

    public void MarkPreflightNotExecutable()
    {
        SetImportStatus("PackagePreflightNotExecutable");
        RefreshCommandState();
    }

    public void MarkPreflightFailed()
    {
        SetImportStatus("PackagePreflightFailed");
        RefreshCommandState();
    }

    public void MarkFailed()
    {
        CompleteDownload();
        _downloadState = DownloadState.Failed;
        RefreshCommandState();
    }

    public void MarkCancelled()
    {
        CompleteDownload();
        _downloadState = DownloadState.Cancelled;
        RefreshCommandState();
    }

    private void CancelDownload()
    {
        _downloadCancellation?.Cancel();
    }

    private void CompleteDownload()
    {
        _isDownloading = false;
        _downloadCancellation?.Dispose();
        _downloadCancellation = null;
    }

    private void RefreshCommandState()
    {
        RefreshLocalization();
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(IsNotInstalled));
        OnPropertyChanged(nameof(IsDownloadActionVisible));
        OnPropertyChanged(nameof(IsImportReady));
        OnPropertyChanged(nameof(HasProjectImportOption));
        OnPropertyChanged(nameof(HasFeatureImportOption));
        OnPropertyChanged(nameof(CanImportProject));
        OnPropertyChanged(nameof(CanImportFeature));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsNotDownloading));
        _installCommand.NotifyCanExecuteChanged();
        _importProjectCommand.NotifyCanExecuteChanged();
        _importFeatureCommand.NotifyCanExecuteChanged();
        _cancelCommand.NotifyCanExecuteChanged();
    }

    private void SetImportStatus(string resourceKey, params string[] arguments)
    {
        _importStatusResourceKey = resourceKey;
        _importStatusArguments = arguments;
    }

    private void RebuildEntrypoints()
    {
        string? selectedId = SelectedFeatureEntrypoint?.EntrypointId;
        FeatureEntrypoints.Clear();
        if (_importDescriptor == null)
        {
            SelectedFeatureEntrypoint = null;
            return;
        }
        foreach (var entrypoint in _importDescriptor.Entrypoints)
        {
            FeatureEntrypoints.Add(new RinoPackageEntrypointItemViewModel(
                entrypoint.EntrypointId,
                GetLocalizedPackageText(entrypoint.DisplayName)));
        }
        SelectedFeatureEntrypoint = FeatureEntrypoints.FirstOrDefault(e => e.EntrypointId.Equals(selectedId, StringComparison.Ordinal))
                                    ?? FeatureEntrypoints.FirstOrDefault();
    }

    private void RefreshEntrypointLocalization()
    {
        if (_importDescriptor != null)
        {
            RebuildEntrypoints();
        }
        OnPropertyChanged(nameof(SuggestedConfigurationName));
    }

    private static string GetLocalizedPackageText(RinoPackageLocalizedTextV1 text)
    {
        var culture = LocalizeDictionary.Instance.Culture;
        if (text.Translations == null || culture == null)
        {
            return text.Default;
        }
        if (text.Translations.TryGetValue(culture.Name, out string? value))
        {
            return value;
        }
        if (text.Translations.TryGetValue(culture.TwoLetterISOLanguageName, out string? value2))
        {
            return value2;
        }
        return text.Default;
    }

    partial void OnSelectedFeatureEntrypointChanged(RinoPackageEntrypointItemViewModel? value)
    {
        OnPropertyChanged(nameof(CanImportFeature));
        _importFeatureCommand.NotifyCanExecuteChanged();
    }
}

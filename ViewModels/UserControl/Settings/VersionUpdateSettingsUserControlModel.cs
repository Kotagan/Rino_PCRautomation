using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAWPF.Configuration;
using MFAWPF.Helper;
using MFAWPF.Views.UI;
using System.Collections.ObjectModel;

using System.Reflection;

namespace MFAWPF.ViewModels.UserControl.Settings;

public partial class VersionUpdateSettingsUserControlModel : ViewModel
{
    [ObservableProperty] private string _softwareVersion = "V0.00";
    [ObservableProperty] private bool _showSoftwareVersion = true;
    partial void OnSoftwareVersionChanged(string value)
    {
        ShowSoftwareVersion = !string.IsNullOrWhiteSpace(value);
    }

    [ObservableProperty] private string _rinoVersion = "V0.00";
    [ObservableProperty] private bool _showRinoVersion = true;
    partial void OnRinoVersionChanged(string value)
    {
        ShowRinoVersion = !string.IsNullOrWhiteSpace(value);
    }

    public VersionUpdateSettingsUserControlModel()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var ver = asm.GetName().Version;
            var versionStr = !string.IsNullOrWhiteSpace(infoVer) && infoVer != "DEBUG"
                ? (infoVer.StartsWith("V", StringComparison.OrdinalIgnoreCase) ? infoVer : $"V{infoVer}")
                : (ver != null && ver.Major > 0 ? $"V{ver.Major}.{ver.Minor}.{ver.Build}" : "V1.9.0");
            _softwareVersion = versionStr;
            _rinoVersion = versionStr;
        }
        catch
        {
            _softwareVersion = "V1.9.0";
            _rinoVersion = "V1.9.0";
        }
    }

    [ObservableProperty] private string _resourceVersion = string.Empty;
    [ObservableProperty] private bool _showResourceVersion;
    partial void OnResourceVersionChanged(string value)
    {
        ShowResourceVersion = !string.IsNullOrWhiteSpace(value);
    }

    public ObservableCollection<Tool.LocalizationViewModel> DownloadSourceList =>
    [
        new("DownloadSourceGitHub"),
    ];

    [ObservableProperty] private int _downloadSourceIndex = Math.Clamp(ConfigurationHelper.GetValue(ConfigurationKeys.DownloadSourceIndex, 0), 0, 0);

    partial void OnDownloadSourceIndexChanged(int value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.DownloadSourceIndex, Math.Clamp(value, 0, 0));
    }

    [ObservableProperty] private string _gitHubMirrorUrl = ConfigurationHelper.GetValue(ConfigurationKeys.GitHubMirrorUrl, string.Empty);

    partial void OnGitHubMirrorUrlChanged(string value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.GitHubMirrorUrl, value?.Trim() ?? string.Empty);
    }

    [ObservableProperty] private string _cdkPassword = SimpleEncryptionHelper.Decrypt(ConfigurationHelper.GetValue(ConfigurationKeys.DownloadCDK, string.Empty));

    partial void OnCdkPasswordChanged(string value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.DownloadCDK, SimpleEncryptionHelper.Encrypt(value));
    }

    [ObservableProperty] private bool _enableCheckVersion = ConfigurationHelper.GetValue(ConfigurationKeys.EnableCheckVersion, true);

    [ObservableProperty] private bool _enableAutoUpdateResource = ConfigurationHelper.GetValue(ConfigurationKeys.EnableAutoUpdateResource, true);

    partial void OnEnableCheckVersionChanged(bool value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.EnableCheckVersion, value);
    }

    partial void OnEnableAutoUpdateResourceChanged(bool value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.EnableAutoUpdateResource, value);
    }

    [RelayCommand]
    private void CheckSoftwareUpdate()
    {
        VersionChecker.CheckSoftwareUpdateAsync();
    }

    [RelayCommand]
    private void UpdateResource()
    {
        VersionChecker.UpdateResourceAsync();
    }
    [RelayCommand]
    private void CheckResourceUpdate()
    {
        VersionChecker.CheckResourceVersionAsync();
    }
    [RelayCommand]
    private void UpdateMaaFW()
    {
        VersionChecker.UpdateMaaFwAsync();
    }

    [RelayCommand]
    public void OpenSoftwareUrl()
    {
        OpenUrl(MFAUrls.GitHub);
    }

    [RelayCommand]
    public void OpenResourceUrl()
    {
        var url = Extensions.Maa.MaaInterface.Instance?.Url;
        if (!string.IsNullOrWhiteSpace(url))
        {
            OpenUrl(url);
        }
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogError(ex);
        }
    }
}

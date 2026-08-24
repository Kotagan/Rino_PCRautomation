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
    [ObservableProperty] private string _softwareVersion = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.3.4.0"}";
    [ObservableProperty] private bool _showSoftwareVersion = true;
    partial void OnSoftwareVersionChanged(string value)
    {
        ShowSoftwareVersion = !string.IsNullOrWhiteSpace(value);
    }

    [ObservableProperty] private string _rinoVersion = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.3.4.0"}";
    [ObservableProperty] private bool _showRinoVersion = true;
    partial void OnRinoVersionChanged(string value)
    {
        ShowRinoVersion = !string.IsNullOrWhiteSpace(value);
    }

    [ObservableProperty] private string _resourceVersion = string.Empty;
    [ObservableProperty] private bool _showResourceVersion;
    partial void OnResourceVersionChanged(string value)
    {
        ShowResourceVersion = !string.IsNullOrWhiteSpace(value);
    }

    public ObservableCollection<Tool.LocalizationViewModel> DownloadSourceList =>
    [
        new()
        {
            Name = "GitHub"
        },
    ];

    [ObservableProperty] private int _downloadSourceIndex = Math.Clamp(ConfigurationHelper.GetValue(ConfigurationKeys.DownloadSourceIndex, 0), 0, 0);

    partial void OnDownloadSourceIndexChanged(int value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.DownloadSourceIndex, Math.Clamp(value, 0, 0));
    }

    [ObservableProperty] private string _cdkPassword = SimpleEncryptionHelper.Decrypt(ConfigurationHelper.GetValue(ConfigurationKeys.DownloadCDK, string.Empty));

    partial void OnCdkPasswordChanged(string value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.DownloadCDK, SimpleEncryptionHelper.Encrypt(value));
    }

    [ObservableProperty] private bool _enableCheckVersion = ConfigurationHelper.GetValue(ConfigurationKeys.EnableCheckVersion, true);

    [ObservableProperty] private bool _enableAutoUpdateResource = ConfigurationHelper.GetValue(ConfigurationKeys.EnableAutoUpdateResource, false);

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
}

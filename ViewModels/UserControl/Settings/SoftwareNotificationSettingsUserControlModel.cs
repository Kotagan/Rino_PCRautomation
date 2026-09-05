using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Helper;

namespace MFAWPF.ViewModels.UserControl.Settings;

public partial class SoftwareNotificationSettingsUserControlModel : ViewModel
{
    [ObservableProperty]
    private bool _enableWindowsToastNotification = ConfigurationHelper.GetValue(ConfigurationKeys.EnableWindowsToastNotification, true);

    partial void OnEnableWindowsToastNotificationChanged(bool value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.EnableWindowsToastNotification, value);
    }

    [ObservableProperty]
    private bool _enableNotificationSound = ConfigurationHelper.GetValue(ConfigurationKeys.EnableNotificationSound, true);

    partial void OnEnableNotificationSoundChanged(bool value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.EnableNotificationSound, value);
    }

    [ObservableProperty]
    private bool _enableNotificationFlashWindow = ConfigurationHelper.GetValue(ConfigurationKeys.EnableNotificationFlashWindow, true);

    partial void OnEnableNotificationFlashWindowChanged(bool value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.EnableNotificationFlashWindow, value);
    }

    [RelayCommand]
    private void SendTestNotification()
    {
        if (EnableWindowsToastNotification)
        {
            ToastNotification.ShowDirect("NotificationTestMessage".ToLocalization());
        }
        else
        {
            GrowlHelper.Warning("NotificationDisabledTip".ToLocalization());
        }
    }
}

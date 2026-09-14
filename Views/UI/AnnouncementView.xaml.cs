using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.ViewModels.UI;
using MFAWPF.Views.UserControl;

namespace MFAWPF.Views.UI;

public partial class AnnouncementView : CustomWindow
{
    private static AnnouncementView? _currentInstance;

    public AnnouncementViewModel ViewModel { get; set; }

    public AnnouncementView()
    {
        DataContext = this;
        ViewModel = Instances.AnnouncementViewModel;
        InitializeComponent();
        _currentInstance = this;
        Closed += (s, e) =>
        {
            if (_currentInstance == this)
            {
                _currentInstance = null;
            }
        };
    }

    public static void ShowUpdateCompleted(string version, string announcementContent, string? title = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            try
            {
                var vm = Instances.AnnouncementViewModel;
                var displayTitle = title ?? "UpdateCompleted".ToLocalization();
                vm.AnnouncementTitle = string.IsNullOrWhiteSpace(displayTitle) || displayTitle == "UpdateCompleted"
                    ? "更新完成"
                    : displayTitle;

                if (!string.IsNullOrWhiteSpace(version))
                {
                    var format = "UpdatedToVersion".ToLocalization();
                    vm.VersionText = string.IsNullOrWhiteSpace(format) || format == "UpdatedToVersion"
                        ? $"已更新到 {version} 版本"
                        : string.Format(format, version);
                }
                else
                {
                    vm.VersionText = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(announcementContent) && !announcementContent.Trim().Equals("placeholder", StringComparison.OrdinalIgnoreCase))
                {
                    vm.AnnouncementInfo = announcementContent;
                }
                else
                {
                    var mdPath = Path.Combine(AppContext.BaseDirectory, "resource", AnnouncementViewModel.AnnouncementFileName);
                    if (File.Exists(mdPath))
                    {
                        try { vm.AnnouncementInfo = File.ReadAllText(mdPath); } catch { }
                    }
                }

                if (string.IsNullOrWhiteSpace(vm.AnnouncementInfo) || vm.AnnouncementInfo.Trim().Equals("placeholder", StringComparison.OrdinalIgnoreCase))
                {
                    vm.AnnouncementInfo = $"### {vm.VersionText}\n\n更新已成功完成。";
                }

                vm.DoNotRemindThisAnnouncementAgain = false;
                GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAgain, bool.FalseString);

                if (_currentInstance != null && _currentInstance.IsLoaded)
                {
                    _currentInstance.Activate();
                    if (_currentInstance.WindowState == WindowState.Minimized)
                    {
                        _currentInstance.WindowState = WindowState.Normal;
                    }
                    return;
                }

                var view = new AnnouncementView();
                view.Show();
                view.Activate();
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"显示更新完成弹窗失败: {ex.Message}");
            }
        });
    }

    public void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
        };
        var parent = ((Control)sender).Parent as UIElement;
        parent?.RaiseEvent(eventArg);
    }

    private void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.DoNotRemindThisAnnouncementAgain = true;
        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAgain, bool.TrueString);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

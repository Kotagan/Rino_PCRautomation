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

    public static void ShowUpdateCompleted(string version, string announcementContent, string? title = null, bool isRestartNeeded = true, string? restartBatPath = null, string? installerExePath = null)
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

                vm.IsRestartMode = isRestartNeeded;
                vm.ConfirmButtonText = isRestartNeeded ? "确认并重启" : "ReadAlready".ToLocalization();
                vm.RestartBatPath = restartBatPath;
                vm.InstallerExePath = installerExePath;

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
                    vm.AnnouncementInfo = $"### {vm.VersionText}\n\n更新已成功完成！请点击下方“确认并重启”重新启动应用。";
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

    public static void ShowLatestVersion(string version, string announcementContent, string? title = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            try
            {
                var vm = Instances.AnnouncementViewModel;
                var displayTitle = title ?? "SoftwareUpdate".ToLocalization();
                vm.AnnouncementTitle = string.IsNullOrWhiteSpace(displayTitle) || displayTitle == "SoftwareUpdate"
                    ? "软件更新"
                    : displayTitle;

                if (!string.IsNullOrWhiteSpace(version))
                {
                    var format = "CurrentVersionIsLatest".ToLocalization();
                    vm.VersionText = string.IsNullOrWhiteSpace(format) || format == "CurrentVersionIsLatest"
                        ? $"当前版本已是最新版（{version}）"
                        : string.Format(format, version);
                }
                else
                {
                    var defaultText = "CurrentVersionIsLatest".ToLocalization();
                    vm.VersionText = string.IsNullOrWhiteSpace(defaultText) || defaultText == "CurrentVersionIsLatest"
                        ? "当前版本已是最新版"
                        : defaultText;
                }

                vm.IsRestartMode = false;
                vm.ConfirmButtonText = "ReadAlready".ToLocalization();
                vm.RestartBatPath = null;
                vm.InstallerExePath = null;

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
                    vm.AnnouncementInfo = $"### {vm.VersionText}\n\n当前暂无更新公告。";
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
                LoggerService.LogError($"显示最新版本公告弹窗失败: {ex.Message}");
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
        if (ViewModel.IsRestartMode)
        {
            RestartApplication(ViewModel.RestartBatPath, ViewModel.InstallerExePath);
            return;
        }
        ViewModel.DoNotRemindThisAnnouncementAgain = true;
        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAgain, bool.TrueString);
        Close();
    }

    public static void RestartApplication(string? batPathToExecute = null, string? installerExePath = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(installerExePath) && File.Exists(installerExePath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(installerExePath)
                {
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            else if (!string.IsNullOrEmpty(batPathToExecute) && File.Exists(batPathToExecute))
            {
                var psi = new System.Diagnostics.ProcessStartInfo(batPathToExecute)
                {
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            else
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var exePath = currentProcess.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
                    {
                        UseShellExecute = true,
                        WorkingDirectory = AppContext.BaseDirectory
                    });
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"重启应用失败: {ex.Message}");
        }
        finally
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                Application.Current?.Shutdown();
            });
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

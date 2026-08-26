using HandyControl.Controls;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MFAWPF.Views.UI.Dialog;
using MFATask = MFAWPF.Helper.ValueType.MFATask;
using RootViewModel = MFAWPF.ViewModels.UI.RootViewModel;

namespace MFAWPF.Views.UI;

public partial class RootView
{
    private const double WindowAspectRatio = 16d / 9d;
    private const double FullHdMonitorHeightPixels = 1080d;
    private const double QuadHdMonitorHeightPixels = 1440d;
    private const double UltraHdMonitorHeightPixels = 2160d;
    private const double FullHdWindowHeightPixels = 720d;
    private const double QuadHdWindowHeightPixels = 1080d;
    private const double UltraHdWindowHeightPixels = 1440d;
    private const double WindowScreenMargin = 16d;
    private const uint MonitorDefaultToNearest = 2;

    private bool _isUpdatingWindowSize;
    private bool _windowSizeInitialized;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", ExactSpelling = true, SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref NativeMonitorInfo lpmi);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    public RootViewModel ViewModel { get; set; }

    public static string Version =>
        $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "DEBUG"}";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void OpenSoftwareUrl()
    {
        MFAWPF.ViewModels.UserControl.Settings.VersionUpdateSettingsUserControlModel.OpenUrl(MFAUrls.GitHub);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void OpenResourceUrl()
    {
        var url = MaaInterface.Instance?.Url;
        if (!string.IsNullOrWhiteSpace(url))
        {
            MFAWPF.ViewModels.UserControl.Settings.VersionUpdateSettingsUserControlModel.OpenUrl(url);
        }
    }


    public RootView(RootViewModel viewModel)
    {
        // Instances.RootView = this;
        LanguageHelper.Initialize();
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        SourceInitialized += (_, _) => InitializeResponsiveWindowSize();
        SizeChanged += OnWindowSizeChanged;
        Loaded += (_, _) => LoadUI();
        Instances.TaskQueueView.InitializeData();
        OCRHelper.Initialize();
        LanguageHelper.ChangeLanguage(LanguageHelper.SupportedLanguages[ConfigurationHelper.GetValue(ConfigurationKeys.LangIndex, 0)]);

        ThemeHelper.UpdateThemeIndexChanged(ConfigurationHelper.GetValue(ConfigurationKeys.ThemeIndex, 0));
        StateChanged += (_, _) =>
        {
            if (ConfigurationHelper.GetValue(ConfigurationKeys.ShouldMinimizeToTray, false))
            {
                ChangeVisibility(WindowState != WindowState.Minimized);
            }
        };
    }

    private void InitializeResponsiveWindowSize()
    {
        if (_windowSizeInitialized)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var targetHeightPixels = GetTargetWindowHeightInPixels(GetMonitorHeightInPixels());
        var targetHeight = targetHeightPixels / GetDpiScale();
        var targetWidth = targetHeight * WindowAspectRatio;
        var maxHeight = Math.Max(MinHeight, workArea.Height - WindowScreenMargin);
        var maxWidth = Math.Max(MinWidth, workArea.Width - WindowScreenMargin);
        var scale = Math.Min(1d, Math.Min(maxHeight / targetHeight, maxWidth / targetWidth));

        targetHeight *= scale;
        targetWidth *= scale;

        _isUpdatingWindowSize = true;
        Width = Math.Max(MinWidth, targetWidth);
        Height = Math.Max(MinHeight, targetHeight);
        Left = workArea.Left + (workArea.Width - Width) / 2d;
        Top = workArea.Top + (workArea.Height - Height) / 2d;
        _isUpdatingWindowSize = false;
        _windowSizeInitialized = true;
    }

    private double GetMonitorHeightInPixels()
    {
        var fallbackHeight = SystemParameters.PrimaryScreenHeight * GetDpiScale();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return fallbackHeight;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return fallbackHeight;
        }

        var monitorInfo = new NativeMonitorInfo
        {
            Size = Marshal.SizeOf<NativeMonitorInfo>(),
        };
        if (!GetMonitorInfoW(monitor, ref monitorInfo))
        {
            return fallbackHeight;
        }

        var monitorHeight = Math.Abs(monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
        if (monitorHeight <= 0)
        {
            return fallbackHeight;
        }

        return monitorHeight;
    }

    private double GetDpiScale()
    {
        var visualScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        if (visualScale > 0d)
        {
            return visualScale;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var dpi = handle != IntPtr.Zero ? GetDpiForWindow(handle) : 0u;
        return dpi > 0u ? dpi / 96d : 1d;
    }

    private static double GetTargetWindowHeightInPixels(double monitorHeightPixels)
    {
        if (monitorHeightPixels >= UltraHdMonitorHeightPixels)
        {
            return UltraHdWindowHeightPixels;
        }

        if (monitorHeightPixels >= QuadHdMonitorHeightPixels)
        {
            return QuadHdWindowHeightPixels;
        }

        return monitorHeightPixels >= FullHdMonitorHeightPixels
            ? FullHdWindowHeightPixels
            : Math.Max(480d, monitorHeightPixels * 2d / 3d);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isUpdatingWindowSize || !_windowSizeInitialized || WindowState != WindowState.Normal)
        {
            return;
        }

        var widthChanged = Math.Abs(e.NewSize.Width - e.PreviousSize.Width);
        var heightChanged = Math.Abs(e.NewSize.Height - e.PreviousSize.Height);
        if (widthChanged < 0.5 && heightChanged < 0.5)
        {
            return;
        }

        _isUpdatingWindowSize = true;
        if (widthChanged >= heightChanged)
        {
            Height = Math.Max(MinHeight, Width / WindowAspectRatio);
        }
        else
        {
            Width = Math.Max(MinWidth, Height * WindowAspectRatio);
        }

        _isUpdatingWindowSize = false;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
        UpdateLayout();
    }

    private void ChangeVisibility(bool visible)
    {
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    public void ToggleTaskButtonsVisibility(bool isRunning)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            if (ViewModel is not null)
                ViewModel.IsRunning = isRunning;
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = !ConfirmExit();
        ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, Instances.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        MaaProcessor.Instance.SetCurrentTasker();
        base.OnClosed(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }


    public void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void Collapse()
    {
        WindowState = WindowState.Minimized;
    }

    public void SwitchWindowState()
    {
        if (WindowState == WindowState.Minimized)
        {
            ShowWindow();
        }
        else
        {
            Collapse();
        }
    }


    public void RestartMFA()
    {
        Process.Start(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
        DispatcherHelper.RunOnMainThread(Application.Current.Shutdown);
    }

    private static EditTaskDialog? _taskDialog;

    public static EditTaskDialog? TaskDialog
    {
        get
        {

            _taskDialog ??= new EditTaskDialog();
            return _taskDialog;
        }
        set => _taskDialog = value;
    }


    public void LoadUI()
    {
        DispatcherHelper.RunOnMainThread(async () =>
        {
            await Task.Delay(300);
            InitializationSettings();
            Instances.ConnectingViewModel.CurrentController = (MaaInterface.Instance?.Controller?.FirstOrDefault()?.Type).ToMaaControllerTypes(Instances.ConnectingViewModel.CurrentController);
            Console.WriteLine((MaaInterface.Instance?.Controller?.FirstOrDefault()?.Type).ToMaaControllerTypes(Instances.ConnectingViewModel.CurrentController));
            if (!Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.NoAutoStart, bool.FalseString)) && ConfigurationHelper.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("Startup", StringComparison.OrdinalIgnoreCase))
            {
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "启动前",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await WaitSoftware(),
                });
                Instances.TaskQueueView.Start(!ConfigurationHelper.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("And", StringComparison.OrdinalIgnoreCase), checkUpdate: true);
            }
            else
            {
                var isAdb = Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb;

                AddLogByKey("ConnectingTo", null, true, isAdb ? "Emulator" : "Window");

                Instances.ConnectingViewModel.TryReadAdbDeviceFromConfig(); 
                MaaProcessor.Instance.TaskQueue.Enqueue(new MFATask
                {
                    Name = "连接检测",
                    Type = MFATask.MFATaskType.MFA,
                    Action = async () => await MaaProcessor.Instance.TestConnecting(),
                });
                Instances.TaskQueueView.Start(true, checkUpdate: true);
            }

            GlobalConfiguration.SetValue("NoAutoStart", bool.FalseString);

            ViewModel.LockController = (MaaInterface.Instance?.Controller?.Count ?? 0) < 2;

            ConfigurationHelper.SetValue(ConfigurationKeys.EnableEdit, ConfigurationHelper.GetValue(ConfigurationKeys.EnableEdit, false));
            if (!string.IsNullOrWhiteSpace(MaaInterface.Instance?.Message))
            {
                GrowlHelper.Info(MaaInterface.Instance.Message);
            }
        });
        TaskManager.RunTaskAsync(async () =>
        {
            await Task.Delay(1000);
            DispatcherHelper.RunOnMainThread(() =>
            {
                Instances.AnnouncementViewModel.CheckAnnouncement();
                if (ConfigurationHelper.GetValue(ConfigurationKeys.AutoMinimize, false))
                {
                    Collapse();
                }
            });
        });
    }

    public static void AppendVersionLog(string? resourceVersion)
    {
        if (resourceVersion is null)
        {
            return;
        }
        string debugFolderPath = Path.Combine(AppContext.BaseDirectory, "debug");
        if (!Directory.Exists(debugFolderPath))
        {
            Directory.CreateDirectory(debugFolderPath);
        }

        string logFilePath = Path.Combine(debugFolderPath, "maa.log");
        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string formattedLogMessage =
            $"[{timeStamp}][INF][Px14600][Tx16498][Parser.cpp][L56][MaaNS::ProjectInterfaceNS::Parser::parse_interface] ";
        var logMessage = $"Rino Version: [mfa.version={Version}] "
            + $"Interface Version: [data.version=v{resourceVersion.Replace("v", "")}] ";
        LoggerService.LogInfo(logMessage);

        try
        {
            File.AppendAllText(logFilePath, formattedLogMessage + logMessage);
        }
        catch (Exception)
        {
            Console.WriteLine("尝试写入失败！");
        }
    }

    private void ToggleWindowTopMost(object sender, RoutedPropertyChangedEventArgs<bool> e)
    {
        Topmost = e.NewValue;
    }

    public static void AddLogByColor(string content,
        string brush = "Gray",
        string weight = "Regular",
        bool showTime = true)
        =>
            Instances.TaskQueueViewModel.AddLog(content, brush, weight, showTime);


    public static void AddLog(string content,
        Brush? brush = null,
        string weight = "Regular",
        bool showTime = true)
        =>
            Instances.TaskQueueViewModel.AddLog(content, brush, weight, showTime);

    public static void AddLogByKey(string key, Brush? brush = null, bool transformKey = true, params string[] formatArgsKeys)
        => Instances.TaskQueueViewModel.AddLogByKey(key, brush, transformKey, formatArgsKeys);
    public void RunScript(string str = "Prescript")
    {
        bool enable = str switch
        {
            "Prescript" => !string.IsNullOrWhiteSpace(ConfigurationHelper.GetValue(ConfigurationKeys.Prescript, string.Empty)),
            "Post-script" => !string.IsNullOrWhiteSpace(ConfigurationHelper.GetValue(ConfigurationKeys.Postscript, string.Empty)),
            _ => false,
        };
        if (!enable)
        {
            return;
        }

        Func<bool> func = str switch
        {
            "Prescript" => () => ExecuteScript(ConfigurationHelper.GetValue(ConfigurationKeys.Prescript, string.Empty)),
            "Post-script" => () => ExecuteScript(ConfigurationHelper.GetValue(ConfigurationKeys.Postscript, string.Empty)),
            _ => () => false,
        };

        if (!func())
        {
            LoggerService.LogError($"Failed to execute the {str}.");
        }
    }

    private static bool ExecuteScript(string scriptPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                return false;
            }

            string fileName;
            string arguments;

            if (scriptPath.StartsWith('\"'))
            {
                var parts = scriptPath.Split("\"", 3);
                fileName = parts[1];
                arguments = parts.Length > 2 ? parts[2] : string.Empty;
            }
            else
            {
                fileName = scriptPath;
                arguments = string.Empty;
            }

            bool createNoWindow = arguments.Contains("-noWindow");
            bool minimized = arguments.Contains("-minimized");

            if (createNoWindow)
            {
                arguments = arguments.Replace("-noWindow", string.Empty).Trim();
            }

            if (minimized)
            {
                arguments = arguments.Replace("-minimized", string.Empty).Trim();
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WindowStyle = minimized ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
                    CreateNoWindow = createNoWindow,
                    UseShellExecute = !createNoWindow,
                },
            };
            process.Start();
            process.WaitForExit();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void InitializationSettings()
    {

    }

    public async Task WaitSoftware()
    {
        if (ConfigurationHelper.GetValue(ConfigurationKeys.BeforeTask, "None").Contains("Startup", StringComparison.OrdinalIgnoreCase))
        {
            await MaaProcessor.Instance.StartSoftware();
        }

        Instances.ConnectingViewModel.TryReadAdbDeviceFromConfig();
    }

    public bool ConfirmExit()
    {
        if (!ViewModel.IsRunning)
            return true;
        var result = MessageBoxHelper.Show("ConfirmExitText".ToLocalization(),
            "ConfirmExitTitle".ToLocalization(), buttons: MessageBoxButton.YesNo, icon: MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public void ClearTasks(Action? action = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            Instances.TaskQueueViewModel.TaskItemViewModels = new();
            action?.Invoke();
        });
    }
}

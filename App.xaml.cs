using System.Text;
using System.Windows;
using System.Windows.Threading;
using Lierda.WPFHelper;
using MFAWPF.Configuration;
using MFAWPF.Services;
using MFAWPF.Helper;
using MFAWPF.ViewModels;
using MFAWPF.ViewModels.UI;
using MFAWPF.ViewModels.UserControl.Settings;
using MFAWPF.Views.UI;
using MFAWPF.Views.UserControl.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Runtime.InteropServices;
using MFAWPF.Services.RinoPackages;
using MFAWPF.Services.Characters;

namespace MFAWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    private static readonly IHost Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
        .ConfigureAppConfiguration(c =>
        {
            var basePath =
                Path.GetDirectoryName(AppContext.BaseDirectory)
                ?? throw new DirectoryNotFoundException(
                    "Unable to find the base directory of the application."
                );
            _ = c.SetBasePath(basePath);
        })
        .ConfigureServices(
            (context, services) =>
            {
                // App Host
                _ = services.AddHostedService<ApplicationHostService>();


                // Main window with navigation
                _ = services.AddSingleton<RootView>();
                _ = services.AddSingleton<RootViewModel>();


                // Views and ViewModels
                _ = services.AddSingleton<TaskPresetStorageService>();
                _ = services.AddSingleton<TaskQueueView>();
                _ = services.AddSingleton<TaskQueueViewModel>();
                _ = services.AddSingleton<TaskSequenceView>();
                _ = services.AddSingleton<TaskSequenceViewModel>();
                _ = services.AddSingleton<TaskQueueSettingsUserControl>();
                _ = services.AddSingleton<TaskOptionSettingsUserControl>();

                _ = services.AddSingleton(CharacterStoragePaths.CreateDefault());
                _ = services.AddSingleton<CharacterSettingsStore>();
                _ = services.AddSingleton<CharacterAnimationLoader>();
                _ = services.AddSingleton<CharacterTaskStateService>();
                _ = services.AddSingleton<CharacterRuntimeService>();
                _ = services.AddSingleton<CharacterViewModel>();
                _ = services.AddSingleton<CharacterView>();

                _ = services.AddSingleton<ConnectingView>();
                _ = services.AddSingleton<ConnectingViewModel>();
                
                _ = services.AddSingleton<SettingsView>();
                _ = services.AddSingleton<SettingsViewModel>();

                _ = services.AddSingleton(RinoPackageStoragePaths.CreateDefault());
                _ = services.AddSingleton<RinoPackageManifestReader>();
                _ = services.AddSingleton<RinoPackageCatalogReader>();
                _ = services.AddSingleton<RinoPackageArchiveValidator>();
                _ = services.AddSingleton(RinoPackageDistributionTrust.CreateCatalogRootKey());
                _ = services.AddSingleton<RinoGitHubReleaseClient>((IServiceProvider provider) => new RinoGitHubReleaseClient(RinoGitHubReleaseClient.CreateDefaultHandler(), provider.GetRequiredService<RinoPackageCatalogReader>(), RinoPackageDistributionTrust.CreateCatalogSource(), provider.GetRequiredService<RinoPackageSigningPublicKey>()));
                _ = services.AddSingleton<RinoPackageCatalogCache>();
                _ = services.AddSingleton<RinoPackageCatalogCoordinator>();
                _ = services.AddSingleton(RinoPackageDistributionTrust.CreateCompatibilityPolicy());
                _ = services.AddSingleton<RinoPackageInstallationService>();
                _ = services.AddSingleton<RinoInstalledPackageVerifier>();
                _ = services.AddSingleton<RinoPackageImportStateStore>();
                _ = services.AddSingleton<RinoPackageImportService>();
                _ = services.AddSingleton<RinoSidecarLaunchOptions>((IServiceProvider provider) => RinoSidecarLaunchOptions.CreateDefault(provider.GetRequiredService<RinoPackageStoragePaths>()));
                _ = services.AddSingleton<RinoSidecarClient>();
                _ = services.AddSingleton<RinoProjectDocumentAssembler>();
                _ = services.AddSingleton<RinoPackagePreflightService>();
                _ = services.AddSingleton<DownloadCenterViewModel>();
                _ = services.AddSingleton<DownloadCenterUserControl>();

                _ = services.AddTransient<ConfigurationMgrUserControl>();
                _ = services.AddSingleton<ConnectSettingsUserControl>();
                _ = services.AddSingleton<ConnectSettingsUserControlModel>();
                _ = services.AddSingleton<SoftwareNotificationSettingsUserControl>();
                _ = services.AddSingleton<SoftwareNotificationSettingsUserControlModel>();
                _ = services.AddSingleton<ExternalNotificationSettingsUserControl>();
                _ = services.AddSingleton<ExternalNotificationSettingsUserControlModel>();
                _ = services.AddSingleton<GameSettingsUserControl>();
                _ = services.AddSingleton<GameSettingsUserControlModel>();
                _ = services.AddSingleton<GameSettingsUserControl>();
                _ = services.AddSingleton<GuiSettingsUserControl>();
                _ = services.AddSingleton<GuiSettingsUserControlModel>();
                _ = services.AddSingleton<PerformanceUserControl>();
                _ = services.AddSingleton<PerformanceUserControlModel>();
                _ = services.AddSingleton<StartSettingsUserControl>();
                _ = services.AddSingleton<StartSettingsUserControlModel>();
                _ = services.AddSingleton<TimerSettingsUserControl>();
                _ = services.AddSingleton<TimerSettingsUserControlModel>();
                _ = services.AddSingleton<HotKeySettingsUserControl>();
                _ = services.AddSingleton<VersionUpdateSettingsUserControl>();
                _ = services.AddSingleton<VersionUpdateSettingsUserControlModel>();
                _ = services.AddSingleton<AboutUserControl>();
                
                _ = services.AddSingleton<AnnouncementViewModel>();
                // _ = services.AddSingleton<Views.Pages.DataPage>();
                // _ = services.AddSingleton<ViewModels.DataPageViewModel>();
                // _ = services.AddSingleton<Views.Pages.SettingsPage>();
                // _ = services.AddSingleton<ViewModels.SettingsViewModel>();

            }
        )
        .Build();

    /// <summary>
    /// Gets services.
    /// </summary>
    public static IServiceProvider Services => Host.Services;


    /// <summary>
    /// Occurs when the application is loading.
    /// </summary>
    async private void OnStartup(object sender, StartupEventArgs e)
    {
        AppLaunchOptions.Parse(e.Args);
        // 先按 --instance / 配置解析设备身份，再抢该设备的单实例锁与任务栏分组
        DeviceIdentity.Resolve();
        // 静默启动：不抢已有窗口焦点（避免反复重启时窗口一直弹出）
        // 同设备：唤起已有窗口；不同设备：可并行
        if (!TryAcquireInstanceSlot())
        {
            Shutdown();
            return;
        }

        // Resolve 可能在自动换号后变化，重新解析并设置任务栏 AUMID（同设备同图标）
        DeviceIdentity.Resolve();
        try
        {
            SetCurrentProcessExplicitAppUserModelID(DeviceIdentity.AppUserModelId);
        }
        catch
        {
            // ignored
        }

        var dllFolderPath = Path.Combine(AppContext.BaseDirectory, "DLL");
        if (Directory.Exists(dllFolderPath))
        {
            var dllFiles = Directory.GetFiles(dllFolderPath, "*.dll");
            foreach (var dllName in dllFiles)
            {
                var handle = LoadLibrary(dllName);
                if (handle == IntPtr.Zero)
                {
                    LoggerService.LogError($"Can't load {Path.GetFileName(dllName)}");
                }
            }
        }
        ConfigurationHelper.LoadConfig();
        // 配置落地后再按 EmulatorConfig/AdbSerial 校正一次（窗口创建前）
        DeviceIdentity.Resolve();
        try
        {
            SetCurrentProcessExplicitAppUserModelID(DeviceIdentity.AppUserModelId);
        }
        catch
        {
            // ignored
        }

        Helper.Animations.ApplePopupAnimationBehavior.Initialize();
        Helper.Animations.TabControlAnimationBehavior.Initialize();
        //Task线程内未捕获异常处理事件
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        //非UI线程未捕获异常处理事件
        AppDomain.CurrentDomain.UnhandledException +=
            CurrentDomain_UnhandledException;
        var cracker = new LierdaCracker();
        cracker.Cracker();

        await Host.StartAsync();
        SingleInstanceCoordinator.StartActivationListener();
    }
    
    /// <summary>
    /// Occurs when the application is closing.
    /// </summary>
    async private void OnExit(object sender, ExitEventArgs e)
    {
        try
        {
            await Host.StopAsync();
            Host.Dispose();
        }
        finally
        {
            SingleInstanceCoordinator.Dispose();
        }
    }

    /// <summary>
    /// Occurs when an exception is thrown by an application but not handled.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            e.Handled = true; //把 Handled 属性设为true，表示此异常已处理，程序可以继续运行，不会强制退出      
            LoggerService.LogError(e.Exception);
            ErrorView.ShowException(e.Exception);
        }
        catch (Exception ex)
        {
            //此时程序出现严重异常，将强制结束退出
            LoggerService.LogError(ex.ToString());
            ErrorView.ShowException(ex, true);
        }
    }


    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private extern static IntPtr LoadLibrary(string lpFileName);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);


    void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var sbEx = new StringBuilder();
        if (e.IsTerminating)
        {
            sbEx.Append("非UI线程发生致命错误");
        }

        sbEx.Append("非UI线程异常：");
        if (e.ExceptionObject is Exception ex)
        {
            Console.WriteLine(ex);
            LoggerService.LogError(ex.ToString());
            ErrorView.ShowException(ex);
            sbEx.Append((ex).Message);
        }
        else
        {
            LoggerService.LogError(e.ExceptionObject);
            sbEx.Append(e.ExceptionObject);
        }

        GrowlHelper.Error(sbEx.ToString());
    }

    void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        //task线程内未处理捕获
        LoggerService.LogError(e.Exception);
        ErrorView.ShowException(e.Exception);
        foreach (var item in e.Exception.InnerExceptions)
        {
            LoggerService.LogError(string.Format("异常类型：{0}{1}来自：{2}{3}异常内容：{4}",
                item.GetType(), Environment.NewLine, item.Source,
                Environment.NewLine, item.Message));
        }

        e.SetObserved(); //设置该异常已察觉（这样处理后就不会引起程序崩溃）
    }

    /// <summary>
    /// 按设备占 mutex。同设备已运行则唤起；显式 --instance 占不到则退出；
    /// 未指定时自动换下一台空闲设备再开。
    /// </summary>
    private static bool TryAcquireInstanceSlot()
    {
        // 显式实例：同设备已开则唤起；占不到（异常）才失败
        if (AppLaunchOptions.InstanceIdFromArgs)
        {
            DeviceIdentity.Resolve();
            return SingleInstanceCoordinator.TryAcquire(activateExisting: !AppLaunchOptions.Silent);
        }

        // 当前设备（默认 config / 已解析）
        DeviceIdentity.Resolve();
        if (SingleInstanceCoordinator.TryAcquire(activateExisting: !AppLaunchOptions.Silent))
            return true;

        // 默认设备已被占用：按 MuMu1 → MuMu3 → 其它 找空闲设备新开
        foreach (var id in DeviceIdentity.PreferredAutoInstanceIds())
        {
            AppLaunchOptions.AssignInstanceId(id);
            DeviceIdentity.Resolve();
            if (SingleInstanceCoordinator.TryAcquire(activateExisting: false))
            {
                LoggerService.LogInfo($"Auto multi-device -> --instance {id} device={DeviceIdentity.DeviceKey}");
                return true;
            }
        }

        return false;
    }
}

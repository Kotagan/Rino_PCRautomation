using HandyControl.Controls;
using HandyControl.Tools.Extension;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;
using MaaFramework.Binding.Notification;
using MailKit.Net.Smtp;
using MailKit.Security;
using MFAWPF.Configuration;
using MFAWPF.Helper;
using MFAWPF.Helper.Converters;
using MFAWPF.Helper.ValueType;
using MFAWPF.ViewModels.UserControl.Settings;
using MFAWPF.Views.UI;
using MFAWPF.Services.Characters;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MimeKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DragItemViewModel = MFAWPF.ViewModels.Tool.DragItemViewModel;
using MFATask = MFAWPF.Helper.ValueType.MFATask;


namespace MFAWPF.Extensions.Maa;

public class MaaProcessor
{
    private static MaaProcessor? _instance;
    private static Random Random = new();
    public static MaaUtility MaaUtility { get; } = new();
    public static MaaToolkit MaaToolkit { get; } = new(init: true);

    public CancellationTokenSource? CancellationTokenSource { get; set; } = new();

    private MaaTasker? _currentTasker;
    private MaaAgentClient? _agentClient;
    private bool _agentStarted;
    private bool _characterTaskRunActive;
    private Process? _agentProcess;
    public static string Resource => AppContext.BaseDirectory + "Resource";
    public static string ResourceBase => $"{Resource}/base";

    public ObservableQueue<MFATask> TaskQueue { get; } = new();

    public static MaaFWConfiguration MaaFwConfiguration { get; } = new();

    public static AutoInitDictionary AutoInitDictionary { get; } = new();

    public static MaaProcessor Instance
    {
        get => _instance ??= new MaaProcessor();
        set => _instance = value;
    }

    public MaaProcessor()
    {
        TaskQueue.CountChanged += (_, args) =>
        {
            if (args.NewValue > 0)
                Instances.RootViewModel.IsRunning = true;
        };
    }

    public class TaskAndParam
    {
        public string? Name { get; set; }
        public string? Entry { get; set; }
        public int? Count { get; set; }
        public string? Param { get; set; }
    }

    private DateTime? _startTime;

    public static int Money { get; set; } = 0;

    public async Task Start(List<DragItemViewModel>? tasks, bool onlyStart = false, bool checkUpdate = false)
    {
        CancellationTokenSource = new CancellationTokenSource();
        Instances.RootViewModel.SetIdle(false);

        _startTime = DateTime.Now;
        tasks ??= new List<DragItemViewModel>();
        _characterTaskRunActive = !onlyStart && tasks.Count > 0;
        if (_characterTaskRunActive)
        {
            Instances.CharacterTaskStateService.Notify(CharacterTaskState.QueueStarted);
        }

        var token = CancellationTokenSource.Token;
        if (!onlyStart)
        {
            var taskAndParams = tasks.SelectMany(ExpandTaskAndParams).ToList();
            InitializeConnectionTasksAsync(token);
            AddCoreTasksAsync(taskAndParams, token);
        }

        AddPostTasksAsync(checkUpdate, token);
        await TaskManager.RunTaskAsync(async () =>
        {
            var runSuccess = await ExecuteTasks(token);
            if (runSuccess)
            {
                Stop(true, onlyStart);
            }
            else if (!token.IsCancellationRequested)
            {
                Stop();
            }
        }, token, name: "启动任务");

    }

    private void InitializeConnectionTasksAsync(CancellationToken token)
    {
        TaskQueue.Enqueue(CreateMFATask("启动脚本", async () =>
        {
            await TaskManager.RunTaskAsync(() => Instances.RootView.RunScript(), token);
        }));

        TaskQueue.Enqueue(CreateMFATask("连接设备", async () =>
        {
            await HandleDeviceConnectionAsync(token);
        }));

        TaskQueue.Enqueue(CreateMFATask("性能基准", async () =>
        {
            await MeasureScreencapPerformanceAsync(token);
        }));
    }

    async private Task HandleDeviceConnectionAsync(CancellationToken token)
    {
        var controllerType = Instances.ConnectingViewModel.CurrentController;
        var isAdb = controllerType == MaaControllerTypes.Adb;
        var targetKey = isAdb ? "Emulator" : "Window";
        var targetName = targetKey.ToLocalization();

        if (Instances.ConnectingViewModel.CurrentDevice == null && isAdb)
            Instances.ConnectingViewModel.TryReadAdbDeviceFromConfig(false, true);

        var waitTime = ConfigurationHelper.GetValue(ConfigurationKeys.ConnectWaitTime, 30.0);
        if (waitTime <= 0) waitTime = 30.0;

        var timeout = TimeSpan.FromSeconds(waitTime);
        var stopwatch = Stopwatch.StartNew();

        int initialSeconds = (int)Math.Ceiling(waitTime);
        var logItem = RootView.AddCountdownLog(
            string.Format("ConnectingWithCountdown".ToLocalization(), targetName, initialSeconds)
        );

        bool connected = false;
        bool retrySoftwareTriggered = false;
        bool reconnectAdbTriggered = false;
        bool restartAdbTriggered = false;
        bool hardRestartAdbTriggered = false;

        while (stopwatch.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();

            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((timeout - stopwatch.Elapsed).TotalSeconds));
            if (logItem != null)
            {
                RootView.UpdateLog(
                    logItem,
                    string.Format("ConnectingWithCountdown".ToLocalization(), targetName, remainingSeconds)
                );
            }

            SetCurrentTasker(null);
            connected = await TryConnectAsync(token);
            if (connected)
            {
                break;
            }

            if (isAdb)
            {
                if (Instances.ConnectingViewModel.CurrentDevice == null)
                {
                    Instances.ConnectingViewModel.TryReadAdbDeviceFromConfig(true, true);
                }

                var elapsed = stopwatch.Elapsed.TotalSeconds;

                // 启动任务连不上时自动拉起模拟器（MuMu 优先；兼容旧 SoftPath 配置）
                if (!retrySoftwareTriggered)
                {
                    retrySoftwareTriggered = true;
                    LoggerService.LogInfo("TryToStartEmulator".ToLocalization());
                    RootView.AddLogByKey("TryToStartEmulator");
                    await StartSoftware();
                    // 拉起后延长连接窗口，避免 WaitSoftware 耗尽 ConnectWaitTime
                    var extraWait = ConfigurationHelper.GetValue(ConfigurationKeys.WaitSoftwareTime, 60.0);
                    timeout = TimeSpan.FromSeconds(Math.Max(waitTime, stopwatch.Elapsed.TotalSeconds + Math.Max(extraWait, 30.0)));
                    Instances.ConnectingViewModel.TryReadAdbDeviceFromConfig(true, true);
                    try
                    {
                        Instances.ConnectingViewModel.AutoDetectDevice(silent: true);
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogWarning($"AutoDetectDevice after emulator launch failed: {ex.Message}");
                    }
                }
                // 持续失败 3 秒后，尝试重连 ADB (重置连接状态)
                else if (!reconnectAdbTriggered && elapsed >= 3.0)
                {
                    reconnectAdbTriggered = true;
                    LoggerService.LogInfo("TryToReconnectByAdb".ToLocalization());
                    await ReconnectByAdb();
                }
                // 持续失败 10 秒后，尝试重启 ADB Server
                else if (!restartAdbTriggered && elapsed >= 10.0 && Instances.ConnectSettingsUserControlModel.AllowAdbRestart)
                {
                    restartAdbTriggered = true;
                    LoggerService.LogInfo("RestartAdb".ToLocalization());
                    await RestartAdb();
                }
                // 持续失败 18 秒后，尝试强制重启 ADB 进程
                else if (!hardRestartAdbTriggered && elapsed >= 18.0 && Instances.ConnectSettingsUserControlModel.AllowAdbHardRestart)
                {
                    hardRestartAdbTriggered = true;
                    LoggerService.LogInfo("HardRestartAdb".ToLocalization());
                    await HardRestartAdb();
                }
            }

            try
            {
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        token.ThrowIfCancellationRequested();

        if (connected)
        {
            if (logItem != null)
            {
                RootView.UpdateLog(
                    logItem,
                    string.Format("ConnectingSuccess".ToLocalization(), targetName)
                );
            }
            Instances.ConnectingViewModel.SetConnected(true);
        }
        else
        {
            if (logItem != null)
            {
                RootView.UpdateLog(
                    logItem,
                    "ConnectFailed".ToLocalization()
                );
            }
            await HandleConnectionFailureAsync(isAdb, token, suppressLog: true);
            throw new Exception("Connection failed after all retries");
        }
    }

    async private Task HandleConnectionFailureAsync(bool isAdb, CancellationToken token, bool suppressLog = false)
    {
        token.ThrowIfCancellationRequested();
        LoggerService.LogWarning("ConnectFailed".ToLocalization());
        if (!suppressLog)
        {
            RootView.AddLogByKey("ConnectFailed");
        }
        Instances.ConnectingViewModel.SetConnected(false);
        GrowlHelper.Warning("Warning_CannotConnect".ToLocalizationFormatted(true, isAdb ? "Emulator" : "Window"));
        Stop();
    }

    private void AddCoreTasksAsync(List<TaskAndParam> taskAndParams, CancellationToken token)
    {
        foreach (var task in taskAndParams)
        {
            TaskQueue.Enqueue(CreateMaaFWTask(task.Name, task.Entry,
                async () =>
                {
                    token.ThrowIfCancellationRequested();
                    await TryRunTasksAsync(_currentTasker, task.Entry, task.Param, token);
                }, task.Count ?? 1
            ));
        }
    }

    async private Task AddPostTasksAsync(bool checkUpdate, CancellationToken token)
    {
        TaskQueue.Enqueue(CreateMFATask("结束脚本", async () =>
        {
            await TaskManager.RunTaskAsync(() => Instances.RootView.RunScript("Post-script"), token);
        }));

        if (checkUpdate)
        {
            TaskQueue.Enqueue(CreateMFATask("检查更新", async () =>
            {
                VersionChecker.Check();
            }));
        }
    }
    private MFATask CreateMaaFWTask(string? name, string? key, Func<Task> action, int count = 1)
    {
        return new MFATask
        {
            Name = name,
            Key = key ?? name,
            Count = count,
            Type = MFATask.MFATaskType.MAAFW,
            ReportsCharacterState = true,
            Action = action
        };
    }

    private MFATask CreateMFATask(string? name, Func<Task> action)
    {
        return new MFATask
        {
            Name = name,
            Type = MFATask.MFATaskType.MFA,
            Action = action
        };
    }

    async public Task MeasureScreencapPerformanceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await MeasureExecutionTimeAsync(async () =>
            _currentTasker?.Controller.Screencap().Wait());
    }

    async private Task<bool> TryConnectAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var instance = await GetCurrentTaskerAsync(token);
        return instance is { IsInitialized: true };
    }

    async private Task TryRunTasksAsync(MaaTasker? maa, string? task, string? param, CancellationToken token)
    {
        if (maa == null)
            throw new InvalidOperationException("InitInstanceFailed".ToLocalization());
        if (task == null)
            throw new InvalidOperationException("Task entry is null");

        var job = maa.AppendTask(task, param ?? "{}");
        await TaskManager.RunTaskAsync(
            () => job.Wait().ThrowIfNot(MaaJobStatus.Succeeded),
            token,
            catchException: false,
            shouldLog: false);
    }

    public void Stop(bool finished = false, bool onlyStart = false)
    {
        try
        {
            if (!ShouldProcessStop(finished))
            {
                GrowlHelper.Warning("NoTaskToStop".ToLocalization());

                ClearTaskQueue();
                return;
            }

            CancelOperations();

            ClearTaskQueue();

            Instances.RootViewModel.IsRunning = false;

            if (_characterTaskRunActive)
            {
                if (finished)
                {
                    Instances.CharacterTaskStateService.Notify(CharacterTaskState.QueueSucceeded);
                }
                else if (Instances.CharacterTaskStateService.CurrentState != CharacterTaskState.TaskFailed)
                {
                    Instances.CharacterTaskStateService.Notify(CharacterTaskState.QueueStopped);
                }

                _characterTaskRunActive = false;
            }

            ExecuteStopCore(finished, () =>
            {
                var stopResult = AbortCurrentTasker();
                HandleStopResult(finished, stopResult, onlyStart);
            });

        }
        catch (Exception ex)
        {
            HandleStopException(ex);
        }
    }

    #region Stop Helpers

    private void CancelOperations()
    {
        if (!_agentStarted)
        {
            _agentProcess?.Kill();
            _agentProcess?.Dispose();
            _agentProcess = null;
        }
        _emulatorCancellationTokenSource?.SafeCancel();
        CancellationTokenSource.SafeCancel();
    }

    private bool ShouldProcessStop(bool finished)
    {
        return (CancellationTokenSource?.IsCancellationRequested).IsFalse()
            || finished;
    }

    private void ExecuteStopCore(bool finished, Action stopAction)
    {
        TaskManager.RunTaskAsync(() =>
        {
            if (!finished)
                RootView.AddLogByKey("Stopping");

            stopAction.Invoke();

            Instances.RootViewModel.SetIdle(true);
        }, null, "停止任务");
    }

    private bool AbortCurrentTasker()
    {
        return _currentTasker == null || _currentTasker.Abort().Wait() == MaaJobStatus.Succeeded;
    }

    private void HandleStopResult(bool finished, bool success, bool onlyStart)
    {
        if (success)
        {
            DisplayTaskCompletionMessage(finished, onlyStart);
        }
        else
        {
            GrowlHelper.Error("StoppingFailed".ToLocalization());
        }
    }

    private void ClearTaskQueue()
    {
        TaskQueue.Clear();
    }

    private void HandleStopException(Exception ex)
    {
        LoggerService.LogError($"Stop operation failed: {ex.Message}");
        GrowlHelper.Error("StopOperationFailed".ToLocalization());
    }

    #endregion


    public void HandleAfterTaskOperation()
    {
        var afterTask = ConfigurationHelper.GetValue(ConfigurationKeys.AfterTask, "None");
        switch (afterTask)
        {
            case "CloseMFA":
                CloseMFA();
                break;
            case "CloseEmulator":
                CloseSoftware();
                break;
            case "CloseEmulatorAndMFA":
                CloseSoftwareAndMFA();
                break;
            case "ShutDown":
                ShutDown();
                break;
            case "CloseEmulatorAndRestartMFA":
                CloseSoftwareAndRestartMFA();
                break;
            case "RestartPC":
                Restart();
                break;
        }
    }


    private CancellationTokenSource? _emulatorCancellationTokenSource;

    private static Process? _softwareProcess;

    public async Task StartSoftware()
    {
        _emulatorCancellationTokenSource?.SafeCancel();
        _emulatorCancellationTokenSource = new CancellationTokenSource();
        var token = _emulatorCancellationTokenSource.Token;
        var waitTime = ConfigurationHelper.GetValue(ConfigurationKeys.WaitSoftwareTime, 60.0);

        if (DeviceIdentity.TryResolveMuMuIndex(out var mumuIndex))
        {
            await LaunchMuMuInstanceAsync(mumuIndex, waitTime, token);
            return;
        }

        await StartRunnableFile(ConfigurationHelper.GetValue(ConfigurationKeys.SoftwarePath, string.Empty),
            waitTime, token);
    }

    /// <summary>通过 MuMuManager 拉起指定多开实例，并等待 ADB 可连。</summary>
    async private Task LaunchMuMuInstanceAsync(int index, double waitTimeInSeconds, CancellationToken token)
    {
        const string managerPath = @"C:\Program Files\Netease\MuMu\nx_main\MuMuManager.exe";
        const string adbPath = @"C:\Program Files\Netease\MuMu\nx_main\adb.exe";
        if (!File.Exists(managerPath))
        {
            LoggerService.LogWarning($"MuMuManager not found: {managerPath}, fallback SoftwarePath");
            await StartRunnableFile(ConfigurationHelper.GetValue(ConfigurationKeys.SoftwarePath, string.Empty),
                waitTimeInSeconds, token);
            return;
        }

        try
        {
            LoggerService.LogInfo($"Launch MuMu instance {index} via MuMuManager");
            _softwareProcess = Process.Start(new ProcessStartInfo
            {
                FileName = managerPath,
                Arguments = $"control -v {index} launch",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(managerPath)!,
                CreateNoWindow = false
            });
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"Launch MuMu-{index} failed: {ex.Message}");
        }

        var serial = DeviceIdentity.GetMuMuAdbSerial(index);
        for (double remainingTime = waitTimeInSeconds + 1; remainingTime > 0; remainingTime -= 1)
        {
            token.ThrowIfCancellationRequested();

            if (remainingTime % 10 == 0)
            {
                RootView.AddLogByKey("WaitSoftwareTime", null, true, "Emulator", remainingTime.ToString());
            }

            if (!string.IsNullOrEmpty(serial) && File.Exists(adbPath))
            {
                try
                {
                    var connect = Process.Start(new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = $"connect {serial}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    });
                    connect?.WaitForExit(3000);
                }
                catch
                {
                    // ignore adb connect noise while booting
                }
            }

            // MuMu 已就绪则提前结束等待
            try
            {
                var info = Process.Start(new ProcessStartInfo
                {
                    FileName = managerPath,
                    Arguments = $"info -v {index}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (info != null)
                {
                    var output = await info.StandardOutput.ReadToEndAsync(token);
                    await info.WaitForExitAsync(token);
                    if (output.Contains("\"player_state\": \"start_finished\"", StringComparison.OrdinalIgnoreCase)
                        || output.Contains("\"player_state\":\"start_finished\"", StringComparison.OrdinalIgnoreCase))
                    {
                        if (output.Contains("\"is_android_started\": true", StringComparison.OrdinalIgnoreCase)
                            || output.Contains("\"is_android_started\":true", StringComparison.OrdinalIgnoreCase))
                        {
                            LoggerService.LogInfo($"MuMu-{index} android started");
                            await Task.Delay(2000, token);
                            return;
                        }
                    }
                }
            }
            catch
            {
                // keep waiting
            }

            await Task.Delay(1000, token);
        }
    }

    async private Task StartRunnableFile(string exePath, double waitTimeInSeconds, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;
        var processName = Path.GetFileNameWithoutExtension(exePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            CreateNoWindow = false
        };
        if (Process.GetProcessesByName(processName).Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(ConfigurationHelper.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty)))
            {
                startInfo.Arguments = ConfigurationHelper.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
                _softwareProcess =
                    Process.Start(startInfo);
            }
            else
                _softwareProcess = Process.Start(startInfo);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(ConfigurationHelper.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty)))
            {
                startInfo.Arguments = ConfigurationHelper.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
                _softwareProcess = Process.Start(startInfo);
            }
            else
                _softwareProcess = Process.Start(startInfo);
        }

        for (double remainingTime = waitTimeInSeconds + 1; remainingTime > 0; remainingTime -= 1)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (remainingTime % 10 == 0)
            {
                RootView.AddLogByKey("WaitSoftwareTime", null, true,
                    Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb
                        ? "Emulator"
                        : "Window",
                    remainingTime.ToString()
                );
            }

            await Task.Delay(1000, token);
        }

    }

    private static string GetCommandLine(Process process)
    {
        return GetCommandLine(process.Id);
    }

    private static string GetCommandLine(int processId)
    {
        var commandLine = string.Empty;

        // 使用 WMI 查询命令行参数
        var query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}";
        using var searcher = new ManagementObjectSearcher(query);

        foreach (var obj in searcher.Get())
        {
            commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
        }

        return commandLine;
    }

    public static void CloseSoftware(Action? action = null)
    {
        if (Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb)
        {
            EmulatorHelper.KillEmulatorModeSwitcher();
        }
        else
        {
            if (_softwareProcess != null && !_softwareProcess.HasExited)
            {
                _softwareProcess.Kill();
            }
            else
            {
                CloseProcessesByName(MaaFwConfiguration.DesktopWindow.Name, ConfigurationHelper.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty));
                _softwareProcess = null;
            }

        }
        action?.Invoke();
    }

    private static void CloseProcessesByName(string processName, string emulatorConfig)
    {
        var processes = Process.GetProcesses().Where(p => p.ProcessName.StartsWith(processName));
        foreach (var process in processes)
        {
            try
            {
                var commandLine = GetCommandLine(process);
                if (string.IsNullOrEmpty(emulatorConfig) || commandLine.ToLower().Contains(emulatorConfig.ToLower()))
                {
                    process.Kill();
                    break;
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogInfo($"Error closing process: {ex.Message}");
            }
        }
    }

    public static void CloseMFA()
    {
        DispatcherHelper.RunOnMainThread(Application.Current.Shutdown);
    }


    public static void CloseSoftwareAndMFA()
    {
        CloseSoftware(CloseMFA);
    }

    public static void ShutDown()
    {
        CloseSoftware();
        Process.Start("shutdown", "/s /t 0");
    }


    public static void RestartMFA(bool noAutoStart = false)
    {
        if (noAutoStart)
            GlobalConfiguration.SetValue(ConfigurationKeys.NoAutoStart, bool.TrueString);
        Process.Start(Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty);
        DispatcherHelper.RunOnMainThread(Application.Current.Shutdown);
    }

    public static void Restart()
    {
        CloseSoftware();
        Process.Start("shutdown", "/r /t 0");
    }

    public static void CloseSoftwareAndRestartMFA()
    {
        CloseSoftware();
        RestartMFA();
    }

    private IEnumerable<TaskAndParam> ExpandTaskAndParams(DragItemViewModel task)
    {
        var options = task.InterfaceItem?.Option;
        if (options == null || options.Count == 0 || MaaInterface.Instance?.Option == null)
        {
            yield return CreateTaskAndParam(task);
            yield break;
        }

        var multiDimensions = new List<List<(string Name, int Index, string CaseName)>>();
        foreach (var selectOption in options)
        {
            if (MaaInterface.Instance.Option.TryGetValue(selectOption.Name ?? string.Empty, out var interfaceOption)
                != true
                || !interfaceOption.Multi
                || interfaceOption.Cases == null)
                continue;

            var selected = selectOption.GetSelectedIndexes()
                .Where(i => i >= 0 && i < interfaceOption.Cases.Count)
                .Select(i => (selectOption.Name ?? string.Empty, i, interfaceOption.Cases[i].Name ?? $"#{i + 1}"))
                .ToList();
            if (selected.Count > 0)
            {
                multiDimensions.Add(selected);
            }
            else
            {
                // 多选未勾选：明确报错，避免静默按无预设跑一遍
                var optionName = LanguageHelper.GetLocalizedString(selectOption.Name);
                var tip = string.Format(
                    LanguageHelper.GetLocalizedString("MultiSelectRequiredTip"),
                    optionName);
                Growl.Warning(tip);
                RootView.AddLog(tip, BrushConverterHelper.ConvertToBrush("DarkGoldenrod"));
            }
        }

        if (multiDimensions.Count == 0)
        {
            yield return CreateTaskAndParam(task);
            yield break;
        }

        foreach (var combo in CartesianProduct(multiDimensions))
        {
            var overrides = combo.ToDictionary(c => c.Name, c => c.Index);
            var suffix = string.Join("+", combo.Select(c => c.CaseName));
            var baseName = task.InterfaceItem?.Name ?? "任务";
            yield return CreateTaskAndParam(task, overrides, $"{baseName}({suffix})");
        }
    }

    private static IEnumerable<List<(string Name, int Index, string CaseName)>> CartesianProduct(
        List<List<(string Name, int Index, string CaseName)>> dimensions)
    {
        IEnumerable<List<(string Name, int Index, string CaseName)>> seed = [[]];
        return dimensions.Aggregate(seed, (acc, dim) =>
            acc.SelectMany(prefix => dim.Select(item =>
            {
                var next = new List<(string Name, int Index, string CaseName)>(prefix) { item };
                return next;
            })));
    }

    private TaskAndParam CreateTaskAndParam(
        DragItemViewModel task,
        Dictionary<string, int>? indexOverrides = null,
        string? displayName = null)
    {
        var taskModels = JsonConvert.DeserializeObject<Dictionary<string, TaskModel>>(JsonConvert.SerializeObject(task.InterfaceItem?.PipelineOverride ?? new Dictionary<string, TaskModel>(), new JsonSerializerSettings()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        }));

        UpdateTaskDictionary(ref taskModels, task.InterfaceItem?.Option, task.InterfaceItem?.Advanced, indexOverrides);

        var taskParams = SerializeTaskParams(taskModels);

        return new TaskAndParam
        {
            Name = displayName ?? task.InterfaceItem?.Name,
            Entry = task.InterfaceItem?.Entry,
            Count = task.InterfaceItem?.Repeatable == true ? (task.InterfaceItem?.RepeatCount ?? 1) : 1,
            Param = taskParams
        };
    }

    private void UpdateTaskDictionary(ref Dictionary<string, TaskModel> taskModels,
        List<MaaInterface.MaaInterfaceSelectOption>? options,
        List<MaaInterface.MaaInterfaceSelectAdvanced>? advanceds,
        Dictionary<string, int>? indexOverrides = null)
    {
        ApplySelectOptions(ref taskModels, options, indexOverrides);

        if (advanceds != null)
        {
            foreach (var selectAdvanced in advanceds)
            {
                if (MaaInterface.Instance?.Advanced?.TryGetValue(selectAdvanced.Name ?? string.Empty,
                        out var interfaceOption)
                    == true)
                {
                    var pipeOverride = interfaceOption.GenerateProcessedPipeline(selectAdvanced.Data);
                    if (!string.IsNullOrWhiteSpace(pipeOverride) && pipeOverride != "{}")
                    {
                        var param = JsonConvert.DeserializeObject<Dictionary<string, TaskModel>>(pipeOverride);
                        taskModels = taskModels.MergeTaskModels(param);
                    }
                }
            }
        }

        ApplySelectOptions(ref taskModels, options, indexOverrides);
    }

    private void ApplySelectOptions(
        ref Dictionary<string, TaskModel> taskModels,
        List<MaaInterface.MaaInterfaceSelectOption>? options,
        Dictionary<string, int>? indexOverrides)
    {
        if (options == null) return;

        foreach (var selectOption in options)
        {
            var name = selectOption.Name ?? string.Empty;
            if (MaaInterface.Instance?.Option?.TryGetValue(name, out var interfaceOption) != true
                || interfaceOption.Cases == null)
                continue;

            int? index = null;
            if (indexOverrides != null && indexOverrides.TryGetValue(name, out var overridden))
                index = overridden;
            else if (!interfaceOption.Multi)
                index = selectOption.Index;

            if (index is int i
                && i >= 0
                && i < interfaceOption.Cases.Count
                && interfaceOption.Cases[i]?.PipelineOverride != null)
            {
                taskModels = taskModels.MergeTaskModels(interfaceOption.Cases[i].PipelineOverride);
            }
        }
    }

    private string SerializeTaskParams(Dictionary<string, TaskModel> taskModels)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };

        try
        {
            return JsonConvert.SerializeObject(taskModels, settings);
        }
        catch (Exception)
        {
            return "{}";
        }
    }

    static void MeasureExecutionTime(Action methodToMeasure)
    {
        var stopwatch = Stopwatch.StartNew();

        methodToMeasure();

        stopwatch.Stop();
        long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        switch (elapsedMilliseconds)
        {
            case >= 800:
                RootView.AddLogByKey("ScreencapErrorTip", BrushConverterHelper.ConvertToBrush("DarkGoldenrod"), false, elapsedMilliseconds.ToString(),
                    Instances.TaskQueueView.ScreenshotType());
                break;

            case >= 400:
                RootView.AddLogByKey("ScreencapWarningTip", BrushConverterHelper.ConvertToBrush("DarkGoldenrod"), false, elapsedMilliseconds.ToString(),
                    Instances.TaskQueueView.ScreenshotType());
                break;

            default:
                RootView.AddLogByKey("ScreencapCost", null, false, elapsedMilliseconds.ToString(),
                    Instances.TaskQueueView.ScreenshotType());
                break;
        }
    }
    async static Task MeasureExecutionTimeAsync(Func<Task> methodToMeasure)
    {
        const int sampleCount = 4;
        long totalElapsed = 0;

        long min = 10000;
        long max = 0;
        for (int i = 0; i < sampleCount; i++)
        {
            var sw = Stopwatch.StartNew();
            await methodToMeasure();
            sw.Stop();
            min = Math.Min(min, sw.ElapsedMilliseconds);
            max = Math.Max(max, sw.ElapsedMilliseconds);
            totalElapsed += sw.ElapsedMilliseconds;
        }

        var avgElapsed = totalElapsed / sampleCount;

        switch (avgElapsed)
        {
            case >= 800:
                RootView.AddLogByKey("ScreencapErrorTip", BrushConverterHelper.ConvertToBrush("DarkGoldenrod"), false, avgElapsed.ToString(),
                    Instances.TaskQueueView.ScreenshotType());
                break;

            case >= 400:
                RootView.AddLogByKey("ScreencapWarningTip", BrushConverterHelper.ConvertToBrush("DarkGoldenrod"), false, avgElapsed.ToString(),
                    Instances.TaskQueueView.ScreenshotType());
                break;

            default:
                RootView.AddLogByKey("ScreencapCost", null, false, avgElapsed.ToString(),
                    Instances.TaskQueueView.ScreenshotType());
                break;
        }
    }


    async private Task<bool> ExecuteTasks(CancellationToken token)
    {
        var coreTaskFailed = false;
        while (TaskQueue.Count > 0 && !token.IsCancellationRequested)
        {
            var task = TaskQueue.Dequeue();
            if (task.ReportsCharacterState)
            {
                Instances.CharacterTaskStateService.Notify(
                    CharacterTaskState.TaskStarted,
                    task.Key ?? task.Name ?? string.Empty,
                    task.Name ?? string.Empty);
            }

            if (!await task.Run(token))
            {
                if (token.IsCancellationRequested)
                    return false;

                if (task.ReportsCharacterState)
                {
                    Instances.CharacterTaskStateService.Notify(
                        CharacterTaskState.TaskFailed,
                        task.Key ?? task.Name ?? string.Empty,
                        task.Name ?? string.Empty);
                    // 流水线任务失败不中断队列，继续执行后续任务
                    coreTaskFailed = true;
                    RootView.AddLogByKey(
                        "TaskFailedContinue",
                        BrushConverterHelper.ConvertToBrush("DarkOrange"),
                        true,
                        task.Name ?? string.Empty);
                    continue;
                }

                // 连接/脚本等基础步骤失败则停止
                RootView.AddLogByKey(
                    "TaskStepFailedStop",
                    BrushConverterHelper.ConvertToBrush("OrangeRed"),
                    true,
                    task.Name ?? string.Empty);
                return false;
            }

            if (task.ReportsCharacterState)
            {
                Instances.CharacterTaskStateService.Notify(
                    CharacterTaskState.TaskSucceeded,
                    task.Key ?? task.Name ?? string.Empty,
                    task.Name ?? string.Empty);
            }
        }

        if (coreTaskFailed && !token.IsCancellationRequested)
        {
            RootView.AddLogByKey("QueueCompletedWithFailures", BrushConverterHelper.ConvertToBrush("DarkOrange"));
        }

        return !token.IsCancellationRequested;
    }

    private void DisplayTaskCompletionMessage(bool finished, bool onlyStart = false)
    {
        if (!finished)
        {
            Growl.Info("TaskStopped".ToLocalization());
            RootView.AddLogByKey("TaskAbandoned");
        }
        else
        {
            if (!onlyStart)
            {
                ToastNotification.ShowDirect("TaskCompleted".ToLocalization());
                Instances.TaskQueueViewModel.TaskItemViewModels.Where(t => t.IsCheckedWithNull == null).ToList().ForEach(d => d.IsCheckedWithNull = false);
            }

            if (_startTime != null)
            {
                var elapsedTime = DateTime.Now - (DateTime)_startTime;
                RootView.AddLogByKey("TaskAllCompletedWithTime", null, true, ((int)elapsedTime.TotalHours).ToString(),
                    ((int)elapsedTime.TotalMinutes % 60).ToString(), ((int)elapsedTime.TotalSeconds % 60).ToString());
            }
            else
            {
                RootView.AddLogByKey("TaskAllCompleted");
            }
            if (!onlyStart)
            {
                ExternalNotificationHelper.ExternalNotificationAsync();
                HandleAfterTaskOperation();
            }
        }

        _startTime = null;
    }

    public MaaTasker? GetCurrentTasker(CancellationToken token = default)
    {
        var task = GetCurrentTaskerAsync(token);
        task.Wait(token);
        return task.Result;
    }

    public async Task<MaaTasker?> GetCurrentTaskerAsync(CancellationToken token = default)
    {
        _currentTasker ??= await InitializeMaaTasker(token);
        return _currentTasker;
    }

    public bool HasTasker()
    {
        return _currentTasker != null;
    }

    public void SetCurrentTasker(MaaTasker? tasker = null)
    {
        if (tasker == null)
        {
            _agentClient?.LinkStop();
            _agentClient?.Dispose();
            _agentClient = null;
            _agentStarted = false;
            _agentProcess?.Kill();
            _agentProcess?.Dispose();
            _agentProcess = null;
            try
            {
                _currentTasker?.Dispose();
            }
            catch
            {
                // ignored
            }
        }
        _currentTasker = tasker;
    }

    public static string HandleStringsWithVariables(string content)
    {
        try
        {
            return Regex.Replace(content, @"\{(\+\+|\-\-)?(\w+)(\+\+|\-\-)?([\+\-\*/]\w+)?\}", match =>
            {
                var prefix = match.Groups[1].Value;
                var counterKey = match.Groups[2].Value;
                var suffix = match.Groups[3].Value;
                var operation = match.Groups[4].Value;

                int value = AutoInitDictionary.GetValueOrDefault(counterKey, 0);

                // 前置操作符7
                if (prefix == "++")
                {
                    value = ++AutoInitDictionary[counterKey];
                }
                else if (prefix == "--")
                {
                    value = --AutoInitDictionary[counterKey];
                }

                // 后置操作符
                if (suffix == "++")
                {
                    value = AutoInitDictionary[counterKey]++;
                }
                else if (suffix == "--")
                {
                    value = AutoInitDictionary[counterKey]--;
                }

                // 算术操作
                if (!string.IsNullOrEmpty(operation))
                {
                    string operationType = operation[0].ToString();
                    string operandKey = operation.Substring(1);

                    if (AutoInitDictionary.TryGetValue(operandKey, out var operandValue))
                    {
                        value = operationType switch
                        {
                            "+" => value + operandValue,
                            "-" => value - operandValue,
                            "*" => value * operandValue,
                            "/" => value / operandValue,
                            _ => value
                        };
                    }
                }

                return value.ToString();
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            ErrorView.ShowException(e);
            return content;
        }
    }

    public static string FindPythonPath(string? program)
    {
        Console.WriteLine("Program:" + program);
        // 仅在程序为 "python" 且运行在 Windows 系统上时进行处理
        if (program != "python" || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return program;
        }

        // 检查 PATH 环境变量
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return program;
        }

        // 分割 PATH 并查找 python.exe
        var pathDirs = pathEnv.Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            try
            {
                var pythonPath = Path.Combine(dir, "python.exe");
                if (File.Exists(pythonPath))
                {
                    return pythonPath;
                }
            }
            catch
            {
                // 忽略无效路径
            }
        }

        // 尝试查找 Python 安装目录
        var pythonDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python")
        };

        foreach (var baseDir in pythonDirs)
        {
            if (Directory.Exists(baseDir))
            {
                try
                {
                    var pythonDir = Directory.GetDirectories(baseDir)
                        .OrderByDescending(d => d)
                        .FirstOrDefault();

                    if (pythonDir != null)
                    {
                        var pythonPath = Path.Combine(pythonDir, "python.exe");
                        if (File.Exists(pythonPath))
                        {
                            return pythonPath;
                        }
                    }
                }
                catch
                {
                    // 忽略错误
                }
            }
        }

        // 未找到，返回原程序名
        return program;
    }

    async private Task<MaaTasker?> InitializeMaaTasker(CancellationToken token) // 添加 async 和 token
    {

        AutoInitDictionary.Clear();
        LoggerService.LogInfo("LoadingResources".ToLocalization());
        RootView.AddLogByKey("LoadingResources");

        MaaResource? maaResource = null;
        try
        {
            var resources = Instances.GameSettingsUserControlModel.CurrentResources
                    .FirstOrDefault(c => c.Name == Instances.GameSettingsUserControlModel.CurrentResource)?.Path
                ?? [];
            LoggerService.LogInfo($"Resource: {string.Join(",", resources)}");

            if (resources.Count == 0 || resources.All(string.IsNullOrWhiteSpace))
            {
                HandleInitializationError(
                    new InvalidOperationException("Resource path list is empty"),
                    "LoadResourcesFailed".ToLocalization());
                return null;
            }

            maaResource = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return new MaaResource(resources);
            }, token, catchException: true, shouldLog: false, handleError: exception => HandleInitializationError(exception, "LoadResourcesFailed".ToLocalization()));

            // TaskManager 吞异常后返回 default；禁止继续对 null 调 API（否则会被外层空 catch 静默吃掉）
            if (maaResource == null)
                return null;

            maaResource.SetOption_InferenceDevice(Instances.PerformanceUserControlModel.GpuOption);
            LoggerService.LogInfo($"GPU acceleration: {Instances.PerformanceUserControlModel.GpuOption}");
            RootView.AddLogByKey("InitResourcesSuccess", BrushConverterHelper.ConvertToBrush("ForestGreen"));
        }
        catch (OperationCanceledException)
        {
            LoggerService.LogWarning("Resource loading was canceled");
            RootView.AddLog("Resource loading was canceled", BrushConverterHelper.ConvertToBrush("DarkOrange"));
            return null;
        }
        catch (Exception ex)
        {
            HandleInitializationError(ex, "LoadResourcesFailed".ToLocalization());
            return null;
        }

        // 初始化控制器部分同理
        MaaController? controller = null;
        try
        {
            RootView.AddLogByKey("LoadingController");
            controller = await TaskManager.RunTaskAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return InitializeController(Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb);
            }, token, catchException: true, shouldLog: false, handleError: exception => HandleInitializationError(exception,
                "ConnectingEmulatorOrWindow".ToLocalization()
                    .FormatWith(Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb
                        ? "Emulator".ToLocalization()
                        : "Window".ToLocalization()), true,
                "InitControllerFailed".ToLocalization(),
                showGrowl: true));

            if (controller == null)
                return null;

            RootView.AddLogByKey("InitControllerSuccess", BrushConverterHelper.ConvertToBrush("ForestGreen"));
        }
        catch (OperationCanceledException)
        {
            LoggerService.LogWarning("Controller initialization was canceled");
            RootView.AddLog("Controller initialization was canceled", BrushConverterHelper.ConvertToBrush("DarkOrange"));
            return null;
        }
        catch (Exception ex)
        {
            HandleInitializationError(ex, "InitControllerFailed".ToLocalization());
            return null;
        }
        try
        {
            token.ThrowIfCancellationRequested();

            var tasker = new MaaTasker
            {
                Controller = controller,
                Resource = maaResource,
                Utility = MaaUtility,
                Toolkit = MaaToolkit,
                DisposeOptions = DisposeOptions.All,
            };

            // 获取代理配置（假设MaaInterface.Instance在UI线程中访问）
            // 使用WPF日志框架记录（需实现ILogger接口）

            var agentConfig = MaaInterface.Instance?.Agent;
            if (agentConfig is { ChildExec: not null } && !_agentStarted)
            {
                RootView.AddLogByKey("StartingAgent");
                if (_agentClient != null)
                {
                    _agentClient.LinkStop();
                    _agentClient.Dispose();
                    _agentClient = null;
                    _agentProcess?.Kill();
                    _agentProcess?.Dispose();
                    _agentProcess = null;
                }

                var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
                var identifier = string.IsNullOrWhiteSpace(MaaInterface.Instance?.Agent?.Identifier) ? new string(Enumerable.Repeat(chars, 8).Select(c => c[Random.Next(c.Length)]).ToArray()) : MaaInterface.Instance.Agent.Identifier;
                LoggerService.LogInfo($"Agent Identifier: {identifier}");
                try
                {
                    _agentClient = MaaAgentClient.Create(identifier, maaResource);
                    if (_agentClient == null)
                    {
                        RootView.AddLogByKey("AgentStartFailed");
                        LoggerService.LogError($"Agent启动失败: agentClient 为 null");
                    }   
                    LoggerService.LogInfo($"Agent Client Hash: {_agentClient?.GetHashCode()}");
                    if (!Directory.Exists($"{AppContext.BaseDirectory}"))
                        Directory.CreateDirectory($"{AppContext.BaseDirectory}");
                    var program = MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory);
                    var args = $"{string.Join(" ", MaaInterface.ReplacePlaceholder(agentConfig.ChildArgs ?? Enumerable.Empty<string>(), AppContext.BaseDirectory))} {_agentClient?.Id}";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = FindPythonPath(program),
                        WorkingDirectory = AppContext.BaseDirectory,
                        Arguments = $"{(program.Contains("python") && args.Contains(".py") && !args.Contains("-u ") ? "-u " : "")}{args}",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };

                    _agentProcess = new Process
                    {
                        StartInfo = startInfo
                    };

                    _agentProcess.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            DispatcherHelper.RunOnMainThread(() =>
                            {
                                RootView.AddLog($"{args.Data}");
                            });
                        }
                    };

                    _agentProcess.ErrorDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrEmpty(args.Data))
                        {
                            DispatcherHelper.RunOnMainThread(() =>
                            {
                                RootView.AddLog($"{args.Data}");
                            });
                        }
                    };

                    _agentProcess.Start();
                    LoggerService.LogInfo(
                        $"Agent启动: {MaaInterface.ReplacePlaceholder(agentConfig.ChildExec, AppContext.BaseDirectory)} {string.Join(" ", MaaInterface.ReplacePlaceholder(agentConfig.ChildArgs ?? Enumerable.Empty<string>(), AppContext.BaseDirectory))} {_agentClient.Id} "
                        + $"socket_id: {_agentClient.Id}");
                    _agentProcess.BeginOutputReadLine();
                    _agentProcess.BeginErrorReadLine();

                    TaskManager.RunTaskAsync(async () => await _agentProcess.WaitForExitAsync(token), token);

                }
                catch (Exception ex)
                {
                    LoggerService.LogError($"Agent启动失败: {ex.Message}");
                    RootView.AddLogByKey("AgentStartFailed");
                    if (_agentClient != null)
                    {
                        _agentClient.LinkStop();
                        _agentClient.Dispose();
                        _agentClient = null;
                        _agentProcess?.Kill();
                        _agentProcess?.Dispose();
                        _agentProcess = null;
                    }
                    return null;
                }

                _agentClient?.LinkStart();
                _agentStarted = true;
            }
            RegisterCustomRecognitionsAndActions(tasker);
            Instances.ConnectingViewModel.SetConnected(true);
            tasker.Utility.SetOption_Recording(ConfigurationHelper.MaaConfig.GetConfig(ConfigurationKeys.Recording, false));
            tasker.Utility.SetOption_SaveDraw(ConfigurationHelper.MaaConfig.GetConfig(ConfigurationKeys.SaveDraw, false));
            tasker.Utility.SetOption_ShowHitDraw(ConfigurationHelper.MaaConfig.GetConfig(ConfigurationKeys.ShowHitDraw, false));
            return tasker;
        }
        catch (OperationCanceledException)
        {
            LoggerService.LogWarning("Tasker initialization was canceled");
            RootView.AddLog("Tasker initialization was canceled", BrushConverterHelper.ConvertToBrush("DarkOrange"));
            return null;
        }
        catch (Exception ex)
        {
            HandleInitializationError(ex, "InitInstanceFailed".ToLocalization());
            return null;
        }
    }

    // private async Task<MaaTasker?> InitializeMaaTasker(CancellationToken token)
    // {
    //     token.ThrowIfCancellationRequested();
    //     AutoInitDictionary.Clear();
    //
    //     LoggerService.LogInfo("LoadingResources".ToLocalization());
    //     MaaResource maaResource = null;
    //     try
    //     {
    //         var resources = Instances.GameSettingsUserControlModel.CurrentResources.FirstOrDefault(c => c.Name == Instances.GameSettingsUserControlModel.CurrentResource)?.Path ?? [];
    //         LoggerService.LogInfo($"Resource: {string.Join(",", resources)}");
    //         maaResource = new MaaResource(resources);
    //
    //         maaResource.SetOptionInferenceDevice(Instances.PerformanceUserControlModel.GpuOption);
    //         LoggerService.LogInfo($"GPU acceleration: {Instances.PerformanceUserControlModel.GpuOption}");
    //     }
    //     catch (Exception e)
    //     {
    //         HandleInitializationError(e, "LoadResourcesFailed".ToLocalization());
    //         return null;
    //     }
    //
    //     LoggerService.LogInfo("InitResourcesSuccess".ToLocalization());
    //     LoggerService.LogInfo("LoadingController".ToLocalization());
    //     MaaController controller = null;
    //     try
    //     {
    //         controller = InitializeController();
    //     }
    //     catch (Exception e)
    //     {
    //         HandleInitializationError(e,
    //             "ConnectingEmulatorOrWindow".ToLocalization()
    //                 .FormatWith(Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb
    //                     ? "Emulator".ToLocalization()
    //                     : "Window".ToLocalization()), true,
    //             "InitControllerFailed".ToLocalization());
    //         return null;
    //     }
    //
    //     LoggerService.LogInfo("InitControllerSuccess".ToLocalization());
    //
    //
    // }

    private MaaController? InitializeController(bool isAdb)
    {
        if (isAdb)
        {
            if (string.IsNullOrWhiteSpace(MaaFwConfiguration.AdbDevice.AdbSerial))
            {
                LoggerService.LogWarning("AdbSerial is empty or null, cannot initialize MaaAdbController.");
                return null;
            }

            LoggerService.LogInfo($"AdbPath: {MaaFwConfiguration.AdbDevice.AdbPath}");
            LoggerService.LogInfo($"AdbSerial: {MaaFwConfiguration.AdbDevice.AdbSerial}");
            LoggerService.LogInfo($"ScreenCap: {MaaFwConfiguration.AdbDevice.ScreenCap}");
            LoggerService.LogInfo($"Input: {MaaFwConfiguration.AdbDevice.Input}");
            LoggerService.LogInfo($"Config: {MaaFwConfiguration.AdbDevice.Config}");

            var adbPath = !string.IsNullOrWhiteSpace(MaaFwConfiguration.AdbDevice.AdbPath)
                ? MaaFwConfiguration.AdbDevice.AdbPath
                : "adb";
            var config = !string.IsNullOrWhiteSpace(MaaFwConfiguration.AdbDevice.Config)
                ? MaaFwConfiguration.AdbDevice.Config
                : "{}";

            return new MaaAdbController(
                adbPath,
                MaaFwConfiguration.AdbDevice.AdbSerial,
                MaaFwConfiguration.AdbDevice.ScreenCap,
                MaaFwConfiguration.AdbDevice.Input,
                config,
                Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary"));
        }
        else
        {
            if (MaaFwConfiguration.DesktopWindow.HWnd == nint.Zero)
            {
                LoggerService.LogWarning("DesktopWindow HWnd is 0, cannot initialize MaaWin32Controller.");
                return null;
            }

            LoggerService.LogInfo($"HWnd: {MaaFwConfiguration.DesktopWindow.HWnd}");
            LoggerService.LogInfo($"ScreenCap: {MaaFwConfiguration.DesktopWindow.ScreenCap}");
            LoggerService.LogInfo($"Input: {MaaFwConfiguration.DesktopWindow.Input}");
            LoggerService.LogInfo($"Link: {MaaFwConfiguration.DesktopWindow.Link}");
            LoggerService.LogInfo($"Check: {MaaFwConfiguration.DesktopWindow.Check}");

            return new MaaWin32Controller(
                MaaFwConfiguration.DesktopWindow.HWnd,
                MaaFwConfiguration.DesktopWindow.ScreenCap,
                MaaFwConfiguration.DesktopWindow.Input,
                MaaFwConfiguration.DesktopWindow.Link,
                MaaFwConfiguration.DesktopWindow.Check);
        }
    }

    private static List<MetadataReference>? _metadataReferences;

    private static List<MetadataReference> GetMetadataReferences()
    {
        if (_metadataReferences == null)
        {
            var domainAssemblys = AppDomain.CurrentDomain.GetAssemblies();
            _metadataReferences = new List<MetadataReference>();

            foreach (var assembly in domainAssemblys)
            {
                if (!assembly.IsDynamic)
                {
                    unsafe
                    {
                        assembly.TryGetRawMetadata(out byte* blob, out int length);
                        var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
                        var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
                        var metadataReference = assemblyMetadata.GetReference();
                        _metadataReferences.Add(metadataReference);
                    }
                }
            }

            unsafe
            {
                typeof(System.Linq.Expressions.Expression).Assembly.TryGetRawMetadata(out byte* blob, out int length);
                _metadataReferences.Add(AssemblyMetadata.Create(ModuleMetadata.CreateFromMetadata((IntPtr)blob, length)).GetReference());
            }
        }
        return _metadataReferences;
    }


    private static bool _shouldLoadCustomClasses = true;
    private static FileSystemWatcher? _watcher;
    private static void onFileChanged(object sender, FileSystemEventArgs e)
    {
        _shouldLoadCustomClasses = true;
    }
    private static IEnumerable<CustomValue<object>> LoadAndInstantiateCustomClasses(string directory, string[] interfacesToImplement)
    {
        var customClasses = new List<CustomValue<object>>();
        if (Path.Exists(directory))
        {
            if (_watcher == null)
            {
                _watcher = new FileSystemWatcher(directory);
                _watcher.Filter = "*.cs";
                _watcher.Changed += onFileChanged;
                _watcher.Created += onFileChanged;
                _watcher.Deleted += onFileChanged;
                _watcher.Renamed += onFileChanged;
                _watcher.EnableRaisingEvents = true;
            }

            var csFiles = Directory.GetFiles(directory, "*.cs");
            var references = GetMetadataReferences();

            // 同一目录下全部 .cs 一起编译，便于共享 helper（如 DawnRealmCharCatalog）
            var trees = new List<SyntaxTree>();
            var codeByPath = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var filePath in csFiles)
            {
                var name = Path.GetFileNameWithoutExtension(filePath);
                LoggerService.LogInfo("Trying to parse " + name);
                var code = File.ReadAllText(filePath);
                codeByPath[filePath] = code.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
                trees.Add(CSharpSyntaxTree.ParseText(code, path: filePath));
            }

            if (trees.Count == 0)
                return customClasses;

            var compilation = CSharpCompilation.Create("DynamicAssembly")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddSyntaxTrees(trees)
                .AddReferences(references);

            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);
            if (!result.Success)
            {
                var failures = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
                foreach (var diagnostic in failures)
                {
                    var lineInfo = diagnostic.Location.GetLineSpan().StartLinePosition;
                    var lineNumber = lineInfo.Line + 1;
                    var filePath = diagnostic.Location.SourceTree?.FilePath ?? "";
                    var lines = codeByPath.TryGetValue(filePath, out var arr) ? arr : [];
                    var errorLine = lineNumber <= lines.Length ? lines[lineNumber - 1].Trim() : "无法获取对应代码行（行号超出范围）";
                    var fileName = string.IsNullOrEmpty(filePath) ? "?" : Path.GetFileName(filePath);
                    LoggerService.LogError($"{diagnostic.Id}: {diagnostic.GetMessage()}  [{fileName}:{lineNumber}]  [错误代码行: {errorLine}]");
                }
                return customClasses;
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());
            var instances =
                from type in assembly.GetTypes()
                from iface in interfacesToImplement
                where type.GetInterfaces().Any(i => i.Name == iface)
                let instance = Activator.CreateInstance(type)
                where instance != null
                select new CustomValue<object>(type.Name, instance);
            customClasses.AddRange(instances);
        }
        _shouldLoadCustomClasses = false;
        return customClasses;
    }

    private static IEnumerable<CustomValue<object>>? _customClasses;
    private static IEnumerable<CustomValue<object>> GetCustomClasses(string directory, string[] interfacesToImplement)
    {
        if (_customClasses == null || _shouldLoadCustomClasses)
            _customClasses = LoadAndInstantiateCustomClasses(directory, interfacesToImplement);
        else
            _customClasses.ForEach(value => LoggerService.LogInfo($"Trying to loading {value.Name}"));
        return _customClasses;
    }

    private void RegisterCustomRecognitionsAndActions(MaaTasker instance)
    {
        if (MaaInterface.Instance == null) return;
        LoggerService.LogInfo("RegisteringCustomRecognizer".ToLocalization());
        LoggerService.LogInfo("RegisteringCustomAction".ToLocalization());
        // instance.Resource.Register(new MoneyDetectRecognition());
        // instance.Resource.Register(new MoneyRecognition());
        var customClasses = GetCustomClasses($"{Resource}/custom", [
            "IMaaCustomRecognition",
            "IMaaCustomAction",
        ]);

        foreach (var customClass in customClasses)
        {
            if (customClass.Value is IMaaCustomRecognition recognition)
            {
                instance.Resource.Register(recognition);
                LoggerService.LogInfo("Registering IMaaCustomRecognition " + customClass.Name);
            }
            else if (customClass.Value is IMaaCustomAction action)
            {
                instance.Resource.Register(action);
                LoggerService.LogInfo("Registering IMaaCustomAction " + customClass.Name);
            }
        }
        instance.Callback += (_, args) =>
        {
            try
            {
                var jObject = JObject.Parse(string.IsNullOrWhiteSpace(args.Details) ? "{}" : args.Details);
                var name = jObject["name"]?.ToString()
                           ?? jObject["task"]?.ToString()
                           ?? string.Empty;
                var entry = jObject["entry"]?.ToString() ?? name;

                if (args.Message.StartsWith(MaaMsg.Node.Action.Prefix))
                {
                    if (jObject.ContainsKey("focus"))
                    {
                        var maaNode = jObject.ToObject<TaskModel>();
                        if (maaNode != null)
                            DisplayFocus(maaNode, args.Message);
                    }

                    if (string.IsNullOrWhiteSpace(name))
                        return;

                    if (args.Message == MaaMsg.Node.Action.Starting)
                    {
                        RootView.AddLogByKey("NodeActionStarting", null, false, name);
                    }
                    else if (args.Message == MaaMsg.Node.Action.Succeeded)
                    {
                        // 成功节点较多，仅写文件日志，避免刷屏
                        LoggerService.LogInfo($"节点成功: {name}");
                    }
                    else if (args.Message == MaaMsg.Node.Action.Failed)
                    {
                        RootView.AddLogByKey("NodeActionFailed", BrushConverterHelper.ConvertToBrush("OrangeRed"), false, name);
                    }
                }
                else if (args.Message.Contains("Tasker.Task.Starting", StringComparison.Ordinal))
                {
                    RootView.AddLogByKey("PipelineTaskStarting", null, false, entry);
                    LoggerService.LogInfo($"Pipeline starting: {entry}");
                }
                else if (args.Message.Contains("Tasker.Task.Succeeded", StringComparison.Ordinal))
                {
                    RootView.AddLogByKey("PipelineTaskSucceeded", BrushConverterHelper.ConvertToBrush("ForestGreen"), false, entry);
                    LoggerService.LogInfo($"Pipeline succeeded: {entry}");
                }
                else if (args.Message.Contains("Tasker.Task.Failed", StringComparison.Ordinal))
                {
                    RootView.AddLogByKey("PipelineTaskFailed", BrushConverterHelper.ConvertToBrush("OrangeRed"), false, entry);
                    LoggerService.LogWarning($"Pipeline failed: {entry} | {args.Details}");
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Maa callback log error: {ex.Message}");
            }
        };
    }

    private class Focus
    {
        [JsonConverter(typeof(SingleOrListConverter))] [JsonProperty("start")]
        public List<string>? Start;
        [JsonConverter(typeof(SingleOrListConverter))] [JsonProperty("succeeded")]
        public List<string>? Succeeded;
        [JsonConverter(typeof(SingleOrListConverter))] [JsonProperty("failed")]
        public List<string>? Failed;
        [JsonConverter(typeof(SingleOrListConverter))] [JsonProperty("toast")]
        public List<string>? Toast;
    }

    public static (string Text, string? Color) ParseColorText(string input)
    {
        var match = Regex.Match(input.Trim(), @"\[color:(?<color>.*?)\](?<text>.*?)\[/color\]", RegexOptions.IgnoreCase);

        if (match.Success)
        {
            string color = match.Groups["color"].Value.Trim();
            string text = match.Groups["text"].Value;
            return (text, color);
        }

        return (input, null);

    }

    private void DisplayFocus(TaskModel taskModel, string message)
    {
        var jToken = JToken.FromObject(taskModel.Focus);
        var focus = new Focus();
        if (jToken.Type == JTokenType.String)
            focus.Start = [jToken.Value<string>()];

        if (jToken.Type == JTokenType.Object)
            focus = jToken.ToObject<Focus>();
        switch (message)
        {
            case MaaMsg.Node.Action.Succeeded:
                if (focus.Succeeded != null)
                {
                    foreach (var line in focus.Succeeded)
                    {
                        var (text, color) = ParseColorText(line);
                        RootView.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushConverterHelper.ConvertToBrush(color));
                    }
                }
                break;
            case MaaMsg.Node.Action.Failed:
                if (focus.Failed != null)
                {
                    foreach (var line in focus.Failed)
                    {
                        var (text, color) = ParseColorText(line);
                        RootView.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushConverterHelper.ConvertToBrush(color));
                    }
                }
                break;
            case MaaMsg.Node.Action.Starting:
                if (focus.Toast is { Count: > 0 })
                {
                    var (text, color) = ParseColorText(focus.Toast[0]);
                    ToastNotification.ShowDirect(HandleStringsWithVariables(text));
                }
                if (focus.Start != null)
                {
                    foreach (var line in focus.Start)
                    {
                        var (text, color) = ParseColorText(line);
                        RootView.AddLog(HandleStringsWithVariables(text), color == null ? null : BrushConverterHelper.ConvertToBrush(color));
                    }
                }
                break;
        }
    }

    // private void DisplayFocus(TaskModel taskModel, string message)
    // {
    //     switch (message)
    //     {
    //         case MaaMsg.Node.Action.Succeeded:
    //             if (taskModel.FocusSucceeded != null)
    //             {
    //                 for (int i = 0; i < taskModel.FocusSucceeded.Count; i++)
    //                 {
    //                     Brush brush = null;
    //                     var tip = taskModel.FocusSucceeded[i];
    //                     try
    //                     {
    //                         if (taskModel.FocusSucceededColor != null && taskModel.FocusSucceededColor.Count > i)
    //                             brush = BrushConverterHelper.ConvertToBrush(taskModel.FocusSucceededColor[i]);
    //                     }
    //                     catch (Exception e)
    //                     {
    //                         LoggerService.LogError(e);
    //                     }
    //
    //                     RootView.AddLog(HandleStringsWithVariables(tip), brush);
    //                 }
    //             }
    //             break;
    //         case MaaMsg.Node.Action.Failed:
    //             if (taskModel.FocusFailed != null)
    //             {
    //                 for (int i = 0; i < taskModel.FocusFailed.Count; i++)
    //                 {
    //                     Brush brush = null;
    //                     var tip = taskModel.FocusFailed[i];
    //                     try
    //                     {
    //                         if (taskModel.FocusFailedColor != null && taskModel.FocusFailedColor.Count > i)
    //                             brush = BrushConverterHelper.ConvertToBrush(taskModel.FocusFailedColor[i]);
    //                     }
    //                     catch (Exception e)
    //                     {
    //                         LoggerService.LogError(e);
    //                     }
    //
    //                     RootView.AddLog(HandleStringsWithVariables(tip), brush);
    //                 }
    //             }
    //             break;
    //         case MaaMsg.Node.Action.Starting:
    //             if (!string.IsNullOrWhiteSpace(taskModel.FocusToast))
    //             {
    //                 ToastNotification.ShowDirect(taskModel.FocusToast);
    //             }
    //             if (taskModel.FocusTip != null)
    //             {
    //                 for (int i = 0; i < taskModel.FocusTip.Count; i++)
    //                 {
    //                     Brush? brush = null;
    //                     var tip = taskModel.FocusTip[i];
    //                     try
    //                     {
    //                         if (taskModel.FocusTipColor != null && taskModel.FocusTipColor.Count > i)
    //                         {
    //                             brush = BrushConverterHelper.ConvertToBrush(taskModel.FocusTipColor[i]);
    //                         }
    //                     }
    //                     catch (Exception e)
    //                     {
    //                         Console.WriteLine(e);
    //                         LoggerService.LogError(e);
    //                     }
    //
    //                     RootView.AddLog(HandleStringsWithVariables(tip), brush);
    //                 }
    //             }
    //             break;
    //     }
    //
    // }

    /// <summary>
    /// 初始化失败统一出口：文件日志 + UI 日志 +（可选）Growl，禁止静默吞掉。
    /// </summary>
    private void HandleInitializationError(Exception e,
        string message,
        bool hasWarning = false,
        string waringMessage = "",
        bool showGrowl = true)
    {
        Console.WriteLine(e);
        LoggerService.LogError(e.ToString());
        if (hasWarning && !string.IsNullOrWhiteSpace(waringMessage))
            LoggerService.LogWarning(waringMessage);

        var detail = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
        var uiLine = string.IsNullOrWhiteSpace(message) ? detail : $"{message} ({detail})";
        RootView.AddLog(uiLine, BrushConverterHelper.ConvertToBrush("OrangeRed"));
        if (hasWarning && !string.IsNullOrWhiteSpace(waringMessage))
            RootView.AddLog(waringMessage, BrushConverterHelper.ConvertToBrush("DarkOrange"));

        if (showGrowl)
            GrowlHelper.Error(message);
    }

    public BitmapImage? GetBitmapImage()
    {
        using var buffer = GetImage(GetCurrentTasker()?.Controller);

        if (!buffer.TryGetEncodedData(out Stream? stream))
        {
            GrowlHelper.ErrorGlobal("Handle为空！");
            return null;
        }

        return CreateBitmapImage(stream);
    }

    private static BitmapImage CreateBitmapImage(byte[] imageData)
    {
        var bitmapImage = new BitmapImage();
        using (var ms = new MemoryStream(imageData))
        {
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
        }

        bitmapImage.Freeze();
        return bitmapImage;
    }

    private static BitmapImage CreateBitmapImage(Stream stream)
    {
        var bitmapImage = new BitmapImage();

        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();


        bitmapImage.Freeze();
        return bitmapImage;
    }

    private static MaaImageBuffer? GetImage(IMaaController? maaController)
    {
        var buffer = new MaaImageBuffer();
        if (maaController == null)
            return buffer;
        var status = maaController.Screencap().Wait();
        Console.WriteLine(status);
        if (status != MaaJobStatus.Succeeded)
            return buffer;
        maaController.GetCachedImage(buffer);
        return buffer;
    }

    public async Task RestartAdb()
    {
        var adbPath = MaaFwConfiguration.AdbDevice.AdbPath;

        if (string.IsNullOrEmpty(adbPath))
        {
            return;
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        Process process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} kill-server");
        await process.StandardInput.WriteLineAsync($"{adbPath} start-server");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    public async Task ReconnectByAdb()
    {
        var adbPath = MaaFwConfiguration.AdbDevice.AdbPath;
        var address = MaaFwConfiguration.AdbDevice.AdbSerial;

        if (string.IsNullOrEmpty(adbPath) || adbPath == "adb" || string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        var process = new Process
        {
            StartInfo = processStartInfo,
        };

        process.Start();
        await process.StandardInput.WriteLineAsync($"{adbPath} disconnect {address}");
        await process.StandardInput.WriteLineAsync("exit");
        await process.WaitForExitAsync();
    }

    public async Task HardRestartAdb()
    {
        var adbPath = MaaFwConfiguration.AdbDevice.AdbPath;
        if (string.IsNullOrEmpty(adbPath))
        {
            return;
        }

        // This allows for SQL injection, but since it is not on a real database nothing horrible would happen.
        // The following query string does what I want, but WMI does not accept it.
        // var wmiQueryString = string.Format("SELECT ProcessId, CommandLine FROM Win32_Process WHERE ExecutablePath='{0}'", adbPath);
        const string WmiQueryString = "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process";
        using var searcher = new ManagementObjectSearcher(WmiQueryString);
        using var results = searcher.Get();
        var query = from p in Process.GetProcesses()
                    join mo in results.Cast<ManagementObject>()
                        on p.Id equals (int)(uint)mo["ProcessId"]
                    select new
                    {
                        Process = p,
                        Path = (string)mo["ExecutablePath"],
                    };
        foreach (var item in query)
        {
            if (item.Path != adbPath)
            {
                continue;
            }

            // Some emulators start their ADB with administrator privilege.
            // Not sure if this is necessary
            try
            {
                item.Process.Kill();
                await item.Process.WaitForExitAsync();
            }
            catch
            {
                // ignored
            }
        }

    }

    public async Task TestConnecting()
    {
        var tasker = await GetCurrentTaskerAsync();
        if (tasker?.Controller == null)
        {
            Instances.ConnectingViewModel.SetConnected(false);
            // 初始化阶段若已写过失败日志，这里仍要抛出，避免 MFATask 把“连接检测”记成成功
            throw new InvalidOperationException("InitInstanceFailed".ToLocalization());
        }

        var job = tasker.Controller.LinkStart();
        job.Wait();
        var ok = job.Status == MaaJobStatus.Succeeded;
        Instances.ConnectingViewModel.SetConnected(ok);
        Console.WriteLine($"测试连接: {job.Status}");

        if (!ok)
        {
            RootView.AddLogByKey("ConnectFailed", BrushConverterHelper.ConvertToBrush("OrangeRed"));
            throw new InvalidOperationException("ConnectFailed".ToLocalization());
        }

        var isAdb = Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb;
        RootView.AddLogByKey("ConnectingSuccess", BrushConverterHelper.ConvertToBrush("ForestGreen"), true,
            isAdb ? "Emulator" : "Window");
    }
}

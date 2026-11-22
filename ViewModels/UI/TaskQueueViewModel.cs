using CommunityToolkit.Mvvm.ComponentModel;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;
using MFAWPF.ViewModels.Tool;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;


namespace MFAWPF.ViewModels.UI;

public partial class TaskQueueViewModel : ViewModel
{
    [ObservableProperty] private ObservableCollection<Tool.DragItemViewModel> _taskItemViewModels = new();

    partial void OnTaskItemViewModelsChanged(ObservableCollection<Tool.DragItemViewModel>? oldValue, ObservableCollection<Tool.DragItemViewModel> newValue)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, newValue.ToList().Select(model => model.InterfaceItem));
    }

    private bool _reorderingCheckedTasks;

    /// <summary>
    /// 已勾选任务按 interface.json 原顺序加入执行列表。
    /// </summary>
    public List<DragItemViewModel> GetCheckedTasksInOriginalOrder()
    {
        var orderMap = BuildOriginalOrderMap();
        return TaskItemViewModels
            .Where(task => task.IsChecked || task.IsCheckedWithNull == null)
            .OrderBy(item => RankByOriginalOrder(item, orderMap))
            .ToList();
    }

    /// <summary>
    /// 界面：已勾选置顶，关闭/未勾选沉底；组内按 interface 原顺序。
    /// </summary>
    public void ReorderCheckedTasksToTop()
    {
        if (_reorderingCheckedTasks || _suppressCheckedReorder || IsPresetMode)
            return;

        DispatcherHelper.RunOnMainThread(() =>
        {
            if (_reorderingCheckedTasks || _suppressCheckedReorder || IsPresetMode || TaskItemViewModels.Count == 0)
                return;

            var orderMap = BuildOriginalOrderMap();
            var ordered = TaskItemViewModels
                .OrderBy(item => CheckSortRank(item.IsCheckedWithNull))
                .ThenBy(item => RankByOriginalOrder(item, orderMap))
                .ToList();

            var unchanged = ordered.Count == TaskItemViewModels.Count
                            && ordered.Zip(TaskItemViewModels, (a, b) => ReferenceEquals(a, b)).All(eq => eq);
            if (unchanged)
                return;

            _reorderingCheckedTasks = true;
            try
            {
                TaskItemViewModels = new ObservableCollection<DragItemViewModel>(ordered);
            }
            finally
            {
                _reorderingCheckedTasks = false;
            }
        });
    }

    /// <summary>批量改勾选时暂停自动重排，结束后再统一沉底。</summary>
    public void RunWithSuppressedReorder(Action action)
    {
        _suppressCheckedReorder = true;
        try
        {
            action();
        }
        finally
        {
            _suppressCheckedReorder = false;
            ReorderCheckedTasksToTop();
        }
    }

    /// <summary>true 置顶，null 其次，false（关闭）最后。</summary>
    private static int CheckSortRank(bool? check) => check switch
    {
        true => 0,
        null => 1,
        false => 2
    };

    private bool _suppressCheckedReorder;

    private static int RankByOriginalOrder(DragItemViewModel item, Dictionary<string, int> orderMap)
    {
        var key = item.InterfaceItem?.Entry ?? item.Name ?? string.Empty;
        return orderMap.TryGetValue(key, out var index) ? index : int.MaxValue;
    }

    private Dictionary<string, int> BuildOriginalOrderMap()
    {
        var orderMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var interfaceTasks = MaaInterface.Instance?.Task;
        if (interfaceTasks is { Count: > 0 })
        {
            for (var i = 0; i < interfaceTasks.Count; i++)
            {
                var key = interfaceTasks[i].Entry ?? interfaceTasks[i].Name;
                if (!string.IsNullOrEmpty(key) && !orderMap.ContainsKey(key))
                    orderMap[key] = i;
            }

            return orderMap;
        }

        for (var i = 0; i < TasksSource.Count; i++)
        {
            var key = TasksSource[i].InterfaceItem?.Entry ?? TasksSource[i].Name;
            if (!string.IsNullOrEmpty(key) && !orderMap.ContainsKey(key))
                orderMap[key!] = i;
        }

        return orderMap;
    }

    [ObservableProperty] private bool _connectSettingChecked = true;
    [ObservableProperty] private bool _isPresetMode;
    [ObservableProperty] private string? _activePresetName;

    /// <summary>
    /// Applies a task sequence preset to the home task queue.
    /// </summary>
    public void ApplyPresetSequence(IEnumerable<Models.TaskPresetItem> sequence, string? presetName = null)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            var orderedPresetItems = sequence
                .Where(s => !string.IsNullOrWhiteSpace(s.TaskName) && s.Order > 0)
                .OrderBy(s => s.Order)
                .ToList();

            if (orderedPresetItems.Count == 0)
            {
                return;
            }

            IsPresetMode = true;
            ActivePresetName = presetName;

            // If queue is empty, populate from TasksSource
            if (!TaskItemViewModels.Any() && TasksSource.Any())
            {
                foreach (var sourceItem in TasksSource)
                {
                    TaskItemViewModels.Add(sourceItem.Clone());
                }
            }

            var usedPresetItems = new HashSet<Models.TaskPresetItem>();

            // Update existing items in place, preserving their existing queue positions
            foreach (var item in TaskItemViewModels)
            {
                var matchedPreset = orderedPresetItems.FirstOrDefault(p =>
                    !usedPresetItems.Contains(p) && IsTaskMatch(item, p.TaskName));

                if (matchedPreset != null)
                {
                    item.SequenceOrder = matchedPreset.Order;
                    item.IsSequenceBadgeVisible = true;
                    item.IsCheckedWithNull = true;
                    usedPresetItems.Add(matchedPreset);
                }
                else
                {
                    item.SequenceOrder = null;
                    item.IsSequenceBadgeVisible = false;
                    item.IsCheckedWithNull = false; // Unselected tasks maintain empty box style
                }
            }

            // Append any preset tasks that were not yet present in the queue
            foreach (var presetItem in orderedPresetItems)
            {
                if (!usedPresetItems.Contains(presetItem))
                {
                    var sourceMatch = TasksSource.FirstOrDefault(s => IsTaskMatch(s, presetItem.TaskName));
                    var item = sourceMatch != null
                        ? sourceMatch.Clone()
                        : new Tool.DragItemViewModel(new Helper.ValueType.TaskInterfaceItem { Name = presetItem.TaskName });

                    item.SequenceOrder = presetItem.Order;
                    item.IsSequenceBadgeVisible = true;
                    item.IsCheckedWithNull = true;
                    TaskItemViewModels.Add(item);
                    usedPresetItems.Add(presetItem);
                }
            }

            ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        });
    }

    private static bool IsTaskMatch(Tool.DragItemViewModel item, string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName)) return false;
        var trimmed = taskName.Trim();
        return string.Equals(item.InterfaceItem?.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.InterfaceItem?.Entry?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Exits preset mode and clears sequence badges.
    /// </summary>
    public void ExitPresetMode(bool cancelPresetSelections = false, Tool.DragItemViewModel? keepSelectedTask = null)
    {
        if (!IsPresetMode) return;

        IsPresetMode = false;
        ActivePresetName = null;

        foreach (var item in TaskItemViewModels)
        {
            item.SequenceOrder = null;
            item.IsSequenceBadgeVisible = false;

            if (cancelPresetSelections)
            {
                item.IsCheckedWithNull = (keepSelectedTask != null && item == keepSelectedTask);
            }
        }

        ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        ReorderCheckedTasksToTop();
    }


    public ObservableCollection<Tool.DragItemViewModel> TasksSource { get; private set; } =
        [];

    public ObservableCollection<Tool.LocalizationViewModel> BeforeTaskList =>
    [
        new("None"),
        new("StartupSoftware"),
        new("StartupSoftwareAndScript"),
    ];


    public ObservableCollection<Tool.LocalizationViewModel> AfterTaskList =>
    [
        new("None"),
        new("CloseMFA"),
        new("CloseEmulator"),
        new("CloseEmulatorAndMFA"),
        new("ShutDown"),
        new("CloseEmulatorAndRestartMFA"),
        new("RestartPC"),
    ];


    [ObservableProperty] private string? _beforeTask = ConfigurationHelper.GetValue(ConfigurationKeys.BeforeTask, "None");

    partial void OnBeforeTaskChanged(string? value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.BeforeTask, value);
    }

    [ObservableProperty] private string? _afterTask = ConfigurationHelper.GetValue(ConfigurationKeys.AfterTask, "None");

    partial void OnAfterTaskChanged(string? value)
    {
        ConfigurationHelper.SetValue(ConfigurationKeys.AfterTask, value);
    }

    public GongSolutions.Wpf.DragDrop.IDropTarget DropHandler { get; } = new DragDropHandler();

    public ObservableCollection<LogItemViewModel> LogItemViewModels { get; } = new();

    public static string FormatFileSize(long size)
    {
        string unit;
        double value;
        if (size >= 1024L * 1024 * 1024 * 1024)
        {
            value = (double)size / (1024L * 1024 * 1024 * 1024);
            unit = "TB";
        }
        else if (size >= 1024 * 1024 * 1024)
        {
            value = (double)size / (1024 * 1024 * 1024);
            unit = "GB";
        }
        else if (size >= 1024 * 1024)
        {
            value = (double)size / (1024 * 1024);
            unit = "MB";
        }
        else if (size >= 1024)
        {
            value = (double)size / 1024;
            unit = "KB";
        }
        else
        {
            value = size;
            unit = "B";
        }

        return $"{value:F} {unit}";
    }

    public static string FormatDownloadSpeed(double speed)
    {
        string unit;
        double value = speed;
        if (value >= 1024L * 1024 * 1024 * 1024)
        {
            value /= 1024L * 1024 * 1024 * 1024;
            unit = "TB/s";
        }
        else if (value >= 1024L * 1024 * 1024)
        {
            value /= 1024L * 1024 * 1024;
            unit = "GB/s";
        }
        else if (value >= 1024 * 1024)
        {
            value /= 1024 * 1024;
            unit = "MB/s";
        }
        else if (value >= 1024)
        {
            value /= 1024;
            unit = "KB/s";
        }
        else
        {
            unit = "B/s";
        }

        return $"{value:F} {unit}";
    }
    public void OutputDownloadProgress(long value = 0, long maximum = 1, int len = 0, double ts = 1)
    {
        string sizeValueStr = FormatFileSize(value);
        string maxSizeValueStr = FormatFileSize(maximum);
        string speedValueStr = FormatDownloadSpeed(len / ts);

        string progressInfo = $"[{sizeValueStr}/{maxSizeValueStr}({100 * value / maximum}%) {speedValueStr}]";
        OutputDownloadProgress(progressInfo);
    }
    public void ClearDownloadProgress()
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            if (LogItemViewModels.Count > 0 && LogItemViewModels[0].IsDownloading)
            {
                LogItemViewModels.RemoveAt(0);
            }
        });
    }
    public void OutputDownloadProgress(string output, bool downloading = true)
    {

        DispatcherHelper.RunOnMainThread(() =>
        {
            var log = new LogItemViewModel(downloading ? "NewVersionFoundDescDownloading".ToLocalization() + "\n" + output : output, Instances.RootView.FindResource("DownloadLogBrush") as Brush,
                dateFormat: "HH':'mm':'ss")
            {
                IsDownloading = true,
            };
            if (LogItemViewModels.Count > 0 && LogItemViewModels[0].IsDownloading)
            {
                if (!string.IsNullOrEmpty(output))
                {
                    LogItemViewModels[0] = log;
                }
                else
                {
                    LogItemViewModels.RemoveAt(0);
                }
            }
            else if (!string.IsNullOrEmpty(output))
            {
                LogItemViewModels.Insert(0, log);
            }
        });
    }

    public void AddLog(string content,
        string color = "",
        string weight = "Regular",
        bool showTime = true)
    {

        var brush = new BrushConverter().ConvertFromString(color) as SolidColorBrush;
        brush ??= Brushes.Gray;
        Task.Run(() =>
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                LogItemViewModels.Add(new LogItemViewModel(content, brush, weight, "HH':'mm':'ss",
                    showTime: showTime));
                LoggerService.LogInfo(content);
            });
        });
    }
    public const string INFO = "info:";
    public const string ERROR = "err:";
    public const string WARN = "warn:";
    public void AddLog(string content,
        Brush? brush = null,
        string weight = "Regular",
        bool showTime = true)
    {
        brush ??= Brushes.Gray;
        var changeColor = true;
        if (content.StartsWith(INFO))
        {
            brush = Brushes.Gray;
            content = content.Substring(INFO.Length);
        }
        if (content.StartsWith(WARN))
        {
            brush = Brushes.Orange;
            content = content.Substring(WARN.Length);
        }
        if (content.StartsWith(ERROR))
        {
            brush = Brushes.OrangeRed;
            content = content.Substring(ERROR.Length);
        }
        Task.Run(() =>
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                LogItemViewModels.Add(new LogItemViewModel(content, brush, weight, "HH':'mm':'ss",
                    showTime: showTime));
                LoggerService.LogInfo(content);
            });
        });
    }

    public LogItemViewModel AddCountdownLog(string content, Brush? brush = null)
    {
        brush ??= Brushes.Gray;
        var item = new LogItemViewModel(content, brush, "Regular", "HH':'mm':'ss", showTime: true);
        DispatcherHelper.RunOnMainThread(() =>
        {
            LogItemViewModels.Add(item);
            LoggerService.LogInfo(content);
        });
        return item;
    }

    public void UpdateLog(LogItemViewModel item, string newContent)
    {
        DispatcherHelper.RunOnMainThread(() =>
        {
            item.Content = newContent;
        });
    }

    public void AddLogByKey(string key, Brush? color = null, params string[] formatArgsKeys)
    {
        color ??= Brushes.Gray;
        Task.Run(() =>
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                LogItemViewModels.Add(new LogItemViewModel(key, color, "Regular", true, "HH':'mm':'ss",
                    true, formatArgsKeys));

                var content = string.Empty;
                if (formatArgsKeys.Length == 0)
                    content = key.ToLocalization();
                else
                {
                    try
                    {
                        content = Regex.Unescape(
                            key.ToLocalizationFormatted(true, formatArgsKeys));
                    }
                    catch
                    {
                        content = key.ToLocalizationFormatted(true, formatArgsKeys);
                    }
                }
                LoggerService.LogInfo(content);
            });
        });
    }

    public void AddLogByKey(string key, Brush? color = null, bool transformKey = true, params string[] formatArgsKeys)
    {
        color ??= Brushes.Gray;
        Task.Run(() =>
        {
            DispatcherHelper.RunOnMainThread(() =>
            {
                LogItemViewModels.Add(new LogItemViewModel(key, color, "Regular", true, "HH':'mm':'ss",
                    true, transformKey, formatArgsKeys));

                var content = string.Empty;
                if (formatArgsKeys.Length == 0)
                    content = key.ToLocalization();
                else
                {
                    try
                    {
                        content = Regex.Unescape(
                            key.ToLocalizationFormatted(false, formatArgsKeys));
                    }
                    catch
                    {
                        content = key.ToLocalizationFormatted(false, formatArgsKeys);
                    }
                }
                LoggerService.LogInfo(content);
            });
        });
    }
}

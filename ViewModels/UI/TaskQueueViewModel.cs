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

            var currentItems = TaskItemViewModels.ToList();
            var allSourceItems = TasksSource.ToList();

            var newOrderedList = new List<Tool.DragItemViewModel>();
            var usedItems = new HashSet<Tool.DragItemViewModel>();

            // 1. Add preset tasks in exact sequence order (1, 2, 3...)
            foreach (var presetItem in orderedPresetItems)
            {
                var match = currentItems.FirstOrDefault(item =>
                    !usedItems.Contains(item) && IsTaskMatch(item, presetItem.TaskName));

                if (match == null)
                {
                    var sourceMatch = allSourceItems.FirstOrDefault(item =>
                        !usedItems.Contains(item) && IsTaskMatch(item, presetItem.TaskName));
                    if (sourceMatch != null)
                    {
                        match = sourceMatch.Clone();
                    }
                }

                if (match != null)
                {
                    match.SequenceOrder = presetItem.Order;
                    match.IsSequenceBadgeVisible = true;
                    match.IsCheckedWithNull = true;
                    newOrderedList.Add(match);
                    usedItems.Add(match);
                }
            }

            // 2. Append remaining unassigned tasks that were already in the queue
            foreach (var item in currentItems)
            {
                if (!usedItems.Contains(item))
                {
                    item.SequenceOrder = null;
                    item.IsSequenceBadgeVisible = false;
                    item.IsCheckedWithNull = false;
                    newOrderedList.Add(item);
                    usedItems.Add(item);
                }
            }

            // 3. Also append any remaining tasks from allSourceItems that are not yet in usedItems
            foreach (var sourceItem in allSourceItems)
            {
                var sourceKey = sourceItem.InterfaceItem?.Name ?? sourceItem.Name;
                if (!usedItems.Any(u => IsTaskMatch(u, sourceKey)))
                {
                    var clone = sourceItem.Clone();
                    clone.SequenceOrder = null;
                    clone.IsSequenceBadgeVisible = false;
                    clone.IsCheckedWithNull = false;
                    newOrderedList.Add(clone);
                    usedItems.Add(clone);
                }
            }

            // 4. Update the observable collection in-place so listeners remain attached and UI re-renders cleanly
            TaskItemViewModels.Clear();
            foreach (var item in newOrderedList)
            {
                TaskItemViewModels.Add(item);
            }

            ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        });
    }

    private static bool IsTaskMatch(Tool.DragItemViewModel item, string taskName)
    {
        return string.Equals(item.InterfaceItem?.Name, taskName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.Name, taskName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.InterfaceItem?.Entry, taskName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Exits preset mode and clears sequence badges.
    /// </summary>
    public void ExitPresetMode()
    {
        if (!IsPresetMode) return;

        IsPresetMode = false;
        ActivePresetName = null;

        foreach (var item in TaskItemViewModels)
        {
            item.SequenceOrder = null;
        }
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

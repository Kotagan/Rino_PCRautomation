using MaaFramework.Binding;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;
using MFAWPF.Helper.ValueType;
using MFAWPF.ViewModels.UI;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WPFLocalizeExtension.Extensions;
using static MFAWPF.Views.UI.RootView;
using DragItemViewModel = MFAWPF.ViewModels.Tool.DragItemViewModel;
using ScrollViewer = HandyControl.Controls.ScrollViewer;


namespace MFAWPF.Views.UI;

public partial class TaskQueueView
{
    public TaskQueueViewModel ViewModel { get; set; }

    public TaskQueueView()
    {
        DataContext = this;
        ViewModel = Instances.TaskQueueViewModel;
        InitializeComponent();
        VirtualizingPanel.SetCacheLengthUnit(TaskList, VirtualizationCacheLengthUnit.Page);
        VirtualizingPanel.SetCacheLength(TaskList, new VirtualizationCacheLength(1));
    }

    public bool InitializeData(Collection<DragItemViewModel>? dragItem = null)
    {
        var (name, version, customTitle) = MaaInterface.Check();
        if (!string.IsNullOrWhiteSpace(name))
            Instances.RootViewModel.ShowResourceName(name);
        if (!string.IsNullOrWhiteSpace(version))
        {
            Instances.RootViewModel.ShowResourceVersion(version);
            Instances.VersionUpdateSettingsUserControlModel.ResourceVersion = !string.IsNullOrWhiteSpace(MaaInterface.Instance?.ResourceVersion)
                ? MaaInterface.Instance.ResourceVersion
                : version;
        }
        if (!string.IsNullOrWhiteSpace(customTitle))
            Instances.RootViewModel.ShowCustomTitle(customTitle);

        if (MaaInterface.Instance != null)
        {
            AppendVersionLog(MaaInterface.Instance.Version);
            Instances.TaskQueueViewModel.TasksSource.Clear();
            LoadTasks(MaaInterface.Instance.Task ?? new List<TaskInterfaceItem>(), dragItem);
        }

        ConnectToMAA();

        return LoadTask();
    }

    private bool LoadTask()
    {
        try
        {
            int filesCount = 0;
            if (Instances.GameSettingsUserControlModel.CurrentResources.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(Instances.GameSettingsUserControlModel.CurrentResource) && !string.IsNullOrWhiteSpace(Instances.GameSettingsUserControlModel.CurrentResources[0].Name))
                    Instances.GameSettingsUserControlModel.CurrentResource = Instances.GameSettingsUserControlModel.CurrentResources[0].Name;
            }
            if (Instances.GameSettingsUserControlModel.CurrentResources.Any(r => r.Name == Instances.GameSettingsUserControlModel.CurrentResource))
            {
                var resources = Instances.GameSettingsUserControlModel.CurrentResources.FirstOrDefault(r => r.Name == Instances.GameSettingsUserControlModel.CurrentResource);
                if (resources?.Path != null)
                {
                    foreach (var resourcePath in resources.Path)
                    {
                        if (!Path.Exists($"{resourcePath}/pipeline/"))
                            break;
                        filesCount = Directory.GetFiles($"{resourcePath}/pipeline", "*.json*", SearchOption.AllDirectories).Length;
                    }
                }
            }

            if (filesCount == 0)
            {
                if (!string.IsNullOrWhiteSpace($"{Instances.GameSettingsUserControlModel.CurrentResource}/pipeline") && !Directory.Exists($"{Instances.GameSettingsUserControlModel.CurrentResource}/pipeline"))
                {
                    try
                    {
                        Directory.CreateDirectory($"{Instances.GameSettingsUserControlModel.CurrentResource}/pipeline");
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogError(ex);
                    }
                }

                if (!File.Exists($"{Instances.GameSettingsUserControlModel.CurrentResource}/pipeline/sample.json"))
                {
                    try
                    {
                        File.WriteAllText($"{Instances.GameSettingsUserControlModel.CurrentResource}/pipeline/sample.json",
                            JsonConvert.SerializeObject(new Dictionary<string, TaskModel>
                            {
                                {
                                    "MFAWPF", new TaskModel
                                    {
                                        Action = "DoNothing"
                                    }
                                }
                            }, new JsonSerializerSettings()
                            {
                                Formatting = Formatting.Indented,
                                NullValueHandling = NullValueHandling.Ignore,
                                DefaultValueHandling = DefaultValueHandling.Ignore
                            }));
                    }
                    catch (Exception ex)
                    {
                        LoggerService.LogError(ex);
                    }
                }
            }

            // PopulateTasks();

            return true;
        }
        catch (Exception ex)
        {
            GrowlHelper.Error(string.Format(LocExtension.GetLocalizedValue<string>("PipelineLoadError"),
                ex.Message));
            Console.WriteLine(ex);
            LoggerService.LogError(ex);
            return false;
        }
    }

    private void PopulateTasks(Dictionary<string, TaskModel> taskDictionary)
    {
        // BaseTasks = taskDictionary;
        // foreach (var task in taskDictionary)
        // {
        //     task.Value.Name = task.Key;
        //     ValidateTaskLinks(taskDictionary, task);
        // }
    }

    private void ValidateTaskLinks(Dictionary<string, TaskModel> taskDictionary,
        KeyValuePair<string, TaskModel> task)
    {
        ValidateNextTasks(taskDictionary, task.Value.Next);
        ValidateNextTasks(taskDictionary, task.Value.OnError, "on_error");
        ValidateNextTasks(taskDictionary, task.Value.Interrupt, "interrupt");
    }

    private void ValidateNextTasks(Dictionary<string, TaskModel> taskDictionary,
        object? nextTasks,
        string name = "next")
    {
        if (nextTasks is List<string> tasks)
        {
            foreach (var task in tasks)
            {
                if (!taskDictionary.ContainsKey(task))
                {
                    GrowlHelper.Error(string.Format(LocExtension.GetLocalizedValue<string>("TaskNotFoundError"),
                        name, task));
                }
            }
        }
    }
    public void ConnectToMAA()
    {
        ConfigureMaaProcessorForADB();
        ConfigureMaaProcessorForWin32();
    }

    private void ConfigureMaaProcessorForADB()
    {
        if (Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb)
        {
            var adbInputType = ConfigureAdbInputTypes();
            var adbScreenCapType = ConfigureAdbScreenCapTypes();

            MaaProcessor.MaaFwConfiguration.AdbDevice.Input = adbInputType;
            MaaProcessor.MaaFwConfiguration.AdbDevice.ScreenCap = adbScreenCapType;

            LoggerService.LogInfo(
                $"{LocExtension.GetLocalizedValue<string>("AdbInputMode")}{adbInputType},{LocExtension.GetLocalizedValue<string>("AdbCaptureMode")}{adbScreenCapType}");
        }
    }

    public string ScreenshotType()
    {
        if (Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb)
            return ConfigureAdbScreenCapTypes().ToString();
        return ConfigureWin32ScreenCapTypes().ToString();
    }


    private AdbInputMethods ConfigureAdbInputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.AdbControlInputType;
    }

    private AdbScreencapMethods ConfigureAdbScreenCapTypes()
    {
        return Instances.ConnectSettingsUserControlModel.AdbControlScreenCapType;
    }

    private void ConfigureMaaProcessorForWin32()
    {
        if (Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Win32)
        {
            var win32InputType = ConfigureWin32InputTypes();
            var winScreenCapType = ConfigureWin32ScreenCapTypes();

            MaaProcessor.MaaFwConfiguration.DesktopWindow.Input = win32InputType;
            MaaProcessor.MaaFwConfiguration.DesktopWindow.ScreenCap = winScreenCapType;

            LoggerService.LogInfo(
                $"{"AdbInputMode".ToLocalization()}{win32InputType},{"AdbCaptureMode".ToLocalization()}{winScreenCapType}");
        }
    }

    private Win32ScreencapMethod ConfigureWin32ScreenCapTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlScreenCapType;
    }

    private Win32InputMethod ConfigureWin32InputTypes()
    {
        return Instances.ConnectSettingsUserControlModel.Win32ControlInputType;
    }

    private bool FirstTask = true;

    private void LoadTasks(List<TaskInterfaceItem> tasks, IList<DragItemViewModel>? oldDrags = null)
    {
        var items = ConfigurationHelper.GetValue(ConfigurationKeys.TaskItems, new List<TaskInterfaceItem>()) ?? [];
        var drags = (oldDrags?.ToList() ?? []).Union(items.Select(interfaceItem => new DragItemViewModel(interfaceItem))).ToList();

        if (FirstTask)
        {
            InitializeResources();
            FirstTask = false;
        }


        var (updateList, removeList) = SynchronizeTaskItems(drags, tasks);
        updateList.RemoveAll(d => removeList.Contains(d));

        UpdateViewModels(updateList, tasks);
    }

    private void InitializeResources()
    {
        Instances.GameSettingsUserControlModel.CurrentResources =
            MaaInterface.Instance?.Resources.Values.Count > 0
                ? new ObservableCollection<MaaInterface.MaaCustomResource>(MaaInterface.Instance.Resources.Values.ToList())
                :
                [
                    new MaaInterface.MaaCustomResource
                    {
                        Name = "Default",
                        Path = [MaaProcessor.ResourceBase]
                    }
                ];
        if (Instances.GameSettingsUserControlModel.CurrentResources.Count > 0 && Instances.GameSettingsUserControlModel.CurrentResources.All(r => r.Name != Instances.GameSettingsUserControlModel.CurrentResource))
            Instances.GameSettingsUserControlModel.CurrentResource = Instances.GameSettingsUserControlModel.CurrentResources[0].Name ?? "Default";
    }

    private (List<DragItemViewModel> updateList, List<DragItemViewModel> removeList) SynchronizeTaskItems(
        IList<DragItemViewModel> drags,
        List<TaskInterfaceItem> tasks)
    {
        // 可选任务集以 interface.json 为准（各实例一致）；
        // 勾选状态 / 选项取值从本机配置合并回来（各实例可不同）。
        var savedByKey = new Dictionary<(string? Name, string? Entry), DragItemViewModel>();
        var savedByEntry = new Dictionary<string?, DragItemViewModel>();
        foreach (var oldItem in drags)
        {
            var key = (oldItem.Name, oldItem.InterfaceItem?.Entry);
            savedByKey[key] = oldItem;
            if (!string.IsNullOrEmpty(oldItem.InterfaceItem?.Entry))
                savedByEntry[oldItem.InterfaceItem.Entry] = oldItem;
        }

        var updateList = new List<DragItemViewModel>();
        var used = new HashSet<DragItemViewModel>();
        foreach (var task in tasks)
        {
            DragItemViewModel? saved = null;
            if (savedByKey.TryGetValue((task.Name, task.Entry), out var byKey))
                saved = byKey;
            else if (!string.IsNullOrEmpty(task.Entry) && savedByEntry.TryGetValue(task.Entry, out var byEntry))
                saved = byEntry;

            if (saved != null)
            {
                UpdateExistingItem(saved, task, updateName: saved.Name != task.Name);
                updateList.Add(saved);
                used.Add(saved);
            }
            else
            {
                var clone = JsonConvert.DeserializeObject<TaskInterfaceItem>(JsonConvert.SerializeObject(task)) ?? task;
                clone.Check ??= false;
                clone.Option?.ForEach(SetDefaultOptionValue);
                updateList.Add(new DragItemViewModel(clone));
            }
        }

        var removeList = drags.Where(d => !used.Contains(d)).ToList();
        return (updateList, removeList);
    }

    private void UpdateExistingItem(DragItemViewModel oldItem, TaskInterfaceItem newItem, bool updateName = false)
    {
        if (oldItem.InterfaceItem == null) return;
        if (updateName)
            oldItem.InterfaceItem.Name = newItem.Name;
        else
        {
            if (oldItem.InterfaceItem.Name != newItem.Name)
                return;
        }
        oldItem.InterfaceItem.Entry = newItem.Entry;
        oldItem.InterfaceItem.PipelineOverride = newItem.PipelineOverride;
        oldItem.InterfaceItem.Document = newItem.Document;
        oldItem.InterfaceItem.Repeatable = newItem.Repeatable;

        if (newItem.Advanced != null)
        {
            var tempDict = oldItem.InterfaceItem.Advanced?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectAdvanced>();
            var maaInterfaceSelectAdvanceds = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectAdvanced>>(JsonConvert.SerializeObject(newItem.Advanced));
            oldItem.InterfaceItem.Advanced = maaInterfaceSelectAdvanceds.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing))
                {
                    opt.Data = existing.Data;
                }
                return opt;
            }).ToList();
        }
        else
        {
            oldItem.InterfaceItem.Advanced = null;
        }

        if (newItem.Option != null)
        {
            var tempDict = oldItem.InterfaceItem.Option?.ToDictionary(t => t.Name) ?? new Dictionary<string, MaaInterface.MaaInterfaceSelectOption>();
            var maaInterfaceSelectOptions = JsonConvert.DeserializeObject<List<MaaInterface.MaaInterfaceSelectOption>>(JsonConvert.SerializeObject(newItem.Option));
            oldItem.InterfaceItem.Option = maaInterfaceSelectOptions.Select(opt =>
            {
                if (tempDict.TryGetValue(opt.Name ?? string.Empty, out var existing))
                {
                    opt.Index = existing.Index;
                    opt.Indexes = existing.Indexes != null ? [..existing.Indexes] : null;
                }
                else
                {
                    SetDefaultOptionValue(opt);
                }
                return opt;
            }).ToList();
        }
        else
        {
            oldItem.InterfaceItem.Option = null;
        }
    }

    private void SetDefaultOptionValue(MaaInterface.MaaInterfaceSelectOption option)
    {
        if (!(MaaInterface.Instance?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) ?? false)) return;

        var defaultIndex = interfaceOption.Cases?
                .FindIndex(c => c.Name == interfaceOption.DefaultCase)
            ?? -1;
        if (defaultIndex != -1) option.Index = defaultIndex;
    }

    private void UpdateViewModels(IList<DragItemViewModel> drags, List<TaskInterfaceItem> tasks)
    {
        var newItems = tasks.Select(t => new DragItemViewModel(t)).ToList();
        
        foreach (var item in newItems)
        {
            if (item.InterfaceItem?.Option != null && !drags.Any())
            {
                item.InterfaceItem.Option.ForEach(SetDefaultOptionValue);
            }
        }

        ViewModel.TasksSource.AddRange(newItems);

        // 列表成员以 interface 同步结果为准；本机只保留勾选/选项差异。
        var display = drags.Any() ? drags : newItems;
        if (!ViewModel.TaskItemViewModels.Any())
        {
            ViewModel.TaskItemViewModels = new ObservableCollection<DragItemViewModel>(display);
            ViewModel.ReorderCheckedTasksToTop();
            return;
        }

        var currentEntries = ViewModel.TaskItemViewModels
            .Select(d => d.InterfaceItem?.Entry)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet();
        var displayEntries = display
            .Select(d => d.InterfaceItem?.Entry)
            .Where(e => !string.IsNullOrEmpty(e))
            .ToHashSet();
        if (!currentEntries.SetEquals(displayEntries) || ViewModel.TaskItemViewModels.Count != display.Count)
        {
            ViewModel.TaskItemViewModels = new ObservableCollection<DragItemViewModel>(display);
        }

        ViewModel.ReorderCheckedTasksToTop();
    }

    public void Toggle()
    {
        if (Instances.RootViewModel.IsRunning)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start(bool onlyStart = false, bool checkUpdate = false)
    {
        if (InitializeData())
        {
            MaaProcessor.Money = 0;
            List<DragItemViewModel> tasks;
            if (ViewModel.IsPresetMode)
            {
                tasks = ViewModel.TaskItemViewModels
                    .Where(task => task.SequenceOrder.HasValue && task.SequenceOrder.Value > 0)
                    .OrderBy(task => task.SequenceOrder!.Value)
                    .ToList();
            }
            else
            {
                // 已勾选任务按 interface.json 原顺序加入执行队列
                tasks = ViewModel.GetCheckedTasksInOriginalOrder();
            }

            ConnectToMAA();
            MaaProcessor.Instance.Start(tasks, onlyStart, checkUpdate);
        }
    }

    public void StartSingle(DragItemViewModel task, bool onlyStart = false, bool checkUpdate = false)
    {
        if (task == null)
            return;

        if (Instances.RootViewModel.IsRunning || !Instances.RootViewModel.Idle)
        {
            GrowlHelper.Warning(LocExtension.GetLocalizedValue<string>("TaskAlreadyRunning"));
            return;
        }

        if (InitializeData())
        {
            MaaProcessor.Money = 0;
            ConnectToMAA();
            MaaProcessor.Instance.Start(new List<DragItemViewModel> { task }, onlyStart, checkUpdate);
        }
    }

    public void Stop()
    {
        MaaProcessor.Instance.Stop();
    }

    private void Start(object sender, RoutedEventArgs e) => Start();

    private void Stop(object sender, RoutedEventArgs e) => Stop();

    private void RunSingle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DragItemViewModel task })
            StartSingle(task);
    }

    private void RunSingleFromMenu(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        var contextMenu = menuItem?.Parent as ContextMenu;
        DragItemViewModel? task = null;

        if (contextMenu?.PlacementTarget is FrameworkElement target)
        {
            task = target.DataContext as DragItemViewModel
                   ?? (target as ListBoxItem)?.Content as DragItemViewModel;
        }

        if (task == null && menuItem?.DataContext is DragItemViewModel fromMenu)
            task = fromMenu;

        if (task != null)
            StartSingle(task);
    }


    private void Edit(object sender, RoutedEventArgs e)
    {
        if (Instances.ConnectingViewModel.CurrentDevice == null)
        {
            GrowlHelper.Warning(
                "Warning_CannotConnect".ToLocalizationFormatted(true, Instances.ConnectingViewModel.CurrentController == MaaControllerTypes.Adb
                    ? "Emulator".ToLocalization()
                    : "Window".ToLocalization()));
            return;
        }

        TaskDialog?.Show();

        Instances.RootViewModel.Idle = false;
    }

    private void OnTaskCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsPresetMode)
        {
            var clickedTask = (sender as CheckBox)?.DataContext as DragItemViewModel;
            ViewModel.ExitPresetMode(cancelPresetSelections: true, keepSelectedTask: clickedTask);
            if (sender is CheckBox checkBox)
            {
                checkBox.IsChecked = true;
            }
            if (clickedTask != null)
            {
                clickedTask.IsCheckedWithNull = true;
            }
        }
    }

    private void SelectAll(object sender, RoutedEventArgs e)
    {
        ViewModel.ExitPresetMode();
        ViewModel.RunWithSuppressedReorder(() =>
        {
            foreach (var task in ViewModel.TaskItemViewModels)
                task.IsChecked = true;
        });
    }

    private void SelectNone(object sender, RoutedEventArgs e)
    {
        ViewModel.ExitPresetMode();
        ViewModel.RunWithSuppressedReorder(() =>
        {
            foreach (var task in ViewModel.TaskItemViewModels)
                task.IsChecked = false;
        });
    }

    private void Add(object sender, RoutedEventArgs e)
    {
        Instances.RootViewModel.Idle = false;
        var addTaskDialog = new MFAWPF.Views.UI.Dialog.AddTaskDialog(ViewModel.TasksSource);
        addTaskDialog.ShowDialog();
        if (addTaskDialog.OutputContent != null)
        {
            ViewModel.ExitPresetMode();
            ViewModel.TaskItemViewModels.Add(addTaskDialog.OutputContent.Clone());
            ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, ViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
        }
    }

    private void Delete(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;

        var contextMenu = menuItem?.Parent as ContextMenu;

        if (contextMenu?.PlacementTarget is TextBlock item)
        {
            if (item.DataContext is DragItemViewModel taskItemViewModel)
            {
                ViewModel.ExitPresetMode();
                int index = ViewModel.TaskItemViewModels.IndexOf(taskItemViewModel);
                ViewModel.TaskItemViewModels.RemoveAt(index);
                Instances.TaskOptionSettingsUserControl.SetOption(taskItemViewModel, false);
                ConfigurationHelper.SetValue(ConfigurationKeys.TaskItems, ViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            }
        }
    }
    private T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;

        var parent = parentObject as T;
        return parent ?? FindVisualParent<T>(parentObject);
    }

    private void TaskList_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindVisualParent<ScrollViewer>((DependencyObject)sender);

        if (scrollViewer != null)
        {
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3);
            e.Handled = true;
        }
    }

    public void ToggleCheckBoxNullOnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right)
        {
            return;
        }

        CheckBox checkBox = (CheckBox)sender;
        if (checkBox.DataContext is DragItemViewModel clickedTask && ViewModel.IsPresetMode)
        {
            ViewModel.ExitPresetMode(cancelPresetSelections: true);
            clickedTask.IsCheckedWithNull = null;
            checkBox.IsChecked = null;
            return;
        }

        ViewModel.ExitPresetMode();
        checkBox.IsChecked = checkBox.IsChecked == null ? (bool?)false : null;
    }

    private void UIElement_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right)
        {
            return;
        }
        if (sender is CheckBox checkBox)
        {
            if (checkBox.DataContext is DragItemViewModel clickedTask && ViewModel.IsPresetMode)
            {
                ViewModel.ExitPresetMode(cancelPresetSelections: true);
                clickedTask.IsCheckedWithNull = null;
                checkBox.IsChecked = null;
                e.Handled = true;
                return;
            }

            ViewModel.ExitPresetMode();
            checkBox.IsChecked = checkBox.IsChecked == null ? (bool?)false : null;
        }
        e.Handled = true;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.Models;
using MFAWPF.Services;
using MFAWPF.ViewModels.Tool;

namespace MFAWPF.ViewModels.UI;

/// <summary>
/// ViewModel for the Task Sequence configuration and preset management page.
/// </summary>
public partial class TaskSequenceViewModel : ViewModel
{
    private readonly TaskPresetStorageService _presetStorageService;

    [ObservableProperty]
    private ObservableCollection<TaskSequenceItemViewModel> _sequenceItems = new();

    [ObservableProperty]
    private ObservableCollection<TaskPreset> _presets = new();

    [ObservableProperty]
    private TaskPreset? _selectedPreset;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private bool _hasTasks;

    public TaskSequenceViewModel(TaskPresetStorageService presetStorageService)
    {
        _presetStorageService = presetStorageService;
        LoadPresets();
        RefreshTasksFromQueue();
    }

    /// <summary>
    /// Refreshes the sequence items from the current tasks source or task queue.
    /// </summary>
    public void RefreshTasksFromQueue()
    {
        var taskSource = Instances.TaskQueueViewModel.TasksSource.Any()
            ? Instances.TaskQueueViewModel.TasksSource
            : Instances.TaskQueueViewModel.TaskItemViewModels;

        var currentAssignments = SequenceItems
            .Where(i => i.IsAssigned)
            .ToDictionary(i => i.RawName, i => i.OrderIndex, StringComparer.OrdinalIgnoreCase);

        var newItems = new ObservableCollection<TaskSequenceItemViewModel>();
        foreach (var task in taskSource)
        {
            var itemVm = new TaskSequenceItemViewModel(task, RequestOrderChange);
            var taskKey = task.InterfaceItem?.Name ?? task.Name;
            if (currentAssignments.TryGetValue(taskKey, out var existingOrder))
            {
                itemVm.SetOrderInternal(existingOrder);
            }
            newItems.Add(itemVm);
        }

        SequenceItems = newItems;
        HasTasks = SequenceItems.Count > 0;

        if (currentAssignments.Count == 0)
        {
            if (SelectedPreset != null && SelectedPreset.Items.Count > 0)
            {
                ApplyPresetToItems(SelectedPreset);
                return;
            }
            else if (Instances.TaskQueueViewModel.IsPresetMode)
            {
                foreach (var itemVm in SequenceItems)
                {
                    if (itemVm.DragItem.SequenceOrder.HasValue && itemVm.DragItem.SequenceOrder.Value > 0)
                    {
                        itemVm.SetOrderInternal(itemVm.DragItem.SequenceOrder.Value);
                    }
                }
            }
        }

        NormalizeSequence();
    }

    /// <summary>
    /// Loads all presets from storage.
    /// </summary>
    public void LoadPresets()
    {
        var loaded = _presetStorageService.LoadPresets();
        Presets.Clear();
        foreach (var preset in loaded)
        {
            Presets.Add(preset);
        }

        if (Presets.Count > 0 && SelectedPreset == null)
        {
            SelectedPreset = Presets.First();
        }
    }

    partial void OnSelectedPresetChanged(TaskPreset? value)
    {
        if (value == null) return;
        ApplyPresetToItems(value);
    }

    /// <summary>
    /// Applies a preset's ordered items to the current sequence item views.
    /// </summary>
    public void ApplyPresetToItems(TaskPreset preset)
    {
        var orderMap = preset.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.TaskName) && i.Order > 0)
            .ToDictionary(i => i.TaskName, i => i.Order, StringComparer.OrdinalIgnoreCase);

        foreach (var item in SequenceItems)
        {
            var taskKey = item.RawName;
            if (orderMap.TryGetValue(taskKey, out var order) ||
                (!string.IsNullOrWhiteSpace(item.Name) && orderMap.TryGetValue(item.Name, out order)) ||
                (item.DragItem.InterfaceItem?.Entry != null && orderMap.TryGetValue(item.DragItem.InterfaceItem.Entry, out order)))
            {
                item.SetOrderInternal(order);
            }
            else
            {
                item.SetOrderInternal(null);
            }
        }

        NormalizeSequence();
    }

    /// <summary>
    /// Handles user request to set an order for a task, implementing gap-filling and continuous sequence enforcement.
    /// </summary>
    public void RequestOrderChange(TaskSequenceItemViewModel item, int? requestedOrder)
    {
        var others = SequenceItems
            .Where(i => i != item && i.IsAssigned)
            .OrderBy(i => i.OrderIndex!.Value)
            .ToList();

        if (requestedOrder == null || requestedOrder <= 0)
        {
            item.SetOrderInternal(null);
            for (int i = 0; i < others.Count; i++)
            {
                others[i].SetOrderInternal(i + 1);
            }
            return;
        }

        // Clamp to next contiguous slot (e.g., if max is 6, input 8 becomes 7)
        int maxAllowed = others.Count + 1;
        int targetOrder = Math.Clamp(requestedOrder.Value, 1, maxAllowed);

        others.Insert(targetOrder - 1, item);
        for (int i = 0; i < others.Count; i++)
        {
            others[i].SetOrderInternal(i + 1);
        }
    }

    /// <summary>
    /// Normalizes the entire sequence to guarantee contiguous 1..N order with no gaps.
    /// </summary>
    private void NormalizeSequence()
    {
        var assigned = SequenceItems
            .Where(i => i.IsAssigned)
            .OrderBy(i => i.OrderIndex!.Value)
            .ToList();

        for (int i = 0; i < assigned.Count; i++)
        {
            assigned[i].SetOrderInternal(i + 1);
        }
    }

    [RelayCommand]
    public void AssignNext(TaskSequenceItemViewModel item)
    {
        if (item == null) return;
        if (item.IsAssigned) return;

        int maxOrder = SequenceItems.Where(i => i.IsAssigned).Select(i => i.OrderIndex!.Value).DefaultIfEmpty(0).Max();
        RequestOrderChange(item, maxOrder + 1);
    }

    [RelayCommand]
    public void ClearOrder(TaskSequenceItemViewModel item)
    {
        if (item == null) return;
        RequestOrderChange(item, null);
    }

    [RelayCommand]
    public void NumberAll()
    {
        for (int i = 0; i < SequenceItems.Count; i++)
        {
            SequenceItems[i].SetOrderInternal(i + 1);
        }
    }

    [RelayCommand]
    public void ClearAll()
    {
        foreach (var item in SequenceItems)
        {
            item.SetOrderInternal(null);
        }
    }

    [RelayCommand]
    public void SaveCurrentPreset()
    {
        if (SelectedPreset == null)
        {
            if (Presets.Count > 0)
            {
                SelectedPreset = Presets.First();
            }
            else
            {
                var defaultPreset = new TaskPreset { Name = "默认预设" };
                Presets.Add(defaultPreset);
                SelectedPreset = defaultPreset;
            }
        }

        SelectedPreset.Items = SequenceItems
            .Where(i => i.IsAssigned)
            .OrderBy(i => i.OrderIndex!.Value)
            .Select(i => new TaskPresetItem
            {
                TaskName = i.RawName,
                Order = i.OrderIndex!.Value
            })
            .ToList();

        _presetStorageService.SavePreset(SelectedPreset);
        GrowlHelper.Success(string.Format("PresetSavedSuccess".ToLocalization(), SelectedPreset.Name));
    }

    public bool CreatePreset(string name)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            GrowlHelper.Warning("PresetNameCannotBeEmpty".ToLocalization());
            return false;
        }

        var preset = new TaskPreset
        {
            Name = trimmedName,
            Items = SequenceItems
                .Where(i => i.IsAssigned)
                .OrderBy(i => i.OrderIndex!.Value)
                .Select(i => new TaskPresetItem
                {
                    TaskName = i.RawName,
                    Order = i.OrderIndex!.Value
                })
                .ToList()
        };

        var existing = Presets.FirstOrDefault(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Items = preset.Items;
            SelectedPreset = existing;
            _presetStorageService.SavePreset(existing);
        }
        else
        {
            Presets.Add(preset);
            SelectedPreset = preset;
            _presetStorageService.SavePreset(preset);
        }

        GrowlHelper.Success(string.Format("PresetSavedSuccess".ToLocalization(), trimmedName));
        return true;
    }

    public bool RenamePreset(TaskPreset preset, string newName)
    {
        var trimmedName = newName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            GrowlHelper.Warning("PresetNameCannotBeEmpty".ToLocalization());
            return false;
        }

        if (preset.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var oldName = preset.Name;
        _presetStorageService.DeletePreset(oldName);

        preset.Name = trimmedName;
        _presetStorageService.SavePreset(preset);

        var index = Presets.IndexOf(preset);
        if (index >= 0)
        {
            Presets[index] = preset;
            SelectedPreset = preset;
        }

        GrowlHelper.Success(string.Format("PresetSavedSuccess".ToLocalization(), trimmedName));
        return true;
    }

    [RelayCommand]
    public void DeleteCurrentPreset()
    {
        if (SelectedPreset == null) return;
        DeletePreset(SelectedPreset);
    }

    [RelayCommand]
    public void DeletePreset(TaskPreset? preset)
    {
        if (preset == null) return;

        var name = preset.Name;
        _presetStorageService.DeletePreset(name);
        Presets.Remove(preset);
        if (SelectedPreset == preset)
        {
            SelectedPreset = Presets.FirstOrDefault();
        }
        GrowlHelper.Success(string.Format("PresetDeletedSuccess".ToLocalization(), name));
    }

    [RelayCommand]
    public void ApplyPreset()
    {
        NormalizeSequence();

        var sequence = SequenceItems
            .Where(i => i.IsAssigned)
            .OrderBy(i => i.OrderIndex!.Value)
            .Select(i => new TaskPresetItem
            {
                TaskName = i.RawName,
                Order = i.OrderIndex!.Value
            })
            .ToList();

        if (sequence.Count == 0 && SelectedPreset != null && SelectedPreset.Items.Count > 0)
        {
            sequence = SelectedPreset.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.TaskName) && i.Order > 0)
                .OrderBy(i => i.Order)
                .ToList();
        }

        if (sequence.Count == 0)
        {
            GrowlHelper.WarningGlobal("NoSequenceToApply".ToLocalization());
            return;
        }

        var presetName = !string.IsNullOrWhiteSpace(SelectedPreset?.Name)
            ? SelectedPreset.Name
            : "DefaultPreset".ToLocalization();
        if (string.IsNullOrWhiteSpace(presetName))
        {
            presetName = "默认预设";
        }

        Instances.TaskQueueViewModel.ApplyPresetSequence(sequence, presetName);
        GrowlHelper.SuccessGlobal(string.Format("PresetAppliedSuccess".ToLocalization(), presetName));

        try
        {
            Instances.RootView.SwitchToHomeTab();
        }
        catch (Exception ex)
        {
            LoggerService.LogError(ex);
        }
    }
}

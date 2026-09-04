using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.Models.Characters;
using MFAWPF.Services.Characters;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;

namespace MFAWPF.ViewModels.UI;

internal sealed partial class CharacterAnimationSlotViewModel : ObservableObject
{
    public CharacterAnimationSlotViewModel(string title, CharacterAnimationDefinition definition)
    {
        Title = title;
        Definition = definition;
        _sourcePath = definition.SourcePath;
        _playbackRate = definition.PlaybackRate;
        _loop = definition.Loop;
    }

    public string Title { get; }
    public CharacterAnimationDefinition Definition { get; }

    [ObservableProperty] private string _sourcePath;
    [ObservableProperty] private double _playbackRate;
    [ObservableProperty] private bool _loop;

    partial void OnSourcePathChanged(string value) => Definition.SourcePath = value?.Trim() ?? string.Empty;
    partial void OnPlaybackRateChanged(double value) => Definition.PlaybackRate = Math.Clamp(value, 0.25d, 3d);
    partial void OnLoopChanged(bool value) => Definition.Loop = value;
}

internal sealed class CharacterTaskOption(string key, string name)
{
    public string Key { get; } = key;
    public string Name { get; } = name;
}

/// <summary>
/// Provides editable desktop-character settings for the character page.
/// </summary>
internal partial class CharacterViewModel : ViewModel
{
    private readonly CharacterSettingsStore _settingsStore;
    private readonly CharacterStoragePaths _storagePaths;
    private readonly CharacterAnimationLoader _animationLoader;
    private readonly CharacterRuntimeService _runtimeService;
    private CharacterSettings _settings;

    public CharacterViewModel(
        CharacterSettingsStore settingsStore,
        CharacterStoragePaths storagePaths,
        CharacterAnimationLoader animationLoader,
        CharacterRuntimeService runtimeService)
    {
        _settingsStore = settingsStore;
        _storagePaths = storagePaths;
        _animationLoader = animationLoader;
        _runtimeService = runtimeService;
        _settings = settingsStore.Load();
        _settings.Normalize();
        foreach (var profile in _settings.Profiles.Where(profile => profile.Name == "Character"))
        {
            profile.Name = "Character".ToLocalization();
        }
        Profiles = new ObservableCollection<CharacterProfile>(_settings.Profiles);
        SelectedProfile = Profiles.First(profile => profile.Id == _settings.ActiveProfileId);
        _isEnabled = _settings.IsEnabled;
        AssetRoot = storagePaths.AssetRoot;
        RefreshTasks();
        RebuildProfileEditors();
        _runtimeService.SettingsChanged += OnRuntimeSettingsChanged;
    }

    public ObservableCollection<CharacterProfile> Profiles { get; }
    public ObservableCollection<CharacterAnimationSlotViewModel> AnimationSlots { get; } = [];
    public ObservableCollection<CharacterTaskAnimationRule> TaskRules { get; } = [];
    public ObservableCollection<CharacterTaskOption> AvailableTasks { get; } = [];

    internal CharacterSettings Settings => _settings;

    public string AssetRoot { get; }

    [ObservableProperty] private CharacterProfile _selectedProfile;
    [ObservableProperty] private CharacterAnimationSlotViewModel? _selectedPreviewSlot;
    [ObservableProperty] private CharacterTaskOption? _selectedTask;
    [ObservableProperty] private ImageSource? _previewFrame;
    [ObservableProperty] private bool _isEnabled;

    partial void OnSelectedProfileChanged(CharacterProfile value)
    {
        if (value is not null)
        {
            RebuildProfileEditors();
        }
    }

    partial void OnSelectedProfileChanging(CharacterProfile value)
    {
        if (_selectedProfile is not null)
        {
            _selectedProfile.TaskRules = TaskRules.ToList();
        }
    }

    partial void OnSelectedPreviewSlotChanged(CharacterAnimationSlotViewModel? value) =>
        PreviewFrame = _animationLoader.LoadFirstFrame(value?.Definition);

    [RelayCommand]
    private void AddProfile()
    {
        var profile = CharacterProfile.CreateDefault();
        profile.Name = $"{"Character".ToLocalization()} {Profiles.Count + 1}";
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (Profiles.Count <= 1)
        {
            GrowlHelper.Warning("CharacterAtLeastOneProfile".ToLocalization());
            return;
        }

        var index = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles[Math.Clamp(index - 1, 0, Profiles.Count - 1)];
    }

    [RelayCommand]
    private void SaveConfiguration()
    {
        SelectedProfile.TaskRules = TaskRules.ToList();
        SelectedProfile.Normalize();
        _settings.IsEnabled = IsEnabled;
        _settings.Profiles = Profiles.ToList();
        _settings.ActiveProfileId = SelectedProfile.Id;
        _settingsStore.Save(_settings);
        _runtimeService.ApplySettings(_settings, save: false);
        Growl.SuccessGlobal("CharacterSettingsSaved".ToLocalization());
    }

    [RelayCommand]
    private void OpenAssetRoot() => _runtimeService.OpenAssetRoot();

    [RelayCommand]
    private void ResetPosition()
    {
        SelectedProfile.Left = null;
        SelectedProfile.Top = null;
        GrowlHelper.Info("CharacterPositionReset".ToLocalization());
    }

    [RelayCommand]
    private void RefreshTaskList()
    {
        RefreshTasks();
        GrowlHelper.Info("CharacterTaskListRefreshed".ToLocalization());
    }

    [RelayCommand]
    private void AddTaskRule()
    {
        if (SelectedTask is null)
        {
            return;
        }

        if (TaskRules.Any(rule => string.Equals(rule.TaskKey, SelectedTask.Key, StringComparison.OrdinalIgnoreCase)))
        {
            GrowlHelper.Warning("CharacterDuplicateTaskRule".ToLocalization());
            return;
        }

        TaskRules.Add(new CharacterTaskAnimationRule
        {
            TaskKey = SelectedTask.Key,
            TaskName = SelectedTask.Name,
        });
    }

    [RelayCommand]
    private void RemoveTaskRule(CharacterTaskAnimationRule? rule)
    {
        if (rule is not null)
        {
            TaskRules.Remove(rule);
        }
    }

    [RelayCommand]
    private void BrowseAnimationFile(CharacterAnimationSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            InitialDirectory = _storagePaths.AssetRoot,
            Filter = "PNG / GIF|*.png;*.gif|PNG|*.png|GIF|*.gif",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            AssignAssetPath(dialog.FileName, path => slot.SourcePath = path);
            SelectedPreviewSlot = slot;
            PreviewFrame = _animationLoader.LoadFirstFrame(slot.Definition);
        }
    }

    [RelayCommand]
    private void BrowseAnimationFolder(CharacterAnimationSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            InitialDirectory = _storagePaths.AssetRoot,
            Multiselect = false,
        };
        if (dialog.ShowDialog() == true)
        {
            AssignAssetPath(dialog.FolderName, path => slot.SourcePath = path);
            SelectedPreviewSlot = slot;
            PreviewFrame = _animationLoader.LoadFirstFrame(slot.Definition);
        }
    }

    [RelayCommand]
    private void ClearAnimation(CharacterAnimationSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        slot.SourcePath = string.Empty;
        if (ReferenceEquals(SelectedPreviewSlot, slot))
        {
            PreviewFrame = null;
        }
    }

    [RelayCommand]
    private void BrowseTaskAnimation(CharacterTaskAnimationRule? rule)
    {
        if (rule is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            InitialDirectory = _storagePaths.AssetRoot,
            Filter = "PNG / GIF|*.png;*.gif|PNG|*.png|GIF|*.gif",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            if (AssignAssetPath(dialog.FileName, path => rule.Animation.SourcePath = path))
            {
                var index = TaskRules.IndexOf(rule);
                TaskRules[index] = rule;
            }
        }
    }

    internal void SetFootAnchor(double x, double y)
    {
        SelectedProfile.FootAnchorX = Math.Clamp(x, 0d, 1d);
        SelectedProfile.FootAnchorY = Math.Clamp(y, 0d, 1d);
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private bool AssignAssetPath(string fullPath, Action<string> assignment)
    {
        if (!_storagePaths.TryCreateRelativePath(fullPath, out var relativePath))
        {
            GrowlHelper.Warning("CharacterAssetOutsideRoot".ToLocalization());
            return false;
        }

        assignment(relativePath);
        return true;
    }

    private void RebuildProfileEditors()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        SelectedProfile.Normalize();
        AnimationSlots.Clear();
        for (var index = 0; index < SelectedProfile.IdleAnimations.Count; index++)
        {
            AnimationSlots.Add(new CharacterAnimationSlotViewModel(
                $"{"CharacterIdle".ToLocalization()} {index + 1}",
                SelectedProfile.IdleAnimations[index]));
        }

        AddSlot("CharacterMoveLeft", SelectedProfile.MoveLeftAnimation);
        AddSlot("CharacterMoveRight", SelectedProfile.MoveRightAnimation);
        AddSlot("CharacterTaskRunning", SelectedProfile.TaskRunningAnimation);
        AddSlot("CharacterTaskSucceeded", SelectedProfile.TaskSucceededAnimation);
        AddSlot("CharacterTaskFailed", SelectedProfile.TaskFailedAnimation);
        AddSlot("CharacterDragging", SelectedProfile.DraggingAnimation);
        AddSlot("CharacterReleased", SelectedProfile.ReleasedAnimation);
        AddSlot("CharacterRightClick", SelectedProfile.RightClickAnimation);

        TaskRules.Clear();
        TaskRules.AddRange(SelectedProfile.TaskRules);
        SelectedPreviewSlot = AnimationSlots.FirstOrDefault();
    }

    private void AddSlot(string titleKey, CharacterAnimationDefinition definition) =>
        AnimationSlots.Add(new CharacterAnimationSlotViewModel(titleKey.ToLocalization(), definition));

    private void RefreshTasks()
    {
        AvailableTasks.Clear();
        foreach (var item in Instances.TaskQueueViewModel.TaskItemViewModels)
        {
            var key = item.InterfaceItem?.Entry ?? item.InterfaceItem?.Name ?? item.Name;
            if (!string.IsNullOrWhiteSpace(key))
            {
                AvailableTasks.Add(new CharacterTaskOption(key, item.Name));
            }
        }

        SelectedTask = AvailableTasks.FirstOrDefault();
    }

    internal void RefreshTaskOptions() => RefreshTasks();

    private void OnRuntimeSettingsChanged(object? sender, EventArgs e)
    {
        IsEnabled = _settings.IsEnabled;
        OnPropertyChanged(nameof(SelectedProfile));
    }
}

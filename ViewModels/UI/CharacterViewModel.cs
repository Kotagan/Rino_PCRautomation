using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandyControl.Controls;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.Models.Characters;
using MFAWPF.Services.Characters;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows.Media;
using System.Windows.Threading;

namespace MFAWPF.ViewModels.UI;

internal sealed partial class CharacterAnimationSlotViewModel : ObservableObject
{
    private readonly CharacterAnimationLoader _animationLoader;

    public CharacterAnimationSlotViewModel(
        string title,
        CharacterAnimationDefinition definition,
        CharacterAnimationLoader animationLoader)
    {
        Title = title;
        Definition = definition;
        _animationLoader = animationLoader;
        _sourcePath = definition.SourcePath;
        _playbackRate = definition.PlaybackRate;
        _loop = definition.Loop;
        _thumbnail = _animationLoader.LoadFirstFrame(definition);
    }

    public string Title { get; }
    public CharacterAnimationDefinition Definition { get; }

    [ObservableProperty] private string _sourcePath;
    [ObservableProperty] private double _playbackRate;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private ImageSource? _thumbnail;

    partial void OnSourcePathChanged(string value)
    {
        Definition.SourcePath = value?.Trim() ?? string.Empty;
        Thumbnail = _animationLoader.LoadFirstFrame(Definition);
    }

    partial void OnPlaybackRateChanged(double value) => Definition.PlaybackRate = Math.Clamp(value, 0.25d, 3d);
    partial void OnLoopChanged(bool value) => Definition.Loop = value;

    public void RefreshThumbnail() => Thumbnail = _animationLoader.LoadFirstFrame(Definition);
}

internal sealed class CharacterAnimationRowViewModel(
    CharacterAnimationSlotViewModel slot1,
    CharacterAnimationSlotViewModel? slot2 = null,
    CharacterAnimationSlotViewModel? slot3 = null,
    bool isLastRow = false)
{
    public CharacterAnimationSlotViewModel Slot1 { get; } = slot1;
    public CharacterAnimationSlotViewModel? Slot2 { get; } = slot2;
    public CharacterAnimationSlotViewModel? Slot3 { get; } = slot3;
    public bool IsLastRow { get; } = isLastRow;
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
    private readonly DispatcherTimer _previewTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16),
    };
    private readonly Stopwatch _previewClock = new();
    private CharacterSettings _settings;
    private CharacterAnimationClip _previewClip = CharacterAnimationClip.Empty;
    private TimeSpan _previewFrameElapsed;
    private int _previewFrameIndex;
    private bool _isPreviewActive;

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

        foreach (var profile in _settings.Profiles)
        {
            try
            {
                _storagePaths.EnsureProfileDirectory(profile.Name);
            }
            catch (Exception ex)
            {
                LoggerService.LogError($"Failed to ensure character profile directory for '{profile.Name}': {ex}");
            }
        }

        _storagePaths.CleanupEmptyOrphanedProfileDirectories(_settings.Profiles.Select(profile => profile.Name));

        Profiles = new ObservableCollection<CharacterProfile>(_settings.Profiles);
        SelectedProfile = Profiles.First(profile => profile.Id == _settings.ActiveProfileId);
        _isEnabled = _settings.IsEnabled;
        AssetRoot = storagePaths.AssetRoot;
        _previewTimer.Tick += OnPreviewTimerTick;
        RefreshTasks();
        RebuildProfileEditors();
        _runtimeService.SettingsChanged += OnRuntimeSettingsChanged;
    }

    public ObservableCollection<CharacterProfile> Profiles { get; }
    public ObservableCollection<CharacterAnimationSlotViewModel> AnimationSlots { get; } = [];
    public ObservableCollection<CharacterAnimationRowViewModel> AnimationRows { get; } = [];
    public ObservableCollection<CharacterTaskAnimationRule> TaskRules { get; } = [];
    public ObservableCollection<CharacterTaskOption> AvailableTasks { get; } = [];

    internal CharacterSettings Settings => _settings;

    public string AssetRoot { get; }

    public string CurrentProfileName
    {
        get => SelectedProfile?.Name ?? string.Empty;
        set
        {
            if (SelectedProfile is null)
            {
                return;
            }

            var trimmed = value?.Trim() ?? string.Empty;
            if (string.Equals(SelectedProfile.Name, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            RenameSelectedProfile(trimmed);
        }
    }

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
            OnPropertyChanged(nameof(CurrentProfileName));
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
        StartPreview(value?.Definition);

    [RelayCommand]
    private void AddProfile()
    {
        var profile = CharacterProfile.CreateDefault();
        var candidateName = $"{"Character".ToLocalization()} {Profiles.Count + 1}";
        try
        {
            var createdPath = _storagePaths.CreateProfileDirectory(candidateName);
            profile.Name = Path.GetFileName(createdPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            LoggerService.LogError(ex);
            GrowlHelper.Error("CharacterProfileDirectoryCreateFailed".ToLocalization());
            return;
        }

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

        var profileToDelete = SelectedProfile;
        var index = Profiles.IndexOf(profileToDelete);
        Profiles.Remove(profileToDelete);
        SelectedProfile = Profiles[Math.Clamp(index - 1, 0, Profiles.Count - 1)];

        try
        {
            _storagePaths.TryDeleteProfileDirectory(profileToDelete.Name);
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"Failed to delete directory for profile '{profileToDelete.Name}': {ex}");
        }
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
    private void OpenAssetRoot()
    {
        ReleaseFileOccupancy();
        _runtimeService.OpenAssetRoot();
    }

    /// <summary>
    /// Cancels file occupancy by pausing preview, clearing frames, and resetting working directory.
    /// </summary>
    public void ReleaseFileOccupancy()
    {
        _previewTimer.Stop();
        _previewClock.Stop();
        _previewClip = CharacterAnimationClip.Empty;
        PreviewFrame = null;
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        _runtimeService.ReleaseAssetOccupancy();
    }

    private void RenameSelectedProfile(string newName)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            GrowlHelper.Warning("CharacterProfileNameEmpty".ToLocalization());
            OnPropertyChanged(nameof(CurrentProfileName));
            return;
        }

        if (Profiles.Any(profile => profile != SelectedProfile &&
                                   string.Equals(profile.Name, newName, StringComparison.OrdinalIgnoreCase)))
        {
            GrowlHelper.Warning("CharacterProfileNameDuplicate".ToLocalization());
            OnPropertyChanged(nameof(CurrentProfileName));
            return;
        }

        var oldName = SelectedProfile.Name;
        var oldSafeDir = CharacterStoragePaths.GetProfileDirectoryName(oldName);

        ReleaseFileOccupancy();

        if (!_storagePaths.TryRenameProfileDirectory(oldName, newName, out var finalDirName, out var errorMessageKey))
        {
            var message = (errorMessageKey ?? "CharacterProfileDirectoryRenameFailed").ToLocalization();
            GrowlHelper.Error(message);
            OnPropertyChanged(nameof(CurrentProfileName));
            return;
        }

        SelectedProfile.Name = newName;
        UpdateProfileAssetPaths(SelectedProfile, oldSafeDir, finalDirName);
        RebuildProfileEditors();
        OnPropertyChanged(nameof(CurrentProfileName));
        OnPropertyChanged(nameof(SelectedProfile));
    }

    private static void UpdateProfileAssetPaths(
        CharacterProfile profile,
        string oldDirectoryName,
        string newDirectoryName)
    {
        if (string.IsNullOrWhiteSpace(oldDirectoryName) ||
            string.IsNullOrWhiteSpace(newDirectoryName) ||
            string.Equals(oldDirectoryName, newDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        void UpdateDefinition(CharacterAnimationDefinition? definition)
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.SourcePath))
            {
                return;
            }

            var normalized = definition.SourcePath.Replace('\\', '/');
            var oldPrefix = oldDirectoryName.Replace('\\', '/');
            var newPrefix = newDirectoryName.Replace('\\', '/');

            if (string.Equals(normalized, oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                definition.SourcePath = newDirectoryName;
            }
            else if (normalized.StartsWith(oldPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                var remainder = definition.SourcePath[(oldDirectoryName.Length + 1)..];
                var separator = definition.SourcePath[oldDirectoryName.Length];
                definition.SourcePath = $"{newDirectoryName}{separator}{remainder}";
            }
        }

        foreach (var anim in profile.IdleAnimations)
        {
            UpdateDefinition(anim);
        }

        UpdateDefinition(profile.MoveLeftAnimation);
        UpdateDefinition(profile.MoveRightAnimation);
        UpdateDefinition(profile.TaskRunningAnimation);
        UpdateDefinition(profile.TaskSucceededAnimation);
        UpdateDefinition(profile.TaskFailedAnimation);
        UpdateDefinition(profile.DraggingAnimation);
        UpdateDefinition(profile.ReleasedAnimation);
        UpdateDefinition(profile.RightClickAnimation);

        foreach (var rule in profile.TaskRules)
        {
            UpdateDefinition(rule.Animation);
        }
    }

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
            RestoreDirectory = true,
        };
        try
        {
            if (dialog.ShowDialog() == true)
            {
                if (AssignAssetPath(dialog.FileName, path => slot.SourcePath = path))
                {
                    PreviewAnimation(slot);
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = AppContext.BaseDirectory;
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
        try
        {
            if (dialog.ShowDialog() == true)
            {
                if (AssignAssetPath(dialog.FolderName, path => slot.SourcePath = path))
                {
                    PreviewAnimation(slot);
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = AppContext.BaseDirectory;
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
            StartPreview(null);
        }
    }

    [RelayCommand]
    private void PreviewAnimation(CharacterAnimationSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        if (ReferenceEquals(SelectedPreviewSlot, slot))
        {
            StartPreview(slot.Definition);
            return;
        }

        SelectedPreviewSlot = slot;
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
            RestoreDirectory = true,
        };
        try
        {
            if (dialog.ShowDialog() == true)
            {
                if (AssignAssetPath(dialog.FileName, path => rule.Animation.SourcePath = path))
                {
                    var index = TaskRules.IndexOf(rule);
                    TaskRules[index] = rule;
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = AppContext.BaseDirectory;
        }
    }

    internal void SetFootAnchor(double x, double y)
    {
        SelectedProfile.FootAnchorX = Math.Clamp(x, 0d, 1d);
        SelectedProfile.FootAnchorY = Math.Clamp(y, 0d, 1d);
        OnPropertyChanged(nameof(SelectedProfile));
    }

    internal void SetPreviewActive(bool isActive)
    {
        _isPreviewActive = isActive;
        if (isActive)
        {
            StartPreview(SelectedPreviewSlot?.Definition);
            return;
        }

        _previewTimer.Stop();
        _previewClock.Stop();
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
                SelectedProfile.IdleAnimations[index],
                _animationLoader));
        }

        AddSlot("CharacterMoveLeft", SelectedProfile.MoveLeftAnimation);
        AddSlot("CharacterMoveRight", SelectedProfile.MoveRightAnimation);
        AddSlot("CharacterTaskRunning", SelectedProfile.TaskRunningAnimation);
        AddSlot("CharacterTaskSucceeded", SelectedProfile.TaskSucceededAnimation);
        AddSlot("CharacterTaskFailed", SelectedProfile.TaskFailedAnimation);
        AddSlot("CharacterDragging", SelectedProfile.DraggingAnimation);
        AddSlot("CharacterReleased", SelectedProfile.ReleasedAnimation);
        AddSlot("CharacterRightClick", SelectedProfile.RightClickAnimation);

        AnimationRows.Clear();
        for (var i = 0; i < AnimationSlots.Count; i += 3)
        {
            var slot1 = AnimationSlots[i];
            var slot2 = i + 1 < AnimationSlots.Count ? AnimationSlots[i + 1] : null;
            var slot3 = i + 2 < AnimationSlots.Count ? AnimationSlots[i + 2] : null;
            var isLast = i + 3 >= AnimationSlots.Count;
            AnimationRows.Add(new CharacterAnimationRowViewModel(slot1, slot2, slot3, isLast));
        }

        TaskRules.Clear();
        TaskRules.AddRange(SelectedProfile.TaskRules);
        SelectedPreviewSlot = AnimationSlots.FirstOrDefault();
    }

    private void AddSlot(string titleKey, CharacterAnimationDefinition definition) =>
        AnimationSlots.Add(new CharacterAnimationSlotViewModel(titleKey.ToLocalization(), definition, _animationLoader));

    private void StartPreview(CharacterAnimationDefinition? definition)
    {
        _previewTimer.Stop();
        _previewClock.Reset();
        _previewClip = _animationLoader.Load(definition);
        _previewFrameElapsed = TimeSpan.Zero;
        _previewFrameIndex = 0;
        PreviewFrame = _previewClip.Frames.FirstOrDefault();
        if (!_isPreviewActive || _previewClip.Frames.Count <= 1)
        {
            return;
        }

        _previewClock.Start();
        _previewTimer.Start();
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        var elapsed = _previewClock.Elapsed;
        _previewClock.Restart();
        if (_previewClip.IsEmpty || SelectedPreviewSlot is null)
        {
            _previewTimer.Stop();
            _previewClock.Stop();
            return;
        }

        var playbackRate = Math.Clamp(SelectedPreviewSlot.PlaybackRate, 0.25d, 3d);
        _previewFrameElapsed += TimeSpan.FromTicks((long)(elapsed.Ticks * playbackRate));
        var frameDuration = _previewClip.FrameDurations[
            Math.Min(_previewFrameIndex, _previewClip.FrameDurations.Count - 1)];
        if (_previewFrameElapsed < frameDuration)
        {
            return;
        }

        _previewFrameElapsed -= frameDuration;
        if (_previewFrameIndex < _previewClip.Frames.Count - 1)
        {
            _previewFrameIndex++;
        }
        else if (SelectedPreviewSlot.Loop)
        {
            _previewFrameIndex = 0;
        }
        else
        {
            _previewTimer.Stop();
            _previewClock.Stop();
        }

        PreviewFrame = _previewClip.Frames[_previewFrameIndex];
    }

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

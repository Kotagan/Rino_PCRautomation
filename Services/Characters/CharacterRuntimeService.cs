using MFAWPF.Helper;
using MFAWPF.Models.Characters;
using MFAWPF.ViewModels.UI;
using MFAWPF.Views.UI;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MFAWPF.Services.Characters;

internal enum CharacterRuntimeMode
{
    Idle,
    Moving,
    TaskRunning,
    TaskSucceeded,
    TaskFailed,
    Dragging,
    Released,
    RightClick,
}

internal sealed class CharacterRuntimeService : IDisposable
{
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan TransientMinimumDuration = TimeSpan.FromSeconds(1.5);
    private const int MaximumStatusTextElements = 24;

    private readonly CharacterStoragePaths _storagePaths;
    private readonly CharacterSettingsStore _settingsStore;
    private readonly CharacterAnimationLoader _animationLoader;
    private readonly CharacterTaskStateService _taskStateService;
    private readonly CharacterWindowViewModel _windowViewModel = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimerInterval };
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private readonly Dictionary<string, CharacterAnimationClip> _clipCache =
        new(StringComparer.OrdinalIgnoreCase);

    private CharacterSettings _settings = CharacterSettings.CreateDefault();
    private CharacterProfile _profile = CharacterProfile.CreateDefault();
    private CharacterWindow? _window;
    private CharacterAnimationDefinition? _activeAnimation;
    private CharacterAnimationClip _activeClip = CharacterAnimationClip.Empty;
    private CharacterRuntimeMode _mode = CharacterRuntimeMode.Idle;
    private TimeSpan _frameElapsed;
    private TimeSpan _transientRemaining;
    private double _behaviorSecondsRemaining;
    private double _movementTargetX;
    private int _movementDirection = 1;
    private int _frameIndex;
    private int _lastIdleIndex = -1;
    private bool _isTaskActive;
    private string _currentTaskKey = string.Empty;
    private string _currentTaskName = string.Empty;
    private bool _disposed;

    public CharacterRuntimeService(
        CharacterStoragePaths storagePaths,
        CharacterSettingsStore settingsStore,
        CharacterAnimationLoader animationLoader,
        CharacterTaskStateService taskStateService)
    {
        _storagePaths = storagePaths;
        _settingsStore = settingsStore;
        _animationLoader = animationLoader;
        _taskStateService = taskStateService;
        _timer.Tick += OnTimerTick;
        _taskStateService.StateChanged += OnTaskStateChanged;
    }

    public event EventHandler? SettingsChanged;

    public string AssetRoot => _storagePaths.AssetRoot;

    public void Initialize(CharacterSettings settings)
    {
        _settings = settings;
        ApplySettings(_settings, save: false);
    }

    public void ApplySettings(CharacterSettings settings, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        _settings = settings;
        _profile = settings.GetActiveProfile();
        _clipCache.Clear();
        _windowViewModel.CharacterSize = _profile.Size;

        if (save)
        {
            _settingsStore.Save(_settings);
        }

        if (!_settings.IsEnabled)
        {
            _timer.Stop();
            _window?.Hide();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        EnsureWindow();
        _window?.Show();
        RestoreWindowPosition();
        ReturnToOperationalState();
        _clock.Restart();
        _timer.Start();
        AlignToGroundIfRoaming();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Disable()
    {
        _settings.IsEnabled = false;
        _settingsStore.Save(_settings);
        _timer.Stop();
        _window?.Hide();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool BeginDrag()
    {
        if (_profile.IsPositionLocked)
        {
            return false;
        }

        SetMode(CharacterRuntimeMode.Dragging, _profile.DraggingAnimation, loopFallback: true);
        return true;
    }

    public void EndDrag(double left, double top)
    {
        if (_profile.IsPositionLocked)
        {
            return;
        }

        _profile.Left = left;
        _profile.Top = top;
        _settingsStore.Save(_settings);
        PlayTransient(
            CharacterRuntimeMode.Released,
            _profile.ReleasedAnimation,
            ReturnToOperationalState);
    }

    public void HandleRightClick()
    {
        PlayTransient(
            CharacterRuntimeMode.RightClick,
            _profile.RightClickAnimation,
            ReturnToOperationalState);
    }

    public void TogglePositionLock()
    {
        _profile.IsPositionLocked = !_profile.IsPositionLocked;
        _settingsStore.Save(_settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OpenAssetRoot()
    {
        ReleaseAssetOccupancy();
        Directory.CreateDirectory(_storagePaths.AssetRoot);
        Process.Start(new ProcessStartInfo(_storagePaths.AssetRoot) { UseShellExecute = true });
    }

    /// <summary>
    /// Releases cached asset clips and resets working directory to avoid locking asset files.
    /// </summary>
    public void ReleaseAssetOccupancy()
    {
        _clipCache.Clear();
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new CharacterWindow(this, _windowViewModel);
    }

    private void RestoreWindowPosition()
    {
        if (_window is null)
        {
            return;
        }

        var workArea = _window.GetCurrentWorkArea();
        _window.Left = _profile.Left ?? workArea.Right - _profile.Size - 24d;
        _window.Top = _profile.Top ?? workArea.Bottom - _profile.Size;
        _window.ClampHorizontallyToWorkArea();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var elapsed = _clock.Elapsed;
        _clock.Restart();
        if (elapsed <= TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(1))
        {
            elapsed = TimerInterval;
        }

        AdvanceAnimation(elapsed);
        AdvanceBehavior(elapsed.TotalSeconds);
    }

    private void AdvanceAnimation(TimeSpan elapsed)
    {
        if (_activeClip.IsEmpty)
        {
            if (_transientTimerCompletion is not null &&
                DateTime.UtcNow >= _transientTimerCompletion)
            {
                CompleteTransientMode();
            }

            return;
        }

        if (_transientRemaining > TimeSpan.Zero)
        {
            _transientRemaining -= elapsed;
        }

        var rate = Math.Clamp(_activeAnimation?.PlaybackRate ?? 1d, 0.25d, 3d);
        _frameElapsed += TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
        var duration = _activeClip.FrameDurations[Math.Min(_frameIndex, _activeClip.FrameDurations.Count - 1)];
        if (_frameElapsed < duration)
        {
            return;
        }

        _frameElapsed -= duration;
        if (_frameIndex < _activeClip.Frames.Count - 1)
        {
            _frameIndex++;
            ShowCurrentFrame();
            return;
        }

        if (_activeAnimation?.Loop != false)
        {
            _frameIndex = 0;
            ShowCurrentFrame();
            return;
        }

        if (_transientRemaining <= TimeSpan.Zero)
        {
            CompleteTransientMode();
        }
    }

    private void AdvanceBehavior(double elapsedSeconds)
    {
        if (!_profile.IsFreeMovementEnabled || _isTaskActive || _window is null)
        {
            return;
        }

        if (_mode == CharacterRuntimeMode.Moving)
        {
            var nextLeft = _window.Left + _movementDirection * _profile.MovementSpeed * elapsedSeconds;
            var reachedTarget = _movementDirection > 0
                ? nextLeft >= _movementTargetX
                : nextLeft <= _movementTargetX;
            _window.Left = reachedTarget ? _movementTargetX : nextLeft;
            _window.ClampHorizontallyToWorkArea();
            if (reachedTarget)
            {
                StartIdleBehavior();
            }

            return;
        }

        if (_mode != CharacterRuntimeMode.Idle)
        {
            return;
        }

        _behaviorSecondsRemaining -= elapsedSeconds;
        if (_behaviorSecondsRemaining > 0d)
        {
            return;
        }

        if (_random.NextDouble() < 0.6d)
        {
            StartMovementBehavior();
        }
        else
        {
            StartIdleBehavior();
        }
    }

    private void StartMovementBehavior()
    {
        if (_window is null)
        {
            return;
        }

        var workArea = _window.GetCurrentWorkArea();
        var maximumLeft = Math.Max(workArea.Left, workArea.Right - Math.Max(_window.ActualWidth, _profile.Size));
        var target = workArea.Left + _random.NextDouble() * Math.Max(0d, maximumLeft - workArea.Left);
        if (Math.Abs(target - _window.Left) < Math.Min(48d, workArea.Width / 8d))
        {
            target = _window.Left < workArea.Left + workArea.Width / 2d ? maximumLeft : workArea.Left;
        }

        _movementTargetX = target;
        _movementDirection = target >= _window.Left ? 1 : -1;
        var animation = ResolveMovementAnimation(_movementDirection);
        SetMode(CharacterRuntimeMode.Moving, animation, loopFallback: true);
        _window.AlignFootToWorkArea(_profile.FootAnchorY);
    }

    private CharacterAnimationDefinition ResolveMovementAnimation(int direction)
    {
        if (_profile.UseAutomaticMirror)
        {
            var sourceFacesRight = !string.IsNullOrWhiteSpace(_profile.MoveRightAnimation.SourcePath);
            var source = sourceFacesRight
                ? _profile.MoveRightAnimation
                : _profile.MoveLeftAnimation;
            var shouldMirror = sourceFacesRight ? direction < 0 : direction > 0;
            _windowViewModel.MirrorScale = shouldMirror ? -1d : 1d;
            return source;
        }

        _windowViewModel.MirrorScale = 1d;
        return direction < 0
            ? _profile.MoveLeftAnimation
            : _profile.MoveRightAnimation;
    }

    private void StartIdleBehavior()
    {
        _windowViewModel.MirrorScale = _movementDirection < 0 && _profile.UseAutomaticMirror ? -1d : 1d;
        SetMode(CharacterRuntimeMode.Idle, SelectIdleAnimation(), loopFallback: true);
        _behaviorSecondsRemaining = _profile.MinimumIdleSeconds +
                                    _random.NextDouble() *
                                    (_profile.MaximumIdleSeconds - _profile.MinimumIdleSeconds);
        AlignToGroundIfRoaming();
    }

    private CharacterAnimationDefinition SelectIdleAnimation()
    {
        var available = _profile.IdleAnimations
            .Select((animation, index) => (animation, index))
            .Where(item => !string.IsNullOrWhiteSpace(item.animation.SourcePath))
            .ToList();
        if (available.Count == 0)
        {
            return _profile.IdleAnimations[0];
        }

        var candidates = available.Count > 1
            ? available.Where(item => item.index != _lastIdleIndex).ToList()
            : available;
        var selected = candidates[_random.Next(candidates.Count)];
        _lastIdleIndex = selected.index;
        return selected.animation;
    }

    private void OnTaskStateChanged(object? sender, CharacterTaskStateChangedEventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            switch (e.State)
            {
                case CharacterTaskState.QueueStarted:
                    _isTaskActive = true;
                    _currentTaskKey = string.Empty;
                    _currentTaskName = string.Empty;
                    ApplyTaskRunningState();
                    break;
                case CharacterTaskState.TaskStarted:
                    _isTaskActive = true;
                    _currentTaskKey = e.TaskKey;
                    _currentTaskName = e.TaskName;
                    ApplyTaskRunningState();
                    break;
                case CharacterTaskState.TaskFailed:
                    _isTaskActive = false;
                    PlayTransient(
                        CharacterRuntimeMode.TaskFailed,
                        _profile.TaskFailedAnimation,
                        ReturnToOperationalState);
                    break;
                case CharacterTaskState.QueueSucceeded:
                    _isTaskActive = false;
                    PlayTransient(
                        CharacterRuntimeMode.TaskSucceeded,
                        _profile.TaskSucceededAnimation,
                        ReturnToOperationalState);
                    break;
                case CharacterTaskState.QueueStopped:
                    _isTaskActive = false;
                    ReturnToOperationalState();
                    break;
                case CharacterTaskState.TaskSucceeded:
                    break;
            }
        });
    }

    private void ApplyTaskRunningState()
    {
        if (_mode is CharacterRuntimeMode.Dragging or
            CharacterRuntimeMode.Released or
            CharacterRuntimeMode.RightClick)
        {
            return;
        }

        var rule = _profile.TaskRules.FirstOrDefault(item =>
            string.Equals(item.TaskKey, _currentTaskKey, StringComparison.OrdinalIgnoreCase));
        var animation = rule is { Animation.SourcePath.Length: > 0 }
            ? rule.Animation
            : _profile.TaskRunningAnimation;
        var text = string.IsNullOrWhiteSpace(rule?.StatusText)
            ? _currentTaskName
            : rule.StatusText;
        _windowViewModel.StatusText = TruncateStatusText(text);
        _windowViewModel.IsStatusVisible =
            _profile.ShowStatusBar && !string.IsNullOrWhiteSpace(_windowViewModel.StatusText);
        _windowViewModel.MirrorScale = 1d;
        SetMode(CharacterRuntimeMode.TaskRunning, animation, loopFallback: true);
        AlignToGroundIfRoaming();
    }

    private void ReturnToOperationalState()
    {
        _windowViewModel.StatusText = string.Empty;
        _windowViewModel.IsStatusVisible = false;
        if (_isTaskActive)
        {
            ApplyTaskRunningState();
            return;
        }

        StartIdleBehavior();
    }

    private void PlayTransient(
        CharacterRuntimeMode mode,
        CharacterAnimationDefinition definition,
        Action completion)
    {
        SetMode(mode, definition, loopFallback: false);
        _transientCompletion = completion;
        _transientRemaining = TransientMinimumDuration;
        if (_activeClip.IsEmpty)
        {
            _transientTimerCompletion = DateTime.UtcNow + TransientMinimumDuration;
        }
    }

    private Action? _transientCompletion;
    private DateTime? _transientTimerCompletion;

    private void CompleteTransientMode()
    {
        var completion = _transientCompletion;
        _transientCompletion = null;
        _transientTimerCompletion = null;
        completion?.Invoke();
    }

    private void SetMode(
        CharacterRuntimeMode mode,
        CharacterAnimationDefinition definition,
        bool loopFallback)
    {
        _mode = mode;
        _activeAnimation = definition;
        _activeClip = LoadClip(definition);
        if (_activeClip.IsEmpty && loopFallback)
        {
            _activeAnimation = SelectFallbackAnimation();
            _activeClip = LoadClip(_activeAnimation);
        }

        _frameIndex = 0;
        _frameElapsed = TimeSpan.Zero;
        _transientRemaining = TimeSpan.Zero;
        _transientCompletion = null;
        _transientTimerCompletion = null;
        ShowCurrentFrame();
    }

    private CharacterAnimationDefinition SelectFallbackAnimation() =>
        _profile.IdleAnimations.FirstOrDefault(animation =>
            !string.IsNullOrWhiteSpace(animation.SourcePath))
        ?? _profile.IdleAnimations[0];

    private CharacterAnimationClip LoadClip(CharacterAnimationDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.SourcePath))
        {
            return CharacterAnimationClip.Empty;
        }

        if (_clipCache.TryGetValue(definition.SourcePath, out var cached))
        {
            return cached;
        }

        if (_clipCache.Count >= 32)
        {
            _clipCache.Clear();
        }

        var clip = _animationLoader.Load(definition);
        _clipCache[definition.SourcePath] = clip;
        return clip;
    }

    private void ShowCurrentFrame()
    {
        var frame = _activeClip.Frames.ElementAtOrDefault(_frameIndex);
        _windowViewModel.CurrentFrame = frame;
        if (frame is null || frame.PixelWidth <= 0)
        {
            _windowViewModel.DisplayHeight = _profile.Size;
            return;
        }

        var ratio = (double)frame.PixelHeight / frame.PixelWidth;
        _windowViewModel.DisplayHeight = Math.Clamp(_profile.Size * ratio, 32d, 768d);
        AlignToGroundIfRoaming();
    }

    private void AlignToGroundIfRoaming()
    {
        if (_profile.IsFreeMovementEnabled)
        {
            _window?.AlignFootToWorkArea(_profile.FootAnchorY);
        }
    }

    private static string TruncateStatusText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var elements = StringInfo.ParseCombiningCharacters(text.Trim());
        if (elements.Length <= MaximumStatusTextElements)
        {
            return text.Trim();
        }

        return text.Trim()[..elements[MaximumStatusTextElements]] + "…";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _taskStateService.StateChanged -= OnTaskStateChanged;
        _window?.Close();
        _window = null;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using MFAWPF.Configuration;
using MFAWPF.Extensions;
using MFAWPF.Helper;
using MFAWPF.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;

namespace MFAWPF.ViewModels.UserControl.Settings;

public partial class TimerSettingsUserControlModel : ViewModel
{
    [ObservableProperty] private bool _forceScheduledStart = Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString));

    public ObservableCollection<Tool.LocalizationViewModel> Periods { get; } =
    [
        new("TimeAm"),
        new("TimePm")
    ];

    [ObservableProperty]
    private ObservableCollection<Tool.LocalizationViewModel> _presetOptions = new();

    public TimerModel TimerModels { get; set; }

    public TimerSettingsUserControlModel()
    {
        RefreshPresetOptions();
        TimerModels = new TimerModel();
    }

    public void RefreshPresetOptions()
    {
        var options = new ObservableCollection<Tool.LocalizationViewModel>
        {
            new("DefaultHomeState")
        };

        var storage = new TaskPresetStorageService();
        var presets = storage.LoadPresets();
        foreach (var preset in presets)
        {
            if (!string.IsNullOrWhiteSpace(preset.Name))
            {
                options.Add(new Tool.LocalizationViewModel(preset.Name));
            }
        }

        PresetOptions = options;
    }

    partial void OnForceScheduledStartChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.ForceScheduledStart, value.ToString());
    }

    public partial class TimerModel
    {
        public partial class TimerProperties : ObservableObject
        {
            private bool _isUpdatingFields;
            private int _periodIndex;
            private int _hour12;

            public TimerProperties(int timeId, bool isOn, int hour, int min, string? timerPreset)
            {
                TimerId = timeId;
                _isOn = isOn;
                _hour = Math.Clamp(hour, 0, 23);
                _min = Math.Clamp(min, 0, 59);
                _periodIndex = _hour >= 12 ? 1 : 0;
                _hour12 = _hour == 0 ? 0 : (_hour > 12 ? _hour - 12 : _hour);
                TimerName = $"{"Timer".ToLocalization()} {TimerId + 1}";
                _timerPreset = timerPreset ?? string.Empty;
                LanguageHelper.LanguageChanged += OnLanguageChanged;
            }

            public int TimerId { get; set; }

            [ObservableProperty] private string _timerName;

            private void OnLanguageChanged(object sender, EventArgs e)
            {
                TimerName = $"{"Timer".ToLocalization()} {TimerId + 1}";
            }

            private bool _isOn;

            /// <summary>
            /// Gets or sets a value indicating whether the timer is set.
            /// </summary>
            public bool IsOn
            {
                get => _isOn;
                set
                {
                    SetProperty(ref _isOn, value);
                    GlobalConfiguration.SetTimer(TimerId, value.ToString());
                }
            }

            private int _hour;

            /// <summary>
            /// Gets or sets the hour of the timer.
            /// </summary>
            public int Hour
            {
                get => _hour;
                set
                {
                    value = Math.Clamp(value, 0, 23);
                    if (SetProperty(ref _hour, value))
                    {
                        GlobalConfiguration.SetTimerHour(TimerId, _hour.ToString());
                        Update12HourFields();
                    }
                }
            }

            /// <summary>
            /// Gets or sets the period (0 = AM, 1 = PM).
            /// </summary>
            public int PeriodIndex
            {
                get => _periodIndex;
                set
                {
                    value = Math.Clamp(value, 0, 1);
                    if (SetProperty(ref _periodIndex, value))
                    {
                        Recalculate24Hour();
                    }
                }
            }

            /// <summary>
            /// Gets or sets the 12-hour value (0-12).
            /// </summary>
            public int Hour12
            {
                get => _hour12;
                set
                {
                    value = Math.Clamp(value, 0, 12);
                    if (SetProperty(ref _hour12, value))
                    {
                        Recalculate24Hour();
                    }
                }
            }

            private void Update12HourFields()
            {
                if (_isUpdatingFields)
                {
                    return;
                }

                _isUpdatingFields = true;
                try
                {
                    PeriodIndex = _hour >= 12 ? 1 : 0;
                    Hour12 = _hour == 0 ? 0 : (_hour > 12 ? _hour - 12 : _hour);
                }
                finally
                {
                    _isUpdatingFields = false;
                }
            }

            private void Recalculate24Hour()
            {
                if (_isUpdatingFields)
                {
                    return;
                }

                _isUpdatingFields = true;
                try
                {
                    int h24;
                    if (_periodIndex == 0) // AM
                    {
                        h24 = (_hour12 == 12 || _hour12 == 0) ? 0 : _hour12;
                    }
                    else // PM
                    {
                        h24 = (_hour12 == 12 || _hour12 == 0) ? 12 : _hour12 + 12;
                    }

                    Hour = h24;
                }
                finally
                {
                    _isUpdatingFields = false;
                }
            }

            private int _min;

            /// <summary>
            /// Gets or sets the minute of the timer.
            /// </summary>
            public int Min
            {
                get => _min;
                set
                {
                    value = Math.Clamp(value, 0, 59);
                    SetProperty(ref _min, value);
                    GlobalConfiguration.SetTimerMin(TimerId, _min.ToString());
                }
            }

            private string _timerPreset = string.Empty;

            /// <summary>
            /// Gets or sets the task preset bound to this timer.
            /// </summary>
            public string TimerPreset
            {
                get => string.IsNullOrWhiteSpace(_timerPreset) ? "DefaultHomeState" : _timerPreset;
                set
                {
                    var val = (value == null || value == "DefaultHomeState") ? string.Empty : value;
                    SetProperty(ref _timerPreset, val);
                    GlobalConfiguration.SetTimerPreset(TimerId, _timerPreset);
                }
            }
        }

        public TimerProperties[] Timers { get; set; } = new TimerProperties[8];
        private readonly DispatcherTimer _dispatcherTimer;

        public TimerModel()
        {
            for (var i = 0; i < 8; i++)
            {
                Timers[i] = new TimerProperties(
                    i,
                    GlobalConfiguration.GetTimer(i, bool.FalseString) == bool.TrueString,
                    int.Parse(GlobalConfiguration.GetTimerHour(i, $"{i * 3}")),
                    int.Parse(GlobalConfiguration.GetTimerMin(i, "0")),
                    GlobalConfiguration.GetTimerPreset(i, string.Empty));
            }
            _dispatcherTimer = new DispatcherTimer();
            _dispatcherTimer.Interval = TimeSpan.FromMinutes(1);
            _dispatcherTimer.Tick += CheckTimerElapsed;
            _dispatcherTimer.Start();
        }

        private void CheckTimerElapsed(object? sender, EventArgs e)
        {
            var currentTime = DateTime.Now;
            foreach (var timer in Timers)
            {
                if (timer.IsOn && timer.Hour == currentTime.Hour && timer.Min == currentTime.Minute)
                {
                    ExecuteTimerTask(timer.TimerId);
                }
            }
        }

        private void ExecuteTimerTask(int timerId)
        {
            var timer = Timers.FirstOrDefault(t => t.TimerId == timerId);
            if (timer == null) return;

            if (Convert.ToBoolean(GlobalConfiguration.GetValue(ConfigurationKeys.ForceScheduledStart, bool.FalseString)) && Instances.RootViewModel.IsRunning)
            {
                Instances.TaskQueueView.Stop();
            }

            if (!string.IsNullOrWhiteSpace(timer.TimerPreset) &&
                !timer.TimerPreset.Equals("DefaultHomeState", StringComparison.OrdinalIgnoreCase) &&
                !timer.TimerPreset.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                var storage = new TaskPresetStorageService();
                var presets = storage.LoadPresets();
                var targetPreset = presets.FirstOrDefault(p => p.Name.Equals(timer.TimerPreset, StringComparison.OrdinalIgnoreCase));
                if (targetPreset != null)
                {
                    DispatcherHelper.RunOnMainThread(() =>
                    {
                        Instances.TaskQueueViewModel.ApplyPresetSequence(targetPreset.Items, targetPreset.Name);
                    });
                }
            }

            DispatcherHelper.RunOnMainThread(() =>
            {
                Instances.TaskQueueView.Start();
            });
        }
    }
}

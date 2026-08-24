using System;
using System.Globalization;
using System.Windows.Media.Animation;
using MFAWPF.Configuration;

namespace MFAWPF.Helper.Animations;

public static class AnimationPreferences
{
    private static readonly AnimationProfile PerformanceProfile = new(
        1.003, 0.985, -0.25, 0.5,
        TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(65), TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(140), 220.0, 16.0,
        TimeSpan.FromMilliseconds(130), TimeSpan.FromMilliseconds(120),
        false);

    private static readonly AnimationProfile BalancedProfile = new(
        1.006, 0.98, -0.6, 0.8,
        TimeSpan.FromMilliseconds(140), TimeSpan.FromMilliseconds(85), TimeSpan.FromMilliseconds(180),
        TimeSpan.FromMilliseconds(210), 320.0, 38.0,
        TimeSpan.FromMilliseconds(260), TimeSpan.FromMilliseconds(180),
        false);

    private static readonly AnimationProfile ComfortProfile = new(
        1.009, 0.972, -0.9, 1.0,
        TimeSpan.FromMilliseconds(210), TimeSpan.FromMilliseconds(110), TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(280), 420.0, 50.0,
        TimeSpan.FromMilliseconds(320), TimeSpan.FromMilliseconds(220),
        true);

    private static AnimationLevel _currentLevel = ReadConfiguredLevel();

    public static AnimationLevel CurrentLevel
    {
        get => _currentLevel;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (_currentLevel != value)
            {
                _currentLevel = value;
                GlobalConfiguration.SetValue(ConfigurationKeys.AnimationLevel, ((int)value).ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    public static AnimationProfile CurrentProfile => CurrentLevel switch
    {
        AnimationLevel.Performance => PerformanceProfile,
        AnimationLevel.Comfort => ComfortProfile,
        _ => BalancedProfile,
    };

    public static double CompactHoverScale => CurrentLevel switch
    {
        AnimationLevel.Performance => 1.03,
        AnimationLevel.Comfort => 1.08,
        _ => 1.055,
    };

    public static double CompactPressedScale => CurrentLevel switch
    {
        AnimationLevel.Performance => 0.92,
        AnimationLevel.Comfort => 0.84,
        _ => 0.88,
    };

    public static IEasingFunction CreateEaseOut() => CurrentLevel switch
    {
        AnimationLevel.Performance => new CubicEase { EasingMode = EasingMode.EaseOut },
        AnimationLevel.Comfort => new QuinticEase { EasingMode = EasingMode.EaseOut },
        _ => new QuarticEase { EasingMode = EasingMode.EaseOut },
    };

    public static IEasingFunction CreateScrollEase() => CurrentLevel switch
    {
        AnimationLevel.Performance => new QuadraticEase { EasingMode = EasingMode.EaseOut },
        AnimationLevel.Comfort => new CubicEase { EasingMode = EasingMode.EaseOut },
        _ => new QuadraticEase { EasingMode = EasingMode.EaseOut },
    };

    public static IEasingFunction CreateReturnEase() =>
        CurrentProfile.UsesSpringReturn
            ? new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut }
            : CreateEaseOut();

    private static AnimationLevel ReadConfiguredLevel()
    {
        string defaultValue = ((int)AnimationLevel.Balanced).ToString(CultureInfo.InvariantCulture);
        string value = GlobalConfiguration.GetValue(ConfigurationKeys.AnimationLevel, defaultValue);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) &&
            Enum.IsDefined(typeof(AnimationLevel), result))
        {
            return (AnimationLevel)result;
        }
        if (Enum.TryParse<AnimationLevel>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }
        return AnimationLevel.Balanced;
    }
}

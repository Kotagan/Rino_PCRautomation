using System;

namespace MFAWPF.Helper.Animations;

public readonly record struct AnimationProfile(
    double ButtonHoverScale,
    double ButtonPressedScale,
    double ButtonHoverLift,
    double ButtonPressedOffset,
    TimeSpan ButtonHoverDuration,
    TimeSpan ButtonPressDuration,
    TimeSpan ButtonReturnDuration,
    TimeSpan ScrollDuration,
    double ScrollDistancePerNotch,
    double TabSlideDistance,
    TimeSpan TabSlideDuration,
    TimeSpan SelectionDuration,
    bool UsesSpringReturn);

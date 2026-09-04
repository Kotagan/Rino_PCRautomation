using CommunityToolkit.Mvvm.ComponentModel;

namespace MFAWPF.Models.Characters;

internal sealed class CharacterSettings
{
    public bool IsEnabled { get; set; }

    public string ActiveProfileId { get; set; } = string.Empty;

    public List<CharacterProfile> Profiles { get; set; } = [];

    public static CharacterSettings CreateDefault()
    {
        var profile = CharacterProfile.CreateDefault();
        return new CharacterSettings
        {
            ActiveProfileId = profile.Id,
            Profiles = [profile],
        };
    }

    public CharacterProfile GetActiveProfile()
    {
        Normalize();
        return Profiles.First(profile => profile.Id == ActiveProfileId);
    }

    public void Normalize()
    {
        Profiles ??= [];
        if (Profiles.Count == 0)
        {
            Profiles.Add(CharacterProfile.CreateDefault());
        }

        foreach (var profile in Profiles)
        {
            profile.Normalize();
        }

        if (Profiles.All(profile => profile.Id != ActiveProfileId))
        {
            ActiveProfileId = Profiles[0].Id;
        }
    }
}

internal sealed partial class CharacterProfile : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] private string _name = "Character";

    public double Size { get; set; } = 180d;

    public bool ShowStatusBar { get; set; } = true;

    public bool IsPositionLocked { get; set; }

    public bool IsFreeMovementEnabled { get; set; }

    public double MovementSpeed { get; set; } = 60d;

    public double MinimumIdleSeconds { get; set; } = 2d;

    public double MaximumIdleSeconds { get; set; } = 6d;

    public bool UseAutomaticMirror { get; set; } = true;

    public double FootAnchorX { get; set; } = 0.5d;

    public double FootAnchorY { get; set; } = 1d;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public List<CharacterAnimationDefinition> IdleAnimations { get; set; } = [];

    public CharacterAnimationDefinition MoveLeftAnimation { get; set; } = new();

    public CharacterAnimationDefinition MoveRightAnimation { get; set; } = new();

    public CharacterAnimationDefinition TaskRunningAnimation { get; set; } = new();

    public CharacterAnimationDefinition TaskSucceededAnimation { get; set; } = CharacterAnimationDefinition.CreateTransient();

    public CharacterAnimationDefinition TaskFailedAnimation { get; set; } = CharacterAnimationDefinition.CreateTransient();

    public CharacterAnimationDefinition DraggingAnimation { get; set; } = new();

    public CharacterAnimationDefinition ReleasedAnimation { get; set; } = CharacterAnimationDefinition.CreateTransient();

    public CharacterAnimationDefinition RightClickAnimation { get; set; } = CharacterAnimationDefinition.CreateTransient();

    public List<CharacterTaskAnimationRule> TaskRules { get; set; } = [];

    public static CharacterProfile CreateDefault()
    {
        var profile = new CharacterProfile();
        profile.Normalize();
        return profile;
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        Name = string.IsNullOrWhiteSpace(Name) ? "Character" : Name.Trim();
        Size = Math.Clamp(Size, 64d, 512d);
        MovementSpeed = Math.Clamp(MovementSpeed, 10d, 300d);
        MinimumIdleSeconds = Math.Clamp(MinimumIdleSeconds, 1d, 30d);
        MaximumIdleSeconds = Math.Clamp(MaximumIdleSeconds, MinimumIdleSeconds, 60d);
        FootAnchorX = Math.Clamp(FootAnchorX, 0d, 1d);
        FootAnchorY = Math.Clamp(FootAnchorY, 0d, 1d);
        IdleAnimations ??= [];
        while (IdleAnimations.Count < 6)
        {
            IdleAnimations.Add(new CharacterAnimationDefinition());
        }

        if (IdleAnimations.Count > 6)
        {
            IdleAnimations = IdleAnimations.Take(6).ToList();
        }

        foreach (var animation in IdleAnimations)
        {
            animation.Normalize();
        }

        MoveLeftAnimation ??= new CharacterAnimationDefinition();
        MoveRightAnimation ??= new CharacterAnimationDefinition();
        TaskRunningAnimation ??= new CharacterAnimationDefinition();
        TaskSucceededAnimation ??= CharacterAnimationDefinition.CreateTransient();
        TaskFailedAnimation ??= CharacterAnimationDefinition.CreateTransient();
        DraggingAnimation ??= new CharacterAnimationDefinition();
        ReleasedAnimation ??= CharacterAnimationDefinition.CreateTransient();
        RightClickAnimation ??= CharacterAnimationDefinition.CreateTransient();
        TaskRules ??= [];
        MoveLeftAnimation.Normalize();
        MoveRightAnimation.Normalize();
        TaskRunningAnimation.Normalize();
        TaskSucceededAnimation.Normalize();
        TaskFailedAnimation.Normalize();
        DraggingAnimation.Normalize();
        ReleasedAnimation.Normalize();
        RightClickAnimation.Normalize();
        foreach (var rule in TaskRules)
        {
            rule.Animation ??= new CharacterAnimationDefinition();
            rule.Animation.Normalize();
            rule.TaskKey = rule.TaskKey?.Trim() ?? string.Empty;
            rule.TaskName = rule.TaskName?.Trim() ?? string.Empty;
            rule.StatusText = rule.StatusText?.Trim() ?? string.Empty;
        }
    }
}

internal sealed class CharacterAnimationDefinition
{
    public string SourcePath { get; set; } = string.Empty;

    public double PlaybackRate { get; set; } = 1d;

    public bool Loop { get; set; } = true;

    public void Normalize()
    {
        SourcePath = SourcePath?.Trim() ?? string.Empty;
        PlaybackRate = Math.Clamp(PlaybackRate, 0.25d, 3d);
    }

    public static CharacterAnimationDefinition CreateTransient() => new()
    {
        Loop = false,
    };
}

internal sealed class CharacterTaskAnimationRule
{
    public string TaskKey { get; set; } = string.Empty;

    public string TaskName { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    public CharacterAnimationDefinition Animation { get; set; } = new();
}

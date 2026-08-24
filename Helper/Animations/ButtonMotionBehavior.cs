using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MFAWPF.Helper.Animations;

public static class ButtonMotionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ButtonMotionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.RegisterAttached(
            "Variant",
            typeof(string),
            typeof(ButtonMotionBehavior),
            new PropertyMetadata("Standard"));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static string GetVariant(DependencyObject obj) => (string)obj.GetValue(VariantProperty);
    public static void SetVariant(DependencyObject obj, string value) => obj.SetValue(VariantProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;

        if ((bool)e.NewValue)
        {
            button.MouseEnter += OnMouseEnter;
            button.MouseLeave += OnMouseLeave;
            button.PreviewMouseDown += OnMouseDown;
            button.PreviewMouseUp += OnMouseUp;
        }
        else
        {
            button.MouseEnter -= OnMouseEnter;
            button.MouseLeave -= OnMouseLeave;
            button.PreviewMouseDown -= OnMouseDown;
            button.PreviewMouseUp -= OnMouseUp;
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateScale(el, AnimationPreferences.CurrentProfile.ButtonHoverScale, AnimationPreferences.CurrentProfile.ButtonHoverDuration);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateScale(el, 1.0, AnimationPreferences.CurrentProfile.ButtonReturnDuration);
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateScale(el, AnimationPreferences.CurrentProfile.ButtonPressedScale, AnimationPreferences.CurrentProfile.ButtonPressDuration);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el) AnimateScale(el, el.IsMouseOver ? AnimationPreferences.CurrentProfile.ButtonHoverScale : 1.0, AnimationPreferences.CurrentProfile.ButtonReturnDuration);
    }

    private static void AnimateScale(FrameworkElement element, double targetScale, TimeSpan duration)
    {
        if (element.RenderTransform is not ScaleTransform scaleTransform)
        {
            scaleTransform = new ScaleTransform(1.0, 1.0);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scaleTransform;
        }

        var ease = AnimationPreferences.CreateEaseOut();
        var animX = new DoubleAnimation { To = targetScale, Duration = duration, EasingFunction = ease };
        var animY = new DoubleAnimation { To = targetScale, Duration = duration, EasingFunction = ease };

        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }
}

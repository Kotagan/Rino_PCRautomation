using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MFAWPF.Helper.Animations
{
    /// <summary>
    /// Attached behavior for ListBox that smoothly animates a floating selection pill indicator
    /// to the position of the newly selected ListBoxItem.
    /// </summary>
    public static class ListBoxSmoothSelectionBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(ListBoxSmoothSelectionBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                listBox.SelectionChanged += OnSelectionChanged;
                listBox.Loaded += OnListBoxLoaded;
            }
            else
            {
                listBox.SelectionChanged -= OnSelectionChanged;
                listBox.Loaded -= OnListBoxLoaded;
            }
        }

        private static void OnListBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                AnimatePillToSelectedItem(listBox, true);
            }
        }

        private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && e.Source == listBox)
            {
                AnimatePillToSelectedItem(listBox, false);
            }
        }

        public static void AnimatePillToSelectedItem(ListBox listBox, bool immediate)
        {
            if (listBox.SelectedIndex < 0)
            {
                return;
            }

            listBox.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                if (listBox.Template.FindName("SelectionPill", listBox) is not FrameworkElement pill)
                {
                    return;
                }

                if (listBox.ItemContainerGenerator.ContainerFromIndex(listBox.SelectedIndex) is not FrameworkElement container)
                {
                    return;
                }

                try
                {
                    Point relativePoint = container.TransformToAncestor(listBox).Transform(new Point(0, 0));
                    double targetY = relativePoint.Y;
                    double targetHeight = container.ActualHeight;

                    TranslateTransform? translateTransform = pill.RenderTransform as TranslateTransform;
                    if (translateTransform == null || translateTransform.IsFrozen || translateTransform.IsSealed)
                    {
                        translateTransform = translateTransform?.Clone() ?? new TranslateTransform();
                        pill.RenderTransform = translateTransform;
                    }

                    if (immediate)
                    {
                        translateTransform.Y = targetY;
                        if (targetHeight > 0) pill.Height = targetHeight;
                        pill.Opacity = 1.0;
                    }
                    else
                    {
                        var profile = AnimationPreferences.CurrentProfile;
                        DoubleAnimation slideYAnimation = new DoubleAnimation
                        {
                            To = targetY,
                            Duration = profile.SelectionDuration,
                            EasingFunction = AnimationPreferences.CreateEaseOut()
                        };

                        DoubleAnimation heightAnimation = new DoubleAnimation
                        {
                            To = targetHeight > 0 ? targetHeight : pill.Height,
                            Duration = profile.SelectionDuration,
                            EasingFunction = AnimationPreferences.CreateEaseOut()
                        };

                        DoubleAnimation fadeAnimation = new DoubleAnimation
                        {
                            To = 1.0,
                            Duration = profile.SelectionDuration,
                            EasingFunction = AnimationPreferences.CreateEaseOut()
                        };

                        translateTransform.BeginAnimation(TranslateTransform.YProperty, slideYAnimation);
                        if (targetHeight > 0)
                        {
                            pill.BeginAnimation(FrameworkElement.HeightProperty, heightAnimation);
                        }
                        pill.BeginAnimation(UIElement.OpacityProperty, fadeAnimation);
                    }
                }
                catch
                {
                }
            });
        }
    }
}

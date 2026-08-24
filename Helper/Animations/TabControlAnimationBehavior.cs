using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CardControl = MFAWPF.Views.UserControl.CardControl;

namespace MFAWPF.Helper.Animations
{
    /// <summary>
    /// Attached behavior providing Apple-style fluid page transition animations for <see cref="TabControl" />.
    /// Features direction-aware horizontal sliding, subtle depth scaling, smooth opacity fading, and cascading card entrances.
    /// </summary>
    public static class TabControlAnimationBehavior
    {
        private static bool _isInitialized;

        /// <summary>
        /// Enables or disables the page transition for a <see cref="TabControl" />.
        /// </summary>
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(TabControlAnimationBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        private static readonly DependencyProperty PreviousIndexProperty =
            DependencyProperty.RegisterAttached(
                "PreviousIndex",
                typeof(int),
                typeof(TabControlAnimationBehavior),
                new PropertyMetadata(-1));

        private static readonly DependencyProperty MotionTransformsProperty =
            DependencyProperty.RegisterAttached(
                "MotionTransforms",
                typeof(MotionTransformsHolder),
                typeof(TabControlAnimationBehavior),
                new PropertyMetadata(null));

        private static readonly DependencyProperty ActiveEntranceTargetsProperty =
            DependencyProperty.RegisterAttached(
                "ActiveEntranceTargets",
                typeof(List<EntranceTargetState>),
                typeof(TabControlAnimationBehavior),
                new PropertyMetadata(null));

        /// <summary>
        /// Gets whether the page transition is enabled.
        /// </summary>
        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        /// <summary>
        /// Enables or disables the page transition.
        /// </summary>
        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        /// <summary>
        /// Initializes global TabControl animation behavior.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;
            _isInitialized = true;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TabControl tabControl)
            {
                return;
            }

            if ((bool)e.NewValue)
            {
                tabControl.SelectionChanged -= OnSelectionChanged;
                tabControl.SelectionChanged += OnSelectionChanged;
                tabControl.Loaded -= OnTabControlLoaded;
                tabControl.Loaded += OnTabControlLoaded;

                if (tabControl.IsLoaded && tabControl.SelectedIndex >= 0)
                {
                    tabControl.SetValue(PreviousIndexProperty, tabControl.SelectedIndex);
                }
            }
            else
            {
                tabControl.SelectionChanged -= OnSelectionChanged;
                tabControl.Loaded -= OnTabControlLoaded;
            }
        }

        private static void OnTabControlLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TabControl tabControl) return;

            if (GetIsEnabled(tabControl))
            {
                int currentIndex = tabControl.SelectedIndex;
                if (currentIndex >= 0 && (int)tabControl.GetValue(PreviousIndexProperty) == -1)
                {
                    tabControl.SetValue(PreviousIndexProperty, currentIndex);
                }
            }
        }

        private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not TabControl tabControl || !ReferenceEquals(e.Source, tabControl))
            {
                return;
            }

            if (!GetIsEnabled(tabControl))
            {
                return;
            }

            int newIndex = tabControl.SelectedIndex;
            if (newIndex < 0 || e.AddedItems.Count == 0)
            {
                return;
            }

            CancelActivePageEntrance(tabControl);

            int oldIndex = (int)tabControl.GetValue(PreviousIndexProperty);
            tabControl.SetValue(PreviousIndexProperty, newIndex);

            if (oldIndex == -1 || oldIndex == newIndex)
            {
                return;
            }

            tabControl.ApplyTemplate();
            AnimatePageTransition(tabControl, oldIndex, newIndex);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var nested = FindVisualChild<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private static void AnimatePageTransition(
            TabControl tabControl,
            int oldIndex,
            int newIndex)
        {
            double width = tabControl.ActualWidth;
            if (width <= 0 && Application.Current?.MainWindow != null)
            {
                width = Application.Current.MainWindow.ActualWidth;
            }
            if (width <= 0)
            {
                width = 1200.0;
            }

            double slideDistance = Math.Clamp(width * 0.22, 160.0, 360.0);
            double startX = (newIndex > oldIndex) ? slideDistance : -slideDistance;
            double fromScale = 0.975;
            var duration = new Duration(TimeSpan.FromMilliseconds(280));
            var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

            FrameworkElement? contentHost = tabControl.Template?.FindName("PART_SelectedContentHost", tabControl) as FrameworkElement;
            FrameworkElement? selectedContent = tabControl.SelectedContent as FrameworkElement;

            if (contentHost != null)
            {
                AnimateElement(contentHost, startX, fromScale, duration, easeOut);
            }
            else if (selectedContent != null)
            {
                AnimateElement(selectedContent, startX, fromScale, duration, easeOut);
            }

            if (selectedContent != null)
            {
                AnimatePageContentCascade(tabControl, selectedContent, easeOut);
            }
        }

        private static void AnimateElement(
            FrameworkElement element,
            double startX,
            double fromScale,
            Duration duration,
            IEasingFunction easeOut)
        {
            var transforms = GetOrCreateTransforms(element);
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            element.BeginAnimation(UIElement.OpacityProperty, null);
            transforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
            transforms.Translate.BeginAnimation(TranslateTransform.YProperty, null);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration,
                EasingFunction = easeOut,
                FillBehavior = FillBehavior.HoldEnd,
            };

            var slideXAnim = new DoubleAnimation
            {
                From = startX,
                To = 0.0,
                Duration = duration,
                EasingFunction = easeOut,
                FillBehavior = FillBehavior.HoldEnd,
            };

            var scaleXAnim = new DoubleAnimation
            {
                From = fromScale,
                To = 1.0,
                Duration = duration,
                EasingFunction = easeOut,
                FillBehavior = FillBehavior.HoldEnd,
            };

            var scaleYAnim = new DoubleAnimation
            {
                From = fromScale,
                To = 1.0,
                Duration = duration,
                EasingFunction = easeOut,
                FillBehavior = FillBehavior.HoldEnd,
            };

            slideXAnim.Completed += (_, _) =>
            {
                transforms.Translate.BeginAnimation(TranslateTransform.XProperty, null);
                transforms.Translate.X = 0.0;
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                transforms.Scale.ScaleX = 1.0;
                transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                transforms.Scale.ScaleY = 1.0;
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = 1.0;
            };

            element.BeginAnimation(UIElement.OpacityProperty, opacityAnim, HandoffBehavior.SnapshotAndReplace);
            transforms.Translate.BeginAnimation(TranslateTransform.XProperty, slideXAnim, HandoffBehavior.SnapshotAndReplace);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim, HandoffBehavior.SnapshotAndReplace);
            transforms.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimatePageContentCascade(
            TabControl tabControl,
            FrameworkElement contentHost,
            IEasingFunction easingFunction)
        {
            var pageRoot = ResolvePageRoot(contentHost);
            var targets = GetEntranceTargets(pageRoot);
            if (targets.Count == 0)
            {
                return;
            }

            const double EntranceStartOpacityRatio = 0.65;
            var activeTargets = new List<EntranceTargetState>();
            int maxStaggerCount = Math.Min(targets.Count, 8);

            for (var index = 0; index < maxStaggerCount; index++)
            {
                var target = targets[index];
                double targetOpacity = Math.Clamp(target.Opacity, 0, 1);
                if (targetOpacity <= 0)
                {
                    continue;
                }

                activeTargets.Add(new EntranceTargetState(target, targetOpacity));
                target.BeginAnimation(UIElement.OpacityProperty, null);
                double startOpacity = targetOpacity * EntranceStartOpacityRatio;
                target.Opacity = startOpacity;

                var fadeAnimation = new DoubleAnimation
                {
                    From = startOpacity,
                    To = targetOpacity,
                    BeginTime = TimeSpan.FromMilliseconds(index * 16),
                    Duration = TimeSpan.FromMilliseconds(180),
                    EasingFunction = easingFunction,
                    FillBehavior = FillBehavior.HoldEnd,
                };

                fadeAnimation.Completed += (_, _) =>
                {
                    target.BeginAnimation(UIElement.OpacityProperty, null);
                    target.Opacity = targetOpacity;
                };

                target.BeginAnimation(UIElement.OpacityProperty, fadeAnimation, HandoffBehavior.SnapshotAndReplace);
            }

            tabControl.SetValue(ActiveEntranceTargetsProperty, activeTargets);
        }

        private static MotionTransformsHolder GetOrCreateTransforms(FrameworkElement element)
        {
            if (element.GetValue(MotionTransformsProperty) is MotionTransformsHolder existing)
            {
                return existing;
            }

            ScaleTransform scale;
            TranslateTransform translate;

            if (element.RenderTransform is TransformGroup group &&
                group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } st &&
                group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } tt)
            {
                scale = st;
                translate = tt;
            }
            else
            {
                var newGroup = new TransformGroup();
                if (element.RenderTransform != null && element.RenderTransform != Transform.Identity)
                {
                    newGroup.Children.Add(element.RenderTransform.CloneCurrentValue());
                }

                scale = new ScaleTransform(1.0, 1.0);
                translate = new TranslateTransform(0.0, 0.0);
                newGroup.Children.Add(scale);
                newGroup.Children.Add(translate);

                element.RenderTransform = newGroup;
            }

            var holder = new MotionTransformsHolder(scale, translate);
            element.SetValue(MotionTransformsProperty, holder);
            return holder;
        }

        private static FrameworkElement ResolvePageRoot(FrameworkElement element)
        {
            while (true)
            {
                FrameworkElement? child = null;
                if (element is ContentPresenter presenter && presenter.Content is FrameworkElement presenterContent)
                {
                    child = presenterContent;
                }
                else if (element is ContentControl contentControl && contentControl.Content is FrameworkElement content)
                {
                    child = content;
                }
                else if (element is Decorator decorator && decorator.Child is FrameworkElement decoratorChild)
                {
                    child = decoratorChild;
                }

                if (child == null || ReferenceEquals(child, element))
                {
                    return element;
                }

                element = child;
            }
        }

        private static List<FrameworkElement> GetEntranceTargets(FrameworkElement pageRoot)
        {
            var targets = new List<FrameworkElement>();
            CollectEntranceTargets(pageRoot, targets, true);
            return targets;
        }

        private static void CollectEntranceTargets(
            FrameworkElement element,
            List<FrameworkElement> targets,
            bool isPageRoot)
        {
            if (element.Visibility != Visibility.Visible)
            {
                return;
            }

            if (!isPageRoot && IsEntranceRow(element))
            {
                targets.Add(element);
                return;
            }

            if (element is Panel panel)
            {
                var children = panel.Children
                    .OfType<FrameworkElement>()
                    .Where(child => child.Visibility == Visibility.Visible)
                    .ToList();

                if (ShouldAnimateChildren(panel, children))
                {
                    targets.AddRange(children);
                    return;
                }

                foreach (var child in children)
                {
                    CollectEntranceTargets(child, targets, false);
                }

                return;
            }

            if (element is ContentControl contentControl && contentControl.Content is FrameworkElement content)
            {
                CollectEntranceTargets(content, targets, false);
                return;
            }

            if (element is Decorator decorator && decorator.Child is FrameworkElement decoratorChild)
            {
                CollectEntranceTargets(decoratorChild, targets, false);
            }
        }

        private static bool IsEntranceRow(FrameworkElement element) =>
            element is Border or CardControl or ListBoxItem;

        private static bool ShouldAnimateChildren(Panel panel, IReadOnlyCollection<FrameworkElement> children) =>
            children.Count >= 2 && (panel is StackPanel or WrapPanel or UniformGrid || children.Count >= 3);

        private static void CancelActivePageEntrance(TabControl tabControl)
        {
            if (tabControl.GetValue(ActiveEntranceTargetsProperty) is not List<EntranceTargetState> targets)
            {
                return;
            }

            foreach (var target in targets)
            {
                target.Element.BeginAnimation(UIElement.OpacityProperty, null);
                target.Element.Opacity = target.OriginalOpacity;
            }

            tabControl.ClearValue(ActiveEntranceTargetsProperty);
        }

        private sealed record MotionTransformsHolder(ScaleTransform Scale, TranslateTransform Translate);
        private sealed record EntranceTargetState(FrameworkElement Element, double OriginalOpacity);
    }
}

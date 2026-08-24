using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MFAWPF.Helper.Animations
{
    /// <summary>
    /// Provides continuous mouse-wheel scrolling for a <see cref="ScrollViewer" />.
    /// </summary>
    public static class SmoothScrollViewerBehavior
    {
        /// <summary>
        /// Enables continuous mouse-wheel scrolling on a <see cref="ScrollViewer" />.
        /// </summary>
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SmoothScrollViewerBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        /// <summary>
        /// Gets or sets the target vertical offset used by the smooth scroll controller.
        /// </summary>
        public static readonly DependencyProperty TargetVerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "TargetVerticalOffset",
                typeof(double),
                typeof(SmoothScrollViewerBehavior),
                new PropertyMetadata(0.0, OnTargetVerticalOffsetChanged));

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State",
                typeof(ScrollState),
                typeof(SmoothScrollViewerBehavior),
                new PropertyMetadata(null));

        /// <summary>
        /// Gets whether smooth scrolling is enabled for the specified object.
        /// </summary>
        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

        /// <summary>
        /// Enables or disables smooth scrolling for the specified object.
        /// </summary>
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        /// <summary>
        /// Gets the current smooth-scroll target offset.
        /// </summary>
        public static double GetTargetVerticalOffset(DependencyObject obj) =>
            (double)obj.GetValue(TargetVerticalOffsetProperty);

        /// <summary>
        /// Sets the smooth-scroll target offset.
        /// </summary>
        public static void SetTargetVerticalOffset(DependencyObject obj, double value) =>
            obj.SetValue(TargetVerticalOffsetProperty, value);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer scrollViewer)
            {
                return;
            }

            var state = GetState(scrollViewer);
            if ((bool)e.NewValue)
            {
                scrollViewer.Loaded += OnLoaded;
                scrollViewer.Unloaded += OnUnloaded;
                scrollViewer.ScrollChanged += OnScrollChanged;
                scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
                scrollViewer.PreviewMouseDown += OnPreviewMouseDown;
                SyncTargetToActual(scrollViewer, state);
            }
            else
            {
                scrollViewer.Loaded -= OnLoaded;
                scrollViewer.Unloaded -= OnUnloaded;
                scrollViewer.ScrollChanged -= OnScrollChanged;
                scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
                scrollViewer.PreviewMouseDown -= OnPreviewMouseDown;
                StopAnimation(state);
                state.HasTarget = false;
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                SyncTargetToActual(scrollViewer, GetState(scrollViewer));
            }
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                var state = GetState(scrollViewer);
                StopAnimation(state);
                state.HasTarget = false;
            }
        }

        private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            var state = GetState(scrollViewer);
            StopAnimation(state);
            SyncTargetToActual(scrollViewer, state);
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            var state = GetState(scrollViewer);
            if (!state.HasTarget)
            {
                SyncTargetToActual(scrollViewer, state);
            }

            double notchCount = e.Delta / 120d;
            double scrollDistance = -notchCount * AnimationPreferences.CurrentProfile.ScrollDistancePerNotch;
            double newTarget = ClampOffset(
                state.TargetOffset + scrollDistance,
                scrollViewer.ScrollableHeight);

            if (Math.Abs(newTarget - state.TargetOffset) < 0.1)
            {
                return;
            }

            state.TargetOffset = newTarget;
            state.HasTarget = true;
            StartAnimation(scrollViewer, state);
            e.Handled = true;
        }

        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            var state = GetState(scrollViewer);
            if (state.IsApplyingOffset ||
                (state.IsAnimating && Math.Abs(e.VerticalOffset - state.LastAppliedOffset) < 0.5))
            {
                return;
            }

            if (e.VerticalChange != 0 || e.ExtentHeightChange != 0)
            {
                StopAnimation(state);
                SyncTargetToActual(scrollViewer, state);
            }
        }

        private static void OnTargetVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer scrollViewer || e.NewValue is not double value || !double.IsFinite(value))
            {
                return;
            }

            if (!GetIsEnabled(scrollViewer))
            {
                scrollViewer.ScrollToVerticalOffset(value);
                return;
            }

            var state = GetState(scrollViewer);
            state.TargetOffset = ClampOffset(value, scrollViewer.ScrollableHeight);
            state.HasTarget = true;
            StartAnimation(scrollViewer, state);
        }

        private static void StartAnimation(ScrollViewer scrollViewer, ScrollState state)
        {
            if (state.IsAnimating)
            {
                return;
            }

            state.IsAnimating = true;
            state.LastRenderingTime = TimeSpan.Zero;
            state.RenderingHandler = (_, args) =>
                UpdateScroll(scrollViewer, state, ((RenderingEventArgs)args).RenderingTime);
            CompositionTarget.Rendering += state.RenderingHandler;
        }

        private static void UpdateScroll(ScrollViewer scrollViewer, ScrollState state, TimeSpan renderingTime)
        {
            if (!scrollViewer.IsLoaded)
            {
                StopAnimation(state);
                return;
            }

            double targetOffset = ClampOffset(state.TargetOffset, scrollViewer.ScrollableHeight);
            state.TargetOffset = targetOffset;
            double currentOffset = scrollViewer.VerticalOffset;
            double remainingDistance = targetOffset - currentOffset;

            if (Math.Abs(remainingDistance) < 0.5)
            {
                ApplyOffset(scrollViewer, state, targetOffset);
                StopAnimation(state);
                return;
            }

            double elapsedSeconds = state.LastRenderingTime == TimeSpan.Zero
                ? 1d / 60d
                : (renderingTime - state.LastRenderingTime).TotalSeconds;
            state.LastRenderingTime = renderingTime;

            if (elapsedSeconds <= 0 || elapsedSeconds > 0.1)
            {
                elapsedSeconds = 1d / 60d;
            }

            var profile = AnimationPreferences.CurrentProfile;
            double durationSeconds = Math.Max(profile.ScrollDuration.TotalSeconds, 0.05);
            double pixelsPerSecond = profile.ScrollDistancePerNotch / durationSeconds;
            double step = Math.Min(Math.Abs(remainingDistance), pixelsPerSecond * elapsedSeconds);
            double nextOffset = currentOffset + Math.Sign(remainingDistance) * step;

            ApplyOffset(scrollViewer, state, nextOffset);
        }

        private static void ApplyOffset(ScrollViewer scrollViewer, ScrollState state, double offset)
        {
            state.LastAppliedOffset = offset;
            state.IsApplyingOffset = true;
            try
            {
                scrollViewer.ScrollToVerticalOffset(offset);
            }
            finally
            {
                state.IsApplyingOffset = false;
            }
        }

        private static void StopAnimation(ScrollState state)
        {
            if (state.RenderingHandler != null)
            {
                CompositionTarget.Rendering -= state.RenderingHandler;
                state.RenderingHandler = null;
            }

            state.IsAnimating = false;
            state.LastRenderingTime = TimeSpan.Zero;
        }

        private static void SyncTargetToActual(ScrollViewer scrollViewer, ScrollState state)
        {
            state.TargetOffset = ClampOffset(scrollViewer.VerticalOffset, scrollViewer.ScrollableHeight);
            state.LastAppliedOffset = state.TargetOffset;
            state.HasTarget = true;
        }

        private static double ClampOffset(double offset, double scrollableHeight)
        {
            if (!double.IsFinite(offset))
            {
                return 0;
            }

            return Math.Clamp(offset, 0, Math.Max(0, scrollableHeight));
        }

        private static ScrollState GetState(ScrollViewer scrollViewer)
        {
            if (scrollViewer.GetValue(StateProperty) is ScrollState state)
            {
                return state;
            }

            state = new ScrollState();
            scrollViewer.SetValue(StateProperty, state);
            return state;
        }

        private sealed class ScrollState
        {
            public bool HasTarget { get; set; }

            public bool IsAnimating { get; set; }

            public bool IsApplyingOffset { get; set; }

            public double LastAppliedOffset { get; set; }

            public TimeSpan LastRenderingTime { get; set; }

            public EventHandler? RenderingHandler { get; set; }

            public double TargetOffset { get; set; }
        }
    }
}

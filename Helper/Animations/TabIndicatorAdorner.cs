using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MFAWPF.Helper.Animations
{
    /// <summary>
    /// Adorner that renders a floating active indicator bar beneath TabControl items,
    /// smoothly sliding horizontally between tabs with trailing inertia and elastic damping.
    /// </summary>
    public sealed class TabIndicatorAdorner : Adorner
    {
        private readonly Border _indicator;
        private readonly TranslateTransform _translateTransform;
        private readonly VisualCollection _visuals;
        private double _currentY;

        public TabIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            _translateTransform = new TranslateTransform();

            _indicator = new Border
            {
                Height = 3.0,
                CornerRadius = new CornerRadius(1.5, 1.5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                RenderTransform = _translateTransform,
                Opacity = 0.0,
            };
            _indicator.SetResourceReference(Border.BackgroundProperty, "PrimaryBrush");

            _visuals = new VisualCollection(this) { _indicator };
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => _visuals?.Count ?? 0;

        protected override Visual GetVisualChild(int index)
        {
            if (_visuals == null || index < 0 || index >= _visuals.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _visuals[index];
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _indicator.Measure(constraint);
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _indicator.Arrange(new Rect(0, _currentY, _indicator.Width > 0 ? _indicator.Width : 60.0, 3.0));
            return finalSize;
        }

        /// <summary>
        /// Moves the indicator to the target TabItem with trailing inertia animation or immediately.
        /// </summary>
        public void MoveToTab(TabItem tabItem, bool immediate)
        {
            try
            {
                if (!tabItem.IsVisible || tabItem.ActualWidth <= 0)
                {
                    return;
                }

                Point relativePoint = tabItem.TransformToAncestor(AdornedElement).Transform(new Point(0, 0));
                double targetX = relativePoint.X + 8.0;
                double targetWidth = Math.Max(tabItem.ActualWidth - 16.0, 24.0);
                _currentY = relativePoint.Y + tabItem.ActualHeight - 3.0;

                InvalidateArrange();

                if (immediate || _indicator.Opacity < 0.1)
                {
                    _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    _indicator.BeginAnimation(FrameworkElement.WidthProperty, null);
                    _indicator.BeginAnimation(UIElement.OpacityProperty, null);

                    _translateTransform.X = targetX;
                    _indicator.Width = targetWidth;
                    _indicator.Opacity = 1.0;
                    return;
                }

                // Trailing inertia / lag animation (滞后感)
                var lagEase = new BackEase { Amplitude = 0.18, EasingMode = EasingMode.EaseOut };
                var duration = new Duration(TimeSpan.FromMilliseconds(320));
                var beginTime = TimeSpan.FromMilliseconds(30);

                var slideAnim = new DoubleAnimation
                {
                    To = targetX,
                    BeginTime = beginTime,
                    Duration = duration,
                    EasingFunction = lagEase,
                    FillBehavior = FillBehavior.HoldEnd,
                };

                var widthAnim = new DoubleAnimation
                {
                    To = targetWidth,
                    BeginTime = beginTime,
                    Duration = duration,
                    EasingFunction = lagEase,
                    FillBehavior = FillBehavior.HoldEnd,
                };

                var fadeAnim = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = duration,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd,
                };

                slideAnim.Completed += (_, _) =>
                {
                    _translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    _translateTransform.X = targetX;
                    _indicator.BeginAnimation(FrameworkElement.WidthProperty, null);
                    _indicator.Width = targetWidth;
                };

                _translateTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim, HandoffBehavior.SnapshotAndReplace);
                _indicator.BeginAnimation(FrameworkElement.WidthProperty, widthAnim, HandoffBehavior.SnapshotAndReplace);
                _indicator.BeginAnimation(UIElement.OpacityProperty, fadeAnim, HandoffBehavior.SnapshotAndReplace);
            }
            catch
            {
            }
        }
    }
}

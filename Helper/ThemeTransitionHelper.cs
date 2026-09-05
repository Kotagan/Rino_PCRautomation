using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MFAWPF.Helper.Animations;

namespace MFAWPF.Helper;

/// <summary>
/// Provides Android-style circular reveal transition animation when switching themes.
/// </summary>
public static class ThemeTransitionHelper
{
    private static bool _isTransitioning;

    /// <summary>
    /// Animates theme transition with a circular reveal expanding outward from the trigger element.
    /// </summary>
    public static void AnimateThemeSwitch(FrameworkElement? triggerElement, Action applyTheme)
    {
        if (applyTheme == null) return;

        if (triggerElement == null ||
            AnimationPreferences.CurrentLevel == AnimationLevel.Performance ||
            _isTransitioning)
        {
            applyTheme();
            return;
        }

        var window = Window.GetWindow(triggerElement) ?? Application.Current.MainWindow;
        if (window == null || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            applyTheme();
            return;
        }

        // Locate or resolve the transition canvas
        Canvas? canvas = null;
        if (window is Views.UI.RootView rootView)
        {
            canvas = rootView.ThemeTransitionCanvas;
        }
        else
        {
            canvas = window.FindName("ThemeTransitionCanvas") as Canvas;
        }

        if (canvas == null)
        {
            applyTheme();
            return;
        }

        try
        {
            _isTransitioning = true;
            canvas.Children.Clear();

            double width = window.ActualWidth;
            double height = window.ActualHeight;

            // Calculate center of the trigger element in window coordinates
            Point centerPoint;
            try
            {
                var transform = triggerElement.TransformToAncestor(window);
                centerPoint = transform.Transform(new Point(triggerElement.ActualWidth / 2.0, triggerElement.ActualHeight / 2.0));
            }
            catch
            {
                centerPoint = new Point(width / 2.0, height / 2.0);
            }

            // Calculate max radius to cover all 4 window corners
            double d1 = Math.Sqrt(Math.Pow(centerPoint.X, 2) + Math.Pow(centerPoint.Y, 2));
            double d2 = Math.Sqrt(Math.Pow(width - centerPoint.X, 2) + Math.Pow(centerPoint.Y, 2));
            double d3 = Math.Sqrt(Math.Pow(centerPoint.X, 2) + Math.Pow(height - centerPoint.Y, 2));
            double d4 = Math.Sqrt(Math.Pow(width - centerPoint.X, 2) + Math.Pow(height - centerPoint.Y, 2));
            double maxRadius = Math.Max(Math.Max(d1, d2), Math.Max(d3, d4)) + 20.0;

            // Capture visual snapshot of the window in current theme
            var dpi = VisualTreeHelper.GetDpi(window);
            int pixelWidth = Math.Max(1, (int)Math.Round(width * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Round(height * dpi.DpiScaleY));

            var rtb = new RenderTargetBitmap(
                pixelWidth,
                pixelHeight,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            rtb.Render(window);

            // Overlay image showing old theme snapshot
            var snapshotImage = new Image
            {
                Source = rtb,
                Width = width,
                Height = height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };

            // Setup expanding hole clip: rectangle excluding the expanding circle
            var rectGeometry = new RectangleGeometry(new Rect(0, 0, width, height));
            var circleGeometry = new EllipseGeometry(centerPoint, 0, 0);
            var combinedGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, rectGeometry, circleGeometry);
            snapshotImage.Clip = combinedGeometry;

            // Optional glowing wave ring at the expanding edge
            var waveRing = new Path
            {
                StrokeThickness = 2.5,
                Stroke = Application.Current.TryFindResource("PrimaryBrush") as Brush ?? Brushes.DarkOrange,
                Opacity = 0.85,
                IsHitTestVisible = false,
                Data = new EllipseGeometry(centerPoint, 0, 0)
            };

            canvas.Children.Add(snapshotImage);
            canvas.Children.Add(waveRing);

            // Switch to new theme underneath the overlay
            applyTheme();

            // Prepare animation
            var duration = AnimationPreferences.CurrentLevel switch
            {
                AnimationLevel.Comfort => TimeSpan.FromMilliseconds(420),
                _ => TimeSpan.FromMilliseconds(320)
            };
            var ease = AnimationPreferences.CreateEaseOut();

            var radiusXAnim = new DoubleAnimation(0.0, maxRadius, duration) { EasingFunction = ease };
            var radiusYAnim = new DoubleAnimation(0.0, maxRadius, duration) { EasingFunction = ease };

            var opacityAnim = new DoubleAnimation(0.85, 0.0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            radiusXAnim.Completed += (_, _) =>
            {
                canvas.Children.Clear();
                _isTransitioning = false;
            };

            // Run animations
            circleGeometry.BeginAnimation(EllipseGeometry.RadiusXProperty, radiusXAnim);
            circleGeometry.BeginAnimation(EllipseGeometry.RadiusYProperty, radiusYAnim);

            if (waveRing.Data is EllipseGeometry waveGeom)
            {
                var waveXAnim = new DoubleAnimation(0.0, maxRadius, duration) { EasingFunction = ease };
                var waveYAnim = new DoubleAnimation(0.0, maxRadius, duration) { EasingFunction = ease };
                waveGeom.BeginAnimation(EllipseGeometry.RadiusXProperty, waveXAnim);
                waveGeom.BeginAnimation(EllipseGeometry.RadiusYProperty, waveYAnim);
                waveRing.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogError(ex);
            canvas.Children.Clear();
            _isTransitioning = false;
            applyTheme();
        }
    }
}

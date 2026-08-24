using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MFAWPF.Views.UserControl;

public class CustomWindow : Window
{
    Point _pressedPosition;
    bool _isDragMoved;
    private bool _isClosing = false;


    public static readonly DependencyProperty IsResizableProperty =
        DependencyProperty.Register(nameof(IsResizable), typeof(bool), typeof(CustomWindow),
            new PropertyMetadata(false));

    public static readonly DependencyProperty DragHandleNameProperty =
        DependencyProperty.Register(nameof(DragHandleName), typeof(string), typeof(CustomWindow),
            new PropertyMetadata("TitleBar"));

    public string DragHandleName
    {
        get => (string)GetValue(DragHandleNameProperty);
        set => SetValue(DragHandleNameProperty, value);
    }

    public bool IsResizable
    {
        get => (bool)GetValue(IsResizableProperty);
        set => SetValue(IsResizableProperty, value);
    }

    public CustomWindow()
    {
        ResizeMode = ResizeMode.CanResize;
        PreviewMouseLeftButtonDown += Window_PreviewMouseLeftButtonDown;
        PreviewMouseMove += Window_PreviewMouseMove;
        PreviewMouseLeftButtonUp += Window_PreviewMouseLeftButtonUp;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        MinWidth = 200;

        InitializeAppleWindowAnimation();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = (new WindowInteropHelper(this)).Handle;
        var hwndSource = HwndSource.FromHwnd(handle);
        hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1;
        const int HTLEFT = 10;
        const int HTRIGHT = 11;
        const int HTTOP = 12;
        const int HTTOPLEFT = 13;
        const int HTTOPRIGHT = 14;
        const int HTBOTTOM = 15;
        const int HTBOTTOMLEFT = 16;
        const int HTBOTTOMRIGHT = 17;

        if (msg == WM_NCHITTEST && IsResizable)
        {
            handled = true;
            var mousePos = PointFromScreen(new Point((int)lParam & 0xFFFF, (int)lParam >> 16));
            var result = HTCLIENT;

            double resizeBorderThickness = 8;

            if (mousePos.Y < resizeBorderThickness)
            {
                if (mousePos.X < resizeBorderThickness) result = HTTOPLEFT;
                else if (mousePos.X > ActualWidth - resizeBorderThickness) result = HTTOPRIGHT;
                else result = HTTOP;
            }
            else if (mousePos.Y > ActualHeight - resizeBorderThickness)
            {
                if (mousePos.X < resizeBorderThickness) result = HTBOTTOMLEFT;
                else if (mousePos.X > ActualWidth - resizeBorderThickness) result = HTBOTTOMRIGHT;
                else result = HTBOTTOM;
            }
            else if (mousePos.X < resizeBorderThickness)
            {
                result = HTLEFT;
            }
            else if (mousePos.X > ActualWidth - resizeBorderThickness)
            {
                result = HTRIGHT;
            }

            return new IntPtr(result);
        }

        return IntPtr.Zero;
    }

    protected virtual void Close(object sender = null, RoutedEventArgs e = null)
    {
        base.Close();
    }

    protected void btnClose_Click(object sender, RoutedEventArgs e)
    {
        base.Close();
    }

    protected void btnRestore_Click(object sender, RoutedEventArgs e)
    {
        var titleBar = FindName("TitleBar") as FrameworkElement;
        if (WindowState == WindowState.Normal)
        {
            var workingArea = SystemParameters.WorkArea;

            MaxHeight = workingArea.Height + 8;

            WindowState = WindowState.Maximized;

            if (titleBar != null) titleBar.Margin = new Thickness(6, 5, 6, 0);
        }
        else
        {
            if (titleBar != null)
                titleBar.Margin = new Thickness(0);
            WindowState = WindowState.Normal;
        }
    }

    protected void btnMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    protected void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedPosition = e.GetPosition(this);
    }

    protected void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed && _pressedPosition != e.GetPosition(this))
        {
            if (FindName(DragHandleName) is UIElement element && IsPointInElement(e.GetPosition(this), element))
            {
                _isDragMoved = true;
                DragMove();
            }
        }
    }

    protected void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragMoved)
        {
            _isDragMoved = false;
            e.Handled = true;
        }
    }

    private bool IsPointInElement(Point point, UIElement element)
    {
        if (element == null) return false;

        var transform = element.TransformToVisual(this);
        var elementPoint = transform.Transform(new Point(0, 0));
        var elementRect = new Rect(elementPoint, new Size(element.RenderSize.Width, element.RenderSize.Height));

        return elementRect.Contains(point);
    }
    
    public void CenterWindow()
    {
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        
        var left = (screenWidth - Width) / 2;
        var top = (screenHeight - Height) / 2;
        
        this.Left = left;
        this.Top = top;
    }

    private void InitializeAppleWindowAnimation()
    {
        Loaded += (_, _) =>
        {
            if (Content is FrameworkElement rootElement)
            {
                rootElement.RenderTransformOrigin = new Point(0.5, 0.5);

                var scaleTransform = new ScaleTransform(0.90, 0.90);
                var translateTransform = new TranslateTransform(0, 18);
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(scaleTransform);
                transformGroup.Children.Add(translateTransform);
                rootElement.RenderTransform = transformGroup;
                rootElement.Opacity = 0.0;

                var sb = new Storyboard();

                // Opacity animation (0.0 -> 1.0)
                var opacityAnim = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(280),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(opacityAnim, rootElement);
                Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(opacityAnim);

                // Spring Scale X (0.90 -> 1.00)
                var scaleXAnim = new DoubleAnimation
                {
                    From = 0.90,
                    To = 1.00,
                    Duration = TimeSpan.FromMilliseconds(340),
                    EasingFunction = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(scaleXAnim, scaleTransform);
                Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath(ScaleTransform.ScaleXProperty));
                sb.Children.Add(scaleXAnim);

                // Spring Scale Y (0.90 -> 1.00)
                var scaleYAnim = new DoubleAnimation
                {
                    From = 0.90,
                    To = 1.00,
                    Duration = TimeSpan.FromMilliseconds(340),
                    EasingFunction = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(scaleYAnim, scaleTransform);
                Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath(ScaleTransform.ScaleYProperty));
                sb.Children.Add(scaleYAnim);

                // Rise Translation Y (18 -> 0)
                var translateYAnim = new DoubleAnimation
                {
                    From = 18.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(translateYAnim, translateTransform);
                Storyboard.SetTargetProperty(translateYAnim, new PropertyPath(TranslateTransform.YProperty));
                sb.Children.Add(translateYAnim);

                sb.Completed += (_, _) =>
                {
                    // Clear RenderTransform so WPF restores direct hardware pixel grid alignment and razor-sharp ClearType text rendering
                    rootElement.RenderTransform = null;
                    rootElement.Opacity = 1.0;
                };

                sb.Begin(rootElement);
            }
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosing && Content is FrameworkElement rootElement && IsLoaded)
        {
            e.Cancel = true;
            _isClosing = true;

            var scaleTransform = new ScaleTransform(1.0, 1.0);
            var translateTransform = new TranslateTransform(0, 0);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);
            rootElement.RenderTransform = transformGroup;

            var sb = new Storyboard();

            var opacityAnim = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(opacityAnim, rootElement);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(opacityAnim);

            var scaleXAnim = new DoubleAnimation
            {
                To = 0.94,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleXAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath(ScaleTransform.ScaleXProperty));
            sb.Children.Add(scaleXAnim);

            var scaleYAnim = new DoubleAnimation
            {
                To = 0.94,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleYAnim, scaleTransform);
            Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath(ScaleTransform.ScaleYProperty));
            sb.Children.Add(scaleYAnim);

            var translateYAnim = new DoubleAnimation
            {
                To = 12.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(translateYAnim, translateTransform);
            Storyboard.SetTargetProperty(translateYAnim, new PropertyPath(TranslateTransform.YProperty));
            sb.Children.Add(translateYAnim);

            sb.Completed += (_, _) =>
            {
                Close();
            };
            sb.Begin(rootElement);
            return;
        }

        base.OnClosing(e);
    }
}
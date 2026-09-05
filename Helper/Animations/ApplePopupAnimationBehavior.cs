using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using HandyControl.Controls;
using ComboBox = System.Windows.Controls.ComboBox;
using DatePicker = System.Windows.Controls.DatePicker;
using ScrollViewer = System.Windows.Controls.ScrollViewer;
using Window = System.Windows.Window;

namespace MFAWPF.Helper.Animations;

/// <summary>
/// Provides Apple-style fluid popover, dropdown, dialog, and option window animations with linear/curved motion.
/// </summary>
public static class ApplePopupAnimationBehavior
{
    private static bool _isInitialized;
    private static readonly List<WeakReference<ComboBox>> ActiveComboBoxes = [];
    private static readonly List<WeakReference<Popup>> ActivePopups = [];
    private static long _lastPopupOpenTime;

    /// <summary>
    /// Gets the timestamp when a popup was last opened.
    /// </summary>
    public static long LastPopupOpenTime => _lastPopupOpenTime;

    private const int PopupGracePeriodMs = 600;

    private static readonly Duration RetractDuration = new(TimeSpan.FromMilliseconds(120));

    private static readonly IEasingFunction AppleOpenEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction AppleCloseEase = new CubicEase { EasingMode = EasingMode.EaseIn };

    /// <summary>
    /// Initializes global popup, dropdown, menu, and dialog animation handlers.
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        FixMenuDropAlignment();

        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnComboBoxLoaded));

        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnContextMenuOpened));

        EventManager.RegisterClassHandler(
            typeof(HandyControl.Controls.Dialog),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDialogLoaded));

        EventManager.RegisterClassHandler(
            typeof(DatePicker),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnDatePickerLoaded));

        EventManager.RegisterClassHandler(
            typeof(TimePicker),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnTimePickerLoaded));

        EventManager.RegisterClassHandler(
            typeof(AutoCompleteTextBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAutoCompleteTextBoxLoaded));

        EventManager.RegisterClassHandler(
            typeof(SplitButton),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnSplitButtonLoaded));

        EventManager.RegisterClassHandler(
            typeof(Window),
            Window.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(OnScrollViewerScrollChanged));

        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollViewerPreviewMouseWheel),
            true);

        EventManager.RegisterClassHandler(
            typeof(HandyControl.Controls.Growl),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnGrowlPreviewMouseLeftButtonDown));
    }

    private static void FixMenuDropAlignment()
    {
        try
        {
            var field = typeof(SystemParameters).GetField("_menuDropAlignment", BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
            {
                field.SetValue(null, false);
            }
        }
        catch
        {
            // Ignore if reflection restricted
        }
    }

    private static void OnComboBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox cb) return;

        cb.DropDownOpened -= OnComboBoxDropDownOpened;
        cb.DropDownOpened += OnComboBoxDropDownOpened;

        cb.DropDownClosed -= OnComboBoxDropDownClosed;
        cb.DropDownClosed += OnComboBoxDropDownClosed;

        cb.SizeChanged -= OnComboBoxSizeChanged;
        cb.SizeChanged += OnComboBoxSizeChanged;

        var descriptor = DependencyPropertyDescriptor.FromProperty(ComboBox.IsDropDownOpenProperty, typeof(ComboBox));
        descriptor?.RemoveValueChanged(cb, OnComboBoxIsDropDownOpenChanged);
        descriptor?.AddValueChanged(cb, OnComboBoxIsDropDownOpenChanged);
    }

    private static void OnComboBoxSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.IsDropDownOpen)
        {
            EnforceComboBoxPopupWidth(cb);
        }
    }

    private static CustomPopupPlacement[] ComboBoxPlacementCallback(Size popupSize, Size targetSize, Point offset)
    {
        return
        [
            new CustomPopupPlacement(new Point(0, targetSize.Height + 6), PopupPrimaryAxis.None),
            new CustomPopupPlacement(new Point(0, -popupSize.Height - 6), PopupPrimaryAxis.None)
        ];
    }

    private static void EnforceComboBoxPopupWidth(ComboBox cb)
    {
        if (cb == null) return;
        double w = cb.ActualWidth;
        if (w <= 0) w = cb.Width;
        if (w <= 0 || double.IsNaN(w)) return;

        Popup? popup = cb.Template?.FindName("PART_Popup", cb) as Popup ?? FindVisualChildren<Popup>(cb).FirstOrDefault();
        if (popup != null)
        {
            popup.AllowsTransparency = true;
            var target = cb.Template?.FindName("templateRoot", cb) as UIElement ?? cb;
            popup.PlacementTarget = target;
            popup.CustomPopupPlacementCallback = ComboBoxPlacementCallback;
            popup.Placement = PlacementMode.Custom;
            popup.HorizontalOffset = 0;
            popup.VerticalOffset = 0;

            popup.ClearValue(FrameworkElement.WidthProperty);
            popup.ClearValue(FrameworkElement.MinWidthProperty);
            popup.ClearValue(FrameworkElement.MaxWidthProperty);

            if (popup.Child is FrameworkElement child)
            {
                child.Width = w;
                child.MinWidth = w;
                child.MaxWidth = w;
                child.Margin = new Thickness(0);
                child.HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
    }

    private static void OnComboBoxIsDropDownOpenChanged(object? sender, EventArgs e)
    {
        if (sender is not ComboBox cb) return;

        if (cb.IsDropDownOpen)
        {
            OnComboBoxDropDownOpened(cb, EventArgs.Empty);
        }
    }

    private static void OnComboBoxDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox cb) return;

        _lastPopupOpenTime = Environment.TickCount64;

        if (FindVisualParent<ScrollViewer>(cb) is ScrollViewer parentScrollViewer)
        {
            SmoothScrollViewerBehavior.StopAllAnimations(parentScrollViewer);
        }

        EnforceComboBoxPopupWidth(cb);

        lock (ActiveComboBoxes)
        {
            CleanDeadReferences();
            if (!ActiveComboBoxes.Any(wr => wr.TryGetTarget(out var target) && ReferenceEquals(target, cb)))
            {
                ActiveComboBoxes.Add(new WeakReference<ComboBox>(cb));
            }
        }

        cb.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!cb.IsDropDownOpen) return;
            EnforceComboBoxPopupWidth(cb);

            Popup? popup = cb.Template?.FindName("PART_Popup", cb) as Popup ?? FindVisualChildren<Popup>(cb).FirstOrDefault();
            if (popup != null)
            {
                popup.AllowsTransparency = true;
                EnforceComboBoxPopupWidth(cb);

                if (popup.Child is FrameworkElement child)
                {
                    if (child.Name != "DropDownBorder")
                    {
                        AnimateOpen(popup, child);
                    }
                }
                else if (popup.Child is FrameworkElement pendingChild)
                {
                    void OnChildLoaded(object? s, RoutedEventArgs re)
                    {
                        if (popup.Child is FrameworkElement loadedChild)
                        {
                            loadedChild.Loaded -= OnChildLoaded;
                            EnforceComboBoxPopupWidth(cb);
                            if (loadedChild.Name != "DropDownBorder")
                            {
                                AnimateOpen(popup, loadedChild);
                            }
                        }
                    }
                    pendingChild.Loaded += OnChildLoaded;
                }
            }
        });
    }

    private static void OnComboBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox cb) return;

        lock (ActiveComboBoxes)
        {
            ActiveComboBoxes.RemoveAll(wr => wr.TryGetTarget(out var target) && ReferenceEquals(target, cb));
        }
    }

    private static void OnDatePickerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker dp) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(DatePicker.IsDropDownOpenProperty, typeof(DatePicker));
        descriptor?.AddValueChanged(dp, (_, _) =>
        {
            if (dp.IsDropDownOpen)
            {
                _lastPopupOpenTime = Environment.TickCount64;
                dp.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    Popup? popup = dp.Template?.FindName("PART_Popup", dp) as Popup ?? FindVisualChildren<Popup>(dp).FirstOrDefault();
                    if (popup?.Child is FrameworkElement child)
                    {
                        AnimateOpen(popup, child);
                    }
                });
            }
        });
    }

    private static void OnTimePickerLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not TimePicker tp) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(TimePicker.IsDropDownOpenProperty, typeof(TimePicker));
        descriptor?.AddValueChanged(tp, (_, _) =>
        {
            if (tp.IsDropDownOpen)
            {
                _lastPopupOpenTime = Environment.TickCount64;
                tp.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    Popup? popup = tp.Template?.FindName("PART_Popup", tp) as Popup ?? FindVisualChildren<Popup>(tp).FirstOrDefault();
                    if (popup?.Child is FrameworkElement child)
                    {
                        AnimateOpen(popup, child);
                    }
                });
            }
        });
    }

    private static void OnAutoCompleteTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not AutoCompleteTextBox ac) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(AutoCompleteTextBox.IsDropDownOpenProperty, typeof(AutoCompleteTextBox));
        descriptor?.AddValueChanged(ac, (_, _) =>
        {
            if (ac.IsDropDownOpen)
            {
                _lastPopupOpenTime = Environment.TickCount64;
                ac.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    Popup? popup = ac.Template?.FindName("PART_Popup", ac) as Popup ?? FindVisualChildren<Popup>(ac).FirstOrDefault();
                    if (popup?.Child is FrameworkElement child)
                    {
                        AnimateOpen(popup, child);
                    }
                });
            }
        });
    }

    private static void OnSplitButtonLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not SplitButton sb) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(SplitButton.IsDropDownOpenProperty, typeof(SplitButton));
        descriptor?.AddValueChanged(sb, (_, _) =>
        {
            if (sb.IsDropDownOpen)
            {
                _lastPopupOpenTime = Environment.TickCount64;
                sb.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    Popup? popup = sb.Template?.FindName("PART_Popup", sb) as Popup ?? FindVisualChildren<Popup>(sb).FirstOrDefault();
                    if (popup?.Child is FrameworkElement child)
                    {
                        AnimateOpen(popup, child);
                    }
                });
            }
        });
    }

    private static void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm) return;

        _lastPopupOpenTime = Environment.TickCount64;

        cm.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (!cm.IsOpen) return;
            ApplyAppleCardStyling(cm);
            AnimateElementOpen(cm, isTopPlacement: false, durationMs: 180, fromScale: 0.92, fromY: -6.0);
        });
    }

    private static void OnDialogLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not HandyControl.Controls.Dialog dialog) return;

        dialog.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            ApplyAppleCardStyling(dialog);
            AnimateElementOpen(dialog, isTopPlacement: false, durationMs: 220, fromScale: 0.90, fromY: -12.0);
        });
    }

    private static void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Window window || window == Application.Current?.MainWindow) return;

        if (window.Content is FrameworkElement rootContent)
        {
            AnimateElementOpen(rootContent, isTopPlacement: false, durationMs: 220, fromScale: 0.92, fromY: -10.0);
        }
    }

    private static void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.HorizontalChange == 0) return;
        if (sender is not DependencyObject scrollViewer) return;

        if (Environment.TickCount64 - _lastPopupOpenTime < PopupGracePeriodMs) return;
        if (IsInsidePopup(scrollViewer)) return;

        CloseAllOpenPopups(scrollViewer);
    }

    private static void OnScrollViewerPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject scrollViewer) return;
        if (IsInsidePopup(scrollViewer)) return;

        if (e.OriginalSource is DependencyObject source)
        {
            if (IsInsidePopup(source)) return;
            if (FindVisualParent<ComboBox>(source) is ComboBox { IsDropDownOpen: true }) return;
        }

        CloseAllOpenPopups(scrollViewer);
    }

    /// <summary>
    /// Closes all active popups and dropdowns with an Apple-style retract animation.
    /// </summary>
    public static void CloseAllOpenPopups(DependencyObject? scope = null)
    {
        if (Environment.TickCount64 - _lastPopupOpenTime < PopupGracePeriodMs) return;

        List<ComboBox> comboBoxesToRetract;
        lock (ActiveComboBoxes)
        {
            CleanDeadReferences();
            comboBoxesToRetract = ActiveComboBoxes
                .Select(wr => wr.TryGetTarget(out var target) ? target : null)
                .Where(cb => cb != null && cb.IsDropDownOpen)
                .Cast<ComboBox>()
                .ToList();
        }

        foreach (var cb in comboBoxesToRetract)
        {
            if (IsComboBoxInteracting(cb)) continue;
            RetractAndClose(cb);
        }

        List<Popup> popupsToRetract;
        lock (ActivePopups)
        {
            CleanDeadReferences();
            popupsToRetract = ActivePopups
                .Select(wr => wr.TryGetTarget(out var target) ? target : null)
                .Where(p => p != null && p.IsOpen)
                .Cast<Popup>()
                .ToList();
        }

        foreach (var popup in popupsToRetract)
        {
            RetractAndClose(popup);
        }

        if (scope != null)
        {
            CloseScopeDropDowns(scope);
        }
    }

    private static void CloseScopeDropDowns(DependencyObject scope)
    {
        foreach (var element in FindVisualChildren<DependencyObject>(scope))
        {
            switch (element)
            {
                case ComboBox cb when cb.IsDropDownOpen:
                    if (!IsComboBoxInteracting(cb))
                    {
                        RetractAndClose(cb);
                    }
                    break;
                case SplitButton sb when sb.IsDropDownOpen:
                    sb.IsDropDownOpen = false;
                    break;
                case DatePicker dp when dp.IsDropDownOpen:
                    dp.IsDropDownOpen = false;
                    break;
                case TimePicker tp when tp.IsDropDownOpen:
                    tp.IsDropDownOpen = false;
                    break;
                case DateTimePicker dtp when dtp.IsDropDownOpen:
                    dtp.IsDropDownOpen = false;
                    break;
                case Popup popup when popup.IsOpen:
                    RetractAndClose(popup);
                    break;
                case ContextMenu cm when cm.IsOpen:
                    cm.IsOpen = false;
                    break;
            }
        }
    }

    private static bool IsComboBoxInteracting(ComboBox cb)
    {
        if (cb.IsMouseOver || cb.IsKeyboardFocusWithin)
        {
            return true;
        }

        Popup? popup = cb.Template?.FindName("PART_Popup", cb) as Popup ?? FindVisualChildren<Popup>(cb).FirstOrDefault();
        if (popup != null && (popup.IsMouseOver || (popup.Child as FrameworkElement)?.IsMouseOver == true))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Runs Apple-style entrance animation (scale + fade + subtle slide).
    /// </summary>
    public static void AnimateOpen(Popup popup, FrameworkElement child)
    {
        bool isTopPlacement = popup.Placement == PlacementMode.Top;
        AnimateElementOpen(child, isTopPlacement, durationMs: 180, fromScale: 0.92, fromY: 0.0);
    }

    /// <summary>
    /// Applies fluid Apple scale + translate + opacity entrance animation to any element.
    /// </summary>
    public static void AnimateElementOpen(
        FrameworkElement element,
        bool isTopPlacement = false,
        double durationMs = 180,
        double fromScale = 0.92,
        double fromY = -8.0)
    {
        element.RenderTransformOrigin = isTopPlacement ? new Point(0.5, 1.0) : new Point(0.5, 0.0);

        var (scaleTransform, translateTransform) = EnsureTransforms(element);

        ApplyAppleCardStyling(element);

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));

        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 0.0;

        var opacityAnim = new DoubleAnimation(0.0, 1.0, duration)
        {
            EasingFunction = AppleOpenEase
        };
        element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

        var scaleXAnim = new DoubleAnimation(fromScale + 0.03, 1.0, duration)
        {
            EasingFunction = AppleOpenEase
        };
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);

        var scaleYAnim = new DoubleAnimation(fromScale, 1.0, duration)
        {
            EasingFunction = AppleOpenEase
        };
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

        var translateYAnim = new DoubleAnimation(fromY, 0.0, duration)
        {
            EasingFunction = AppleOpenEase
        };
        translateTransform.BeginAnimation(TranslateTransform.YProperty, translateYAnim);
    }

    /// <summary>
    /// Retracts and closes a ComboBox with Apple-style retract animation.
    /// </summary>
    public static void RetractAndClose(ComboBox cb)
    {
        if (!cb.IsDropDownOpen) return;

        Popup? popup = cb.Template?.FindName("PART_Popup", cb) as Popup ?? FindVisualChildren<Popup>(cb).FirstOrDefault();
        if (popup != null && popup.IsOpen && popup.Child is FrameworkElement child)
        {
            RetractAndClosePopup(popup, child, () =>
            {
                cb.IsDropDownOpen = false;
            });
        }
        else
        {
            cb.IsDropDownOpen = false;
        }
    }

    /// <summary>
    /// Retracts and closes a Popup with Apple-style retract animation.
    /// </summary>
    public static void RetractAndClose(Popup popup)
    {
        if (!popup.IsOpen) return;

        if (popup.Child is FrameworkElement child)
        {
            RetractAndClosePopup(popup, child, () =>
            {
                ClosePopup(popup);
            });
        }
        else
        {
            ClosePopup(popup);
        }
    }

    private static void RetractAndClosePopup(Popup popup, FrameworkElement child, Action onCompleted)
    {
        bool isTopPlacement = popup.Placement == PlacementMode.Top;
        child.RenderTransformOrigin = isTopPlacement ? new Point(0.5, 1.0) : new Point(0.5, 0.0);

        var (scaleTransform, translateTransform) = EnsureTransforms(child);

        var opacityAnim = new DoubleAnimation(child.Opacity, 0.0, RetractDuration)
        {
            EasingFunction = AppleCloseEase
        };

        var scaleXAnim = new DoubleAnimation(scaleTransform.ScaleX, 0.94, RetractDuration)
        {
            EasingFunction = AppleCloseEase
        };

        var scaleYAnim = new DoubleAnimation(scaleTransform.ScaleY, 0.88, RetractDuration)
        {
            EasingFunction = AppleCloseEase
        };

        double toY = isTopPlacement ? 5.0 : -5.0;
        var translateYAnim = new DoubleAnimation(translateTransform.Y, toY, RetractDuration)
        {
            EasingFunction = AppleCloseEase
        };

        opacityAnim.Completed += (_, _) =>
        {
            onCompleted();
            child.BeginAnimation(UIElement.OpacityProperty, null);
            child.Opacity = 1.0;
        };

        child.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
        translateTransform.BeginAnimation(TranslateTransform.YProperty, translateYAnim);
    }

    private static void ClosePopup(Popup popup)
    {
        try
        {
            if (popup.TemplatedParent is ComboBox cb && cb.IsDropDownOpen)
            {
                cb.IsDropDownOpen = false;
            }
            else
            {
                popup.IsOpen = false;
            }
        }
        catch
        {
            popup.IsOpen = false;
        }
    }

    private static (ScaleTransform scale, TranslateTransform translate) EnsureTransforms(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group &&
            group.Children.Count >= 2 &&
            group.Children[0] is ScaleTransform st &&
            group.Children[1] is TranslateTransform tt)
        {
            return (st, tt);
        }

        var newScale = new ScaleTransform(1.0, 1.0);
        var newTranslate = new TranslateTransform(0.0, 0.0);
        var newGroup = new TransformGroup();
        newGroup.Children.Add(newScale);
        newGroup.Children.Add(newTranslate);

        element.RenderTransform = newGroup;
        return (newScale, newTranslate);
    }

    /// <summary>
    /// Applies Apple-style smooth rounded corners without harsh drop shadows.
    /// </summary>
    public static void ApplyAppleCardStyling(FrameworkElement child)
    {
        if (child is Border border)
        {
            if (border.CornerRadius == default)
            {
                border.CornerRadius = new CornerRadius(8);
            }
        }
    }

    /// <summary>
    /// Checks whether the specified element is located inside a popup or dropdown.
    /// </summary>
    public static bool IsInsidePopup(DependencyObject? element)
    {
        if (element == null) return false;

        if (element is Visual visual)
        {
            var source = PresentationSource.FromVisual(visual);
            if (source?.RootVisual != null && source.RootVisual.GetType().Name == "PopupRoot")
            {
                return true;
            }
        }

        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Popup || current.GetType().Name == "PopupRoot") return true;

            DependencyObject? parent = null;
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            if (parent == null && current is FrameworkElement fe)
            {
                parent = fe.Parent ?? fe.TemplatedParent;
            }
            current = parent;
        }
        return false;
    }

    private static void CleanDeadReferences()
    {
        ActiveComboBoxes.RemoveAll(wr => !wr.TryGetTarget(out _));
        ActivePopups.RemoveAll(wr => !wr.TryGetTarget(out _));
    }

    private static void OnGrowlPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not HandyControl.Controls.Growl growl) return;

        // If the user clicks interactive buttons inside PART_PanelMore (such as Cancel / Ok in Growl.Ask), do not dismiss
        if (e.OriginalSource is DependencyObject source)
        {
            var parentButton = FindVisualParent<Button>(source);
            if (parentButton != null && (parentButton.Name == "PART_ButtonCancel" || parentButton.Name == "PART_ButtonOk"))
            {
                return;
            }
        }

        e.Handled = true;

        try
        {
            var closeMethod = typeof(HandyControl.Controls.Growl).GetMethod(
                "Close",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null,
                new[] { typeof(bool) },
                null);

            if (closeMethod != null)
            {
                closeMethod.Invoke(growl, new object[] { true });
                return;
            }
        }
        catch
        {
        }

        try
        {
            if (growl.Template?.FindName("PART_ButtonClose", growl) is Button btnClose)
            {
                var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(btnClose) as System.Windows.Automation.Peers.ButtonAutomationPeer;
                if (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is System.Windows.Automation.Provider.IInvokeProvider invokeProv)
                {
                    invokeProv.Invoke();
                }
            }
        }
        catch
        {
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

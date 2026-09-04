using MFAWPF.Helper;
using MFAWPF.Services.Characters;
using MFAWPF.ViewModels.UI;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MFAWPF.Views.UI;

/// <summary>
/// Displays the configured desktop character.
/// </summary>
public partial class CharacterWindow
{
    private const uint MonitorDefaultToNearest = 2;
    private readonly CharacterRuntimeService _runtimeService;

    internal CharacterWindow(
        CharacterRuntimeService runtimeService,
        CharacterWindowViewModel viewModel)
    {
        _runtimeService = runtimeService;
        DataContext = viewModel;
        InitializeComponent();
    }

    internal Rect GetCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfoW(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            info.Work.Left / dpi.DpiScaleX,
            info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    internal void ClampHorizontallyToWorkArea()
    {
        var workArea = GetCurrentWorkArea();
        var width = Math.Max(ActualWidth, CharacterImage.Width);
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
    }

    internal void AlignFootToWorkArea(double footAnchorY)
    {
        if (!IsLoaded)
        {
            Dispatcher.InvokeAsync(() => AlignFootToWorkArea(footAnchorY));
            return;
        }

        var workArea = GetCurrentWorkArea();
        Top = workArea.Bottom - StatusBar.ActualHeight -
              Math.Clamp(footAnchorY, 0d, 1d) * CharacterImage.ActualHeight;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (!_runtimeService.BeginDrag())
        {
            return;
        }
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        finally
        {
            _runtimeService.EndDrag(Left, Top);
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        _runtimeService.HandleRightClick();

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu || contextMenu.Items.Count < 2)
        {
            return;
        }

        ((MenuItem)contextMenu.Items[0]).IsEnabled = !Instances.RootViewModel.IsRunning;
        ((MenuItem)contextMenu.Items[1]).IsEnabled = Instances.RootViewModel.IsRunning;
    }

    private void OnStartTaskClick(object sender, RoutedEventArgs e) =>
        Instances.TaskQueueView.Start();

    private void OnStopTaskClick(object sender, RoutedEventArgs e) =>
        Instances.TaskQueueView.Stop();

    private void OnOpenMainWindowClick(object sender, RoutedEventArgs e) =>
        Instances.RootView.ShowWindow();

    private void OnOpenCharacterPageClick(object sender, RoutedEventArgs e) =>
        Instances.RootView.ShowCharacterPage();

    private void OnOpenAssetFolderClick(object sender, RoutedEventArgs e) =>
        _runtimeService.OpenAssetRoot();

    private void OnTogglePositionLockClick(object sender, RoutedEventArgs e) =>
        _runtimeService.TogglePositionLock();

    private void OnHideCharacterClick(object sender, RoutedEventArgs e) =>
        _runtimeService.Disable();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", SetLastError = false)]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = false)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref NativeMonitorInfo monitorInfo);
}

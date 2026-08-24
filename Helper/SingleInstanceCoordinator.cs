using System.Windows;

namespace MFAWPF.Helper;

internal static class SingleInstanceCoordinator
{
    private const string MutexName = @"Local\Rino.Desktop.SingleInstance";
    private const string ActivationEventName = @"Local\Rino.Desktop.Activate";
    private static readonly TimeSpan RestartHandoffTimeout = TimeSpan.FromSeconds(3);

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activationEvent;
    private static CancellationTokenSource? _listenerCancellation;
    private static Task? _listenerTask;
    private static bool _ownsMutex;

    public static bool TryAcquire()
    {
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out var createdNew);
            _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);

            if (createdNew)
            {
                _ownsMutex = true;
                return true;
            }

            _activationEvent.Set();

            try
            {
                _ownsMutex = _instanceMutex.WaitOne(RestartHandoffTimeout);
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
            }

            if (!_ownsMutex)
            {
                DisposeHandles();
            }

            return _ownsMutex;
        }
        catch (Exception ex)
        {
            DisposeHandles();
            LoggerService.LogError($"Failed to initialize single-instance coordination: {ex}");
            return true;
        }
    }

    public static void StartActivationListener()
    {
        if (!_ownsMutex || _activationEvent is null || _listenerTask is not null)
        {
            return;
        }

        var activationEvent = _activationEvent;
        _listenerCancellation = new CancellationTokenSource();
        var cancellation = _listenerCancellation;
        _listenerTask = Task.Run(() => ListenForActivation(activationEvent, cancellation));
    }

    public static void Dispose()
    {
        _listenerCancellation?.Cancel();

        try
        {
            _listenerTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException ex)
        {
            LoggerService.LogError($"Failed to stop the single-instance activation listener: {ex}");
        }

        _listenerCancellation?.Dispose();
        _listenerCancellation = null;
        _listenerTask = null;

        if (_ownsMutex)
        {
            try
            {
                _instanceMutex?.ReleaseMutex();
            }
            catch (ApplicationException ex)
            {
                LoggerService.LogError($"Failed to release the single-instance mutex: {ex}");
            }
        }

        _ownsMutex = false;
        DisposeHandles();
    }

    private static void ListenForActivation(
        EventWaitHandle activationEvent,
        CancellationTokenSource cancellation)
    {
        var waitHandles = new WaitHandle[] { activationEvent, cancellation.Token.WaitHandle };

        while (WaitHandle.WaitAny(waitHandles) == 0)
        {
            ActivateMainWindow();
        }
    }

    private static void ActivateMainWindow()
    {
        var application = Application.Current;
        if (application?.Dispatcher.HasShutdownStarted != false)
        {
            return;
        }

        _ = application.Dispatcher.BeginInvoke(() =>
        {
            var window = application.MainWindow
                ?? application.Windows.OfType<Window>().FirstOrDefault(candidate => candidate.IsVisible);
            if (window is null)
            {
                return;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            var wasTopmost = window.Topmost;
            window.Activate();
            window.Topmost = true;
            window.Topmost = wasTopmost;
            window.Focus();
        });
    }

    private static void DisposeHandles()
    {
        _activationEvent?.Dispose();
        _activationEvent = null;
        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }
}

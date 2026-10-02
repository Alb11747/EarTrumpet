using EarTrumpet.Extensions;
using EarTrumpet.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Threading;
using Cursor = System.Windows.Forms.Cursor;
using Screen = System.Windows.Forms.Screen;

namespace EarTrumpet.UI.Notifications;

internal static class VolumeToastService
{
    private static readonly VolumeToastLifetime s_lifetime = new();
    private static VolumeToastWindow s_window;
    private static DispatcherTimer s_hideTimer;
    private static bool s_isShuttingDown;

    public static bool ContainsScreenPoint(int x, int y) => s_window?.ContainsScreenPoint(x, y) == true;

    public static void ShowDevice(DeviceViewModel device, Screen screen, bool? isMuted = null)
    {
        if (device != null && screen != null)
        {
            Show(VolumeToastViewModel.FromDevice(device, isMuted), screen);
        }
    }

    public static void ShowApp(IAppItemViewModel app, Screen screen, bool? isMuted = null)
    {
        if (app != null && screen != null)
        {
            Show(VolumeToastViewModel.FromApp(app, isMuted), screen);
        }
    }

    // Changes made inside an EarTrumpet window show the toast on that window's monitor.
    public static Screen GetScreen(DependencyObject element)
    {
        var window = element == null ? null : Window.GetWindow(element);
        return window == null ? Screen.FromPoint(Cursor.Position) : Screen.FromHandle(window.GetHandle());
    }

    public static void Shutdown()
    {
        s_isShuttingDown = true;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)ShutdownCore);
            }
            catch (InvalidOperationException)
            {
                // The dispatcher started shutting down after the checks above.
            }
            return;
        }

        ShutdownCore();
    }

    private static void Show(VolumeToastViewModel viewModel, Screen screen)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || !CanShow(dispatcher))
        {
            viewModel.Dispose();
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)(() => Show(viewModel, screen)));
            }
            catch (InvalidOperationException)
            {
                viewModel.Dispose();
            }
            return;
        }

        if (!CanShow(dispatcher))
        {
            viewModel.Dispose();
            return;
        }

        if (s_window == null)
        {
            s_window = new VolumeToastWindow();
            s_window.UserActivity += Window_UserActivity;
            s_window.HoverChanged += Window_HoverChanged;
            s_window.InputCaptureChanged += Window_InputCaptureChanged;
            s_window.CloseRequested += HideWindow;
            s_window.Closed += Window_Closed;
        }

        s_hideTimer ??= CreateHideTimer(dispatcher);

        s_lifetime.Reset(s_window.IsMouseOver, s_window.HasInputCapture);
        s_window.SetVolume(viewModel);
        if (!s_window.IsVisible)
        {
            s_window.Opacity = 0;
            s_window.Show();
        }

        s_window.UpdateLayout();
        s_window.Position(screen);
        s_window.Opacity = 1;
        RestartHideTimer();
    }

    private static bool CanShow(Dispatcher dispatcher) =>
        !s_isShuttingDown &&
        !App.IsShuttingDown &&
        !dispatcher.HasShutdownStarted &&
        !dispatcher.HasShutdownFinished;

    private static DispatcherTimer CreateHideTimer(Dispatcher dispatcher)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        timer.Tick += HideTimer_Tick;
        return timer;
    }

    private static void RestartHideTimer()
    {
        if (s_hideTimer == null)
        {
            return;
        }

        s_hideTimer.Stop();
        if (s_window?.IsVisible == true && s_lifetime.HideDelay is TimeSpan delay)
        {
            s_hideTimer.Interval = delay;
            s_hideTimer.Start();
        }
    }

    private static void Window_UserActivity(bool isInteraction)
    {
        s_lifetime.RecordActivity(isInteraction);
        RestartHideTimer();
    }

    private static void Window_HoverChanged(bool isPointerOver)
    {
        s_lifetime.SetPointerOver(isPointerOver);
        RestartHideTimer();
    }

    private static void Window_InputCaptureChanged(bool hasInputCapture)
    {
        s_lifetime.SetInputCapture(hasInputCapture);
        RestartHideTimer();
    }

    private static void HideTimer_Tick(object sender, EventArgs e)
    {
        s_hideTimer?.Stop();

        s_lifetime.SetPointerOver(s_window?.IsMouseOver == true);
        s_lifetime.SetInputCapture(s_window?.HasInputCapture == true);
        if (s_lifetime.HideDelay == null)
        {
            return;
        }

        HideWindow();
    }

    private static void HideWindow()
    {
        s_hideTimer?.Stop();
        s_window?.Hide();
        s_window?.ClearVolume();
        s_lifetime.Reset();
    }

    private static void Window_Closed(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, s_window))
        {
            DetachWindow();
        }

        DisposeHideTimer();
        s_lifetime.Reset();
    }

    private static void ShutdownCore()
    {
        DisposeHideTimer();

        var window = s_window;
        if (window != null)
        {
            DetachWindow();
            window.Close();
        }

        s_lifetime.Reset();
    }

    private static void DetachWindow()
    {
        s_window.UserActivity -= Window_UserActivity;
        s_window.HoverChanged -= Window_HoverChanged;
        s_window.InputCaptureChanged -= Window_InputCaptureChanged;
        s_window.CloseRequested -= HideWindow;
        s_window.Closed -= Window_Closed;
        s_window = null;
    }

    private static void DisposeHideTimer()
    {
        if (s_hideTimer != null)
        {
            s_hideTimer.Stop();
            s_hideTimer.Tick -= HideTimer_Tick;
            s_hideTimer = null;
        }
    }
}

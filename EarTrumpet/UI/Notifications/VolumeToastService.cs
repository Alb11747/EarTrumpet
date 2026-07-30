using EarTrumpet.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace EarTrumpet.UI.Notifications;

internal static class VolumeToastService
{
    private static readonly TimeSpan s_defaultDisplayDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_interactedDisplayDuration = TimeSpan.FromSeconds(5);
    private static VolumeToastWindow s_window;
    private static DispatcherTimer s_hideTimer;
    private static bool s_hasInteracted;
    private static bool s_isPointerOver;
    private static bool s_isShuttingDown;

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
            s_window.Closed += Window_Closed;
        }

        s_hideTimer ??= CreateHideTimer(dispatcher);

        s_hasInteracted = false;
        s_isPointerOver = s_window.IsMouseOver;
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
        s_hideTimer.Interval = s_hasInteracted ? s_interactedDisplayDuration : s_defaultDisplayDuration;

        if (!s_isPointerOver)
        {
            s_hideTimer.Start();
        }
    }

    private static void Window_UserActivity(bool isInteraction)
    {
        s_hasInteracted |= isInteraction;
        RestartHideTimer();
    }

    private static void Window_HoverChanged(bool isPointerOver)
    {
        s_isPointerOver = isPointerOver;
        RestartHideTimer();
    }

    private static void HideTimer_Tick(object sender, EventArgs e)
    {
        s_hideTimer?.Stop();

        if (s_window?.IsMouseOver == true)
        {
            s_isPointerOver = true;
            return;
        }

        HideWindow();
    }

    private static void HideWindow()
    {
        s_hideTimer?.Stop();
        s_window?.Hide();
        s_window?.ClearVolume();
        s_hasInteracted = false;
        s_isPointerOver = false;
    }

    private static void Window_Closed(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, s_window))
        {
            s_window.UserActivity -= Window_UserActivity;
            s_window.HoverChanged -= Window_HoverChanged;
            s_window.Closed -= Window_Closed;
            s_window = null;
        }

        if (s_hideTimer != null)
        {
            s_hideTimer.Stop();
            s_hideTimer.Tick -= HideTimer_Tick;
            s_hideTimer = null;
        }

        s_hasInteracted = false;
        s_isPointerOver = false;
    }

    private static void ShutdownCore()
    {
        if (s_hideTimer != null)
        {
            s_hideTimer.Stop();
            s_hideTimer.Tick -= HideTimer_Tick;
            s_hideTimer = null;
        }

        if (s_window != null)
        {
            s_window.UserActivity -= Window_UserActivity;
            s_window.HoverChanged -= Window_HoverChanged;
            s_window.Closed -= Window_Closed;
            s_window.Close();
            s_window = null;
        }

        s_hasInteracted = false;
        s_isPointerOver = false;
    }
}

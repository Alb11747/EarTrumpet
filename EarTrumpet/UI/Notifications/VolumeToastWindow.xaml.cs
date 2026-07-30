using EarTrumpet.Extensions;
using EarTrumpet.UI.Controls;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Win32.UI.WindowsAndMessaging;
using Screen = System.Windows.Forms.Screen;

namespace EarTrumpet.UI.Notifications;

internal partial class VolumeToastWindow : Window
{
    private const int c_wmMouseActivate = 0x0021;
    private const int c_maNoActivate = 3;
    private const double c_workAreaMargin = 24;

    private HwndSource _source;
    private Screen _targetScreen;
    private bool _positionPending;

    internal event Action<bool> UserActivity;
    internal event Action<bool> HoverChanged;

    public VolumeToastWindow()
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        DpiChanged += OnDpiChanged;
        Closed += OnClosed;
    }

    internal void SetVolume(VolumeToastViewModel viewModel)
    {
        ClearVolume();
        DataContext = viewModel;
    }

    internal void ClearVolume()
    {
        if (DataContext is VolumeToastViewModel viewModel)
        {
            DataContext = null;
            viewModel.Dispose();
        }
    }

    internal void Position(Screen screen)
    {
        _targetScreen = screen;

        var previousDpiX = this.DpiX();
        var previousDpiY = this.DpiY();
        PositionCore();
        UpdateLayout();

        if (this.DpiX() != previousDpiX || this.DpiY() != previousDpiY)
        {
            PositionCore();
        }
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        this.ApplyExtendedWindowStyle(
            WINDOW_EX_STYLE.WS_EX_NOACTIVATE |
            WINDOW_EX_STYLE.WS_EX_TOOLWINDOW);

        _source = HwndSource.FromHwnd(this.GetHandle());
        _source?.AddHook(WndProc);
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        if (_targetScreen == null || _positionPending)
        {
            return;
        }

        _positionPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
        {
            _positionPending = false;
            if (IsVisible && _targetScreen != null)
            {
                UpdateLayout();
                PositionCore();
            }
        }));
    }

    private void PositionCore()
    {
        if (_targetScreen == null)
        {
            return;
        }

        var dpiX = this.DpiX();
        var dpiY = this.DpiY();
        var width = (ActualWidth > 0 ? ActualWidth : Width) * dpiX;
        var height = (ActualHeight > 0 ? ActualHeight : MinHeight) * dpiY;
        var workArea = _targetScreen.WorkingArea;
        var left = workArea.Left + (c_workAreaMargin * dpiX);
        var top = workArea.Top + (c_workAreaMargin * dpiY);

        this.SetWindowPos(top, left, height, width);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == c_wmMouseActivate)
        {
            handled = true;
            return new IntPtr(c_maNoActivate);
        }

        return IntPtr.Zero;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        HoverChanged?.Invoke(true);
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        HoverChanged?.Invoke(false);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        UserActivity?.Invoke(IsInteractiveSource(e.OriginalSource as DependencyObject));
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        UserActivity?.Invoke(IsInteractiveSource(e.OriginalSource as DependencyObject));
    }

    private void Window_PreviewTouchDown(object sender, TouchEventArgs e)
    {
        UserActivity?.Invoke(IsInteractiveSource(e.OriginalSource as DependencyObject));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        UserActivity?.Invoke(true);

        if (DataContext is VolumeToastViewModel viewModel)
        {
            viewModel.IsMuted = !viewModel.IsMuted;
        }
    }

    private bool IsInteractiveSource(DependencyObject source)
    {
        if (source == null)
        {
            return false;
        }

        var slider = source as VolumeSlider ?? source.FindVisualParent<VolumeSlider>();
        if (ReferenceEquals(slider, VolumeSlider))
        {
            return true;
        }

        var button = source as Button ?? source.FindVisualParent<Button>();
        return ReferenceEquals(button, MuteButton);
    }

    private void OnClosed(object sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        DpiChanged -= OnDpiChanged;
        Closed -= OnClosed;

        ClearVolume();
        _source?.RemoveHook(WndProc);
        _source = null;
        _targetScreen = null;
        UserActivity = null;
        HoverChanged = null;
    }
}

using EarTrumpet.Extensions;
using EarTrumpet.UI.Notifications;
using EarTrumpet.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EarTrumpet.UI.Views;

public partial class AppItemView : UserControl
{
    private IAppItemViewModel App => (IAppItemViewModel)DataContext;

    public AppItemView()
    {
        InitializeComponent();

        PreviewMouseRightButtonUp += (_, __) => OpenPopup();
        Loaded += (_, __) =>
        {
            var container = this.FindVisualParent<ListViewItem>();
            if (container != null)
            {
                container.PreviewKeyDown += OnPreviewKeyDown;
            }
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Right:
                case Key.OemPlus:
                    ChangeVolume(10);
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.OemMinus:
                    ChangeVolume(-10);
                    e.Handled = true;
                    break;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.M:
            case Key.OemPeriod:
                var isMuted = !App.IsMuted;
                App.IsMuted = isMuted;
                VolumeToastService.ShowApp(App, GetContainingScreen(), isMuted);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.OemPlus:
                ChangeVolume(1);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.OemMinus:
                ChangeVolume(-1);
                e.Handled = true;
                break;
            case Key.Space:
                OpenPopup();
                e.Handled = true;
                break;
        }
    }

    private void ChangeVolume(float delta)
    {
        var oldVolume = App.Volume;
        App.Volume += delta;
        if (App.Volume != oldVolume)
        {
            VolumeToastService.ShowApp(App, GetContainingScreen());
        }
    }

    private System.Windows.Forms.Screen GetContainingScreen()
    {
        var window = Window.GetWindow(this);
        return window == null
            ? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position)
            : System.Windows.Forms.Screen.FromHandle(window.GetHandle());
    }

    private void OpenPopup()
    {
        var viewModel = Window.GetWindow(this).DataContext as IPopupHostViewModel;
        if (viewModel != null && App != null && !App.IsExpanded)
        {
            viewModel.OpenPopup(App, this);
        }
    }
}
